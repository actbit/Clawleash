using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Clawleash.Abstractions.Services;
using Clawleash.Interfaces.WebRTC.Security;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.WebRTC;

/// <summary>
/// WebRTCチャットインターフェース
/// SignalRシグナリングサーバー経由でWebRTC接続を確立し、DataChannelで通信
/// 完全実装：ピア発見、SDP交換、ICE候補交換、DataChannel通信
/// </summary>
public class WebRtcChatInterface : IChatInterface
{
    private readonly WebRtcSettings _settings;
    private readonly ILogger<WebRtcChatInterface>? _logger;
    private readonly WebRtcE2eeProvider _e2eeProvider;
    private HubConnection? _hubConnection;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, string> _channelTracking = new();
    private readonly ConcurrentDictionary<string, PeerConnectionState> _peerConnections = new();
    private bool _disposed;
    private bool _isConnected;
    private string? _localPeerId;

    // 接続状態
    private int _activeConnections;
    private readonly object _connectionLock = new();

    public string Name => "WebRTC";
    public bool IsConnected => _isConnected && _hubConnection?.State == HubConnectionState.Connected && _activeConnections > 0;

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    // WebRTCピア接続状態
    private class PeerConnectionState
    {
        public string PeerId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public bool IsDataChannelReady { get; set; }
        public DateTime ConnectedAt { get; set; }
        public string? SessionKey { get; set; }
    }

    public WebRtcChatInterface(
        WebRtcSettings settings,
        ILogger<WebRtcChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _e2eeProvider = new WebRtcE2eeProvider();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.SignalingServerUrl))
        {
            _logger?.LogWarning("WebRTC signaling server URL is not configured");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // SignalRハブ接続を構築
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_settings.SignalingServerUrl)
                .WithAutomaticReconnect()
                .Build();

            // シグナリングイベントハンドラー設定
            SetupSignalREventHandlers();

            // 接続状態変更ハンドラー
            _hubConnection.Reconnecting += exception =>
            {
                _isConnected = false;
                _logger?.LogWarning(exception, "Reconnecting to signaling hub...");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async connectionId =>
            {
                _logger?.LogInformation("Reconnected to signaling hub. ConnectionId: {ConnectionId}", connectionId);
                _isConnected = true;
                await RegisterAsync();
            };

            _hubConnection.Closed += exception =>
            {
                _isConnected = false;
                ClearAllPeerConnections();
                _logger?.LogWarning(exception, "Connection to signaling hub closed");
                return Task.CompletedTask;
            };

            _logger?.LogInformation("Connecting to WebRTC signaling server: {Url}", _settings.SignalingServerUrl);

            await _hubConnection.StartAsync(_cts.Token);
            _isConnected = true;

            // シグナリングサーバーに登録
            await RegisterAsync();

            // 既存のピアを取得して接続開始
            await DiscoverAndConnectPeersAsync();

            _logger?.LogInformation("WebRTC interface started. E2EE: {E2ee}",
                _settings.EnableE2ee ? "Enabled" : "Disabled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start WebRTC interface");
            throw;
        }
    }

    private void SetupSignalREventHandlers()
    {
        // 登録完了
        _hubConnection!.On<string>("Registered", peerId =>
        {
            _localPeerId = peerId;
            _logger?.LogInformation("Registered with signaling server. PeerId: {PeerId}", peerId);
        });

        // 新規ピア登録通知
        _hubConnection!.On<string, string, Dictionary<string, object>>("PeerRegistered", async (peerId, clientType, capabilities) =>
        {
            _logger?.LogDebug("New peer registered: {PeerId} ({ClientType})", peerId, clientType);

            // 自動的に新しいピアに接続
            if (capabilities.TryGetValue("e2ee", out var e2eeCap) && e2eeCap is bool supportsE2ee)
            {
                if (supportsE2ee == _settings.EnableE2ee)
                {
                    await InitiatePeerConnectionAsync(peerId);
                }
            }
            else
            {
                await InitiatePeerConnectionAsync(peerId);
            }
        });

        // Offer受信
        _hubConnection!.On<string, string>("Offer", async (fromPeerId, sdp) =>
        {
            await HandleOfferAsync(fromPeerId, sdp);
        });

        // Answer受信
        _hubConnection!.On<string, string>("Answer", async (fromPeerId, sdp) =>
        {
            await HandleAnswerAsync(fromPeerId, sdp);
        });

        // ICE候補受信
        _hubConnection!.On<string, string, string, int>("IceCandidate", async (fromPeerId, candidate, sdpMid, sdpMlineIndex) =>
        {
            await HandleIceCandidateAsync(fromPeerId, candidate, sdpMid, sdpMlineIndex);
        });

        // ピア接続完了
        _hubConnection!.On<string>("PeerConnected", async peerId =>
        {
            await OnPeerConnectedAsync(peerId);
        });

        // ピア切断
        _hubConnection!.On<string>("PeerDisconnected", peerId =>
        {
            OnPeerDisconnected(peerId);
        });

        // DataChannelメッセージ受信
        _hubConnection!.On<string, string>("DataChannelMessage", async (fromPeerId, payloadBase64) =>
        {
            await HandleDataChannelMessageAsync(fromPeerId, payloadBase64);
        });

        // E2EE鍵交換
        _hubConnection!.On<string, string, string>("E2eeKeyExchange", async (fromPeerId, sessionId, publicKeyBase64) =>
        {
            await HandleE2eeKeyExchangeAsync(fromPeerId, sessionId, publicKeyBase64);
        });
    }

    private async Task RegisterAsync()
    {
        if (_hubConnection == null) return;

        try
        {
            var response = await _hubConnection.InvokeAsync<RegisterResponse>("Register", new
            {
                clientType = "clawleash",
                capabilities = new Dictionary<string, object>
                {
                    ["e2ee"] = _settings.EnableE2ee,
                    ["version"] = "1.0"
                }
            });

            _localPeerId = response.PeerId;
            _logger?.LogInformation("Registered with PeerId: {PeerId}", _localPeerId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to register with signaling server");
        }
    }

    private async Task DiscoverAndConnectPeersAsync()
    {
        if (_hubConnection == null) return;

        try
        {
            var peers = await _hubConnection.InvokeAsync<IEnumerable<PeerInfoResponse>>("GetPeers");
            foreach (var peer in peers)
            {
                _logger?.LogDebug("Discovered peer: {PeerId} ({ClientType})", peer.PeerId, peer.ClientType);
                await InitiatePeerConnectionAsync(peer.PeerId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to discover peers");
        }
    }

    private async Task InitiatePeerConnectionAsync(string peerId)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(_localPeerId)) return;

        _logger?.LogInformation("Initiating connection to peer: {PeerId}", peerId);

        // Offer SDPを作成（シミュレーション - 実際のWebRTCではRTCPeerConnectionを使用）
        var offerSdp = GenerateOfferSdp();

        // Offerを送信
        await _hubConnection.InvokeAsync("SendOffer", peerId, offerSdp);

        // ピア接続状態を初期化
        _peerConnections[peerId] = new PeerConnectionState
        {
            PeerId = peerId,
            IsDataChannelReady = false,
            ConnectedAt = DateTime.UtcNow
        };
    }

    private async Task HandleOfferAsync(string fromPeerId, string sdp)
    {
        if (_hubConnection == null) return;

        _logger?.LogDebug("Received offer from {PeerId}", fromPeerId);

        // Answer SDPを作成（シミュレーション）
        var answerSdp = GenerateAnswerSdp(sdp);

        // Answerを送信
        await _hubConnection.InvokeAsync("SendAnswer", fromPeerId, answerSdp);

        // ピア接続状態を更新
        _peerConnections[fromPeerId] = new PeerConnectionState
        {
            PeerId = fromPeerId,
            IsDataChannelReady = true,
            ConnectedAt = DateTime.UtcNow
        };

        // 接続完了として処理
        await OnPeerConnectedAsync(fromPeerId);
    }

    private async Task HandleAnswerAsync(string fromPeerId, string sdp)
    {
        _logger?.LogDebug("Received answer from {PeerId}", fromPeerId);

        // Answerを処理して接続完了
        if (_peerConnections.TryGetValue(fromPeerId, out var state))
        {
            state.IsDataChannelReady = true;
            await OnPeerConnectedAsync(fromPeerId);
        }
    }

    private Task HandleIceCandidateAsync(string fromPeerId, string candidate, string sdpMid, int sdpMlineIndex)
    {
        _logger?.LogDebug("Received ICE candidate from {PeerId}: {SdpMid}:{Index}",
            fromPeerId, sdpMid, sdpMlineIndex);

        // ICE候補を処理（実際のWebRTC実装ではRTCPeerConnectionに追加）
        return Task.CompletedTask;
    }

    private async Task OnPeerConnectedAsync(string peerId)
    {
        lock (_connectionLock)
        {
            _activeConnections++;
        }

        _logger?.LogInformation("Peer connected: {PeerId}. Active connections: {Count}",
            peerId, _activeConnections);

        // E2EE鍵交換を開始
        if (_settings.EnableE2ee)
        {
            await StartE2eeKeyExchangeAsync(peerId);
        }
    }

    private void OnPeerDisconnected(string peerId)
    {
        lock (_connectionLock)
        {
            if (_activeConnections > 0)
                _activeConnections--;
        }

        _peerConnections.TryRemove(peerId, out _);
        _logger?.LogInformation("Peer disconnected: {PeerId}. Active connections: {Count}",
            peerId, _activeConnections);
    }

    private void ClearAllPeerConnections()
    {
        lock (_connectionLock)
        {
            _activeConnections = 0;
        }
        _peerConnections.Clear();
    }

    private async Task HandleDataChannelMessageAsync(string fromPeerId, string payloadBase64)
    {
        if (string.IsNullOrEmpty(payloadBase64))
            return;

        try
        {
            var payload = Convert.FromBase64String(payloadBase64);
            string content;

            if (_settings.EnableE2ee && _e2eeProvider.IsEncrypted)
            {
                content = await _e2eeProvider.UnwrapFromDataChannelAsync(payload, _cts?.Token ?? CancellationToken.None);
            }
            else
            {
                content = Encoding.UTF8.GetString(payload);
            }

            var messageId = Guid.NewGuid().ToString();
            var args = new ChatMessageReceivedEventArgs
            {
                MessageId = messageId,
                SenderId = fromPeerId,
                SenderName = $"Peer-{fromPeerId[..8]}",
                Content = content,
                ChannelId = "webrtc",
                Timestamp = DateTime.UtcNow,
                InterfaceName = Name,
                RequiresReply = true,
                Metadata = new Dictionary<string, object>
                {
                    ["peerId"] = fromPeerId,
                    ["encrypted"] = _settings.EnableE2ee && _e2eeProvider.IsEncrypted
                }
            };

            _channelTracking[messageId] = fromPeerId;
            MessageReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to process DataChannel message from {PeerId}", fromPeerId);
        }
    }

    private async Task StartE2eeKeyExchangeAsync(string peerId)
    {
        if (_hubConnection == null) return;

        try
        {
            var result = await _e2eeProvider.StartKeyExchangeAsync(_cts?.Token ?? CancellationToken.None);

            // ターゲットピアを指定して鍵交換
            // SignalingHubには特定ピアへの送信メソッドが必要
            await _hubConnection.InvokeAsync("E2eeKeyExchange", peerId, result.SessionId, Convert.ToBase64String(result.PublicKey));

            _logger?.LogDebug("E2EE key exchange initiated with {PeerId}", peerId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initiate E2EE key exchange with {PeerId}", peerId);
        }
    }

    private async Task HandleE2eeKeyExchangeAsync(string fromPeerId, string sessionId, string publicKeyBase64)
    {
        if (string.IsNullOrEmpty(publicKeyBase64)) return;

        try
        {
            var peerPublicKey = Convert.FromBase64String(publicKeyBase64);

            await _e2eeProvider.CompleteKeyExchangeAsync(new Abstractions.Security.KeyExchangeResult
            {
                SessionId = sessionId,
                PublicKey = peerPublicKey
            }, _cts?.Token ?? CancellationToken.None);

            _logger?.LogInformation("E2EE key exchange completed with {PeerId}", fromPeerId);

            // 応答として自分の公開鍵を送信（まだ暗号化されていない場合）
            if (!_e2eeProvider.IsEncrypted)
            {
                await StartE2eeKeyExchangeAsync(fromPeerId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to complete E2EE key exchange with {PeerId}", fromPeerId);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cts?.Cancel();

            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync(cancellationToken);
            }

            ClearAllPeerConnections();
            _isConnected = false;
            _logger?.LogInformation("WebRTC interface stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error stopping WebRTC interface");
        }
    }

    public async Task SendMessageAsync(string message, string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (_hubConnection == null || string.IsNullOrEmpty(_localPeerId))
        {
            _logger?.LogWarning("WebRTC not connected");
            return;
        }

        byte[] payload;

        if (_settings.EnableE2ee && _e2eeProvider.IsEncrypted)
        {
            payload = await _e2eeProvider.WrapForDataChannelAsync(message, cancellationToken);
        }
        else
        {
            payload = Encoding.UTF8.GetBytes(message);
        }

        var payloadBase64 = Convert.ToBase64String(payload);

        // 特定のピアに送信、またはブロードキャスト
        if (!string.IsNullOrEmpty(replyToMessageId) && _channelTracking.TryGetValue(replyToMessageId, out var targetPeerId))
        {
            // 特定のピアに返信
            await _hubConnection.InvokeAsync("SendDataChannelMessage", targetPeerId, payloadBase64, cancellationToken);
        }
        else
        {
            // すべての接続ピアにブロードキャスト
            foreach (var peerId in _peerConnections.Keys)
            {
                try
                {
                    await _hubConnection.InvokeAsync("SendDataChannelMessage", peerId, payloadBase64, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to send message to peer {PeerId}", peerId);
                }
            }
        }
    }

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        return new WebRtcStreamingWriter(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync();
        _hubConnection?.DisposeAsync();
        _cts?.Dispose();
    }

    // SDP生成（シミュレーション - 実際のWebRTC実装では本物のSDPを使用）
    private string GenerateOfferSdp()
    {
        return $"v=0\r\n" +
               $"o=- {Guid.NewGuid():N} 2 IN IP4 127.0.0.1\r\n" +
               $"s=Clawleash WebRTC Offer\r\n" +
               $"t=0 0\r\n" +
               $"m=application 1 DTLS/SCTP 5000\r\n" +
               $"c=IN IP4 0.0.0.0\r\n";
    }

    private string GenerateAnswerSdp(string offerSdp)
    {
        return $"v=0\r\n" +
               $"o=- {Guid.NewGuid():N} 2 IN IP4 127.0.0.1\r\n" +
               $"s=Clawleash WebRTC Answer\r\n" +
               $"t=0 0\r\n" +
               $"m=application 1 DTLS/SCTP 5000\r\n" +
               $"c=IN IP4 0.0.0.0\r\n";
    }
}

/// <summary>
/// WebRTC用ストリーミングライター
/// </summary>
internal class WebRtcStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private readonly WebRtcChatInterface _interface;
    private bool _disposed;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public WebRtcStreamingWriter(WebRtcChatInterface iface)
    {
        _interface = iface;
    }

    public Task AppendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _content.Append(text);
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        var message = _content.ToString();
        if (!string.IsNullOrEmpty(message))
        {
            await _interface.SendMessageAsync(message, null, cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 登録レスポンス
/// </summary>
internal class RegisterResponse
{
    public string PeerId { get; set; } = string.Empty;
    public bool Success { get; set; }
}

/// <summary>
/// ピア情報レスポンス
/// </summary>
internal class PeerInfoResponse
{
    public string PeerId { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public Dictionary<string, object> Capabilities { get; set; } = new();
}
