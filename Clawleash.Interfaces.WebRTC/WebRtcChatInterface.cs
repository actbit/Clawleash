using System.Collections.Concurrent;
using System.Text;
using Clawleash.Abstractions.Services;
using Clawleash.Interfaces.WebRTC.Security;
using Lucid.Rtc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.WebRTC;

/// <summary>
/// WebRTCチャットインターフェース
/// SignalRシグナリングサーバー経由でWebRTC接続を確立し、DataChannelで通信
/// Lucid.Rtc高レベルAPIを使用
/// </summary>
public class WebRtcChatInterface : IChatInterface
{
    private readonly WebRtcSettings _settings;
    private readonly ILogger<WebRtcChatInterface>? _logger;
    private readonly WebRtcE2eeProvider _e2eeProvider;
    private HubConnection? _hubConnection;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, string> _channelTracking = new();
    private bool _disposed;
    private bool _isConnected;
    private string? _localPeerId;

    // Lucid.Rtc high-level API
    private RtcConnection? _rtcConnection;
    private readonly ConcurrentDictionary<string, Peer> _peers = new();

    // 接続状態
    private int _activeConnections;
    private readonly object _connectionLock = new();

    public string Name => "WebRTC";
    public bool IsConnected => _isConnected && _hubConnection?.State == HubConnectionState.Connected && _activeConnections > 0;

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

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
            // Initialize Lucid.Rtc connection
            InitializeRtcConnection();

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

            _logger?.LogInformation("WebRTC interface started. E2EE: {E2ee}, Backend: Lucid.Rtc",
                _settings.EnableE2ee ? "Enabled" : "Disabled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start WebRTC interface");
            throw;
        }
    }

    private void InitializeRtcConnection()
    {
        var builder = new RtcConnectionBuilder();

        // STUN servers
        foreach (var stunServer in _settings.StunServers)
        {
            builder.WithStunServer(stunServer);
        }

        // TURN server (optional)
        if (!string.IsNullOrEmpty(_settings.TurnServerUrl))
        {
            builder.WithTurnServer(
                _settings.TurnServerUrl,
                _settings.TurnUsername ?? "",
                _settings.TurnPassword ?? "");
        }

        // Other settings
        builder
            .WithIceConnectionTimeout(_settings.IceConnectionTimeoutMs)
            .WithDataChannelReliable(_settings.DataChannelReliable);

        _rtcConnection = builder.Build();

        // Register event handlers with method chaining
        _rtcConnection
            .On<Lucid.Rtc.PeerConnectedEvent>(e => OnPeerConnected(e.PeerId, e.Peer))
            .On<Lucid.Rtc.PeerDisconnectedEvent>(e => OnPeerDisconnected(e.PeerId))
            .On<Lucid.Rtc.MessageReceivedEvent>(e => HandleMessage(e.PeerId, e.Data))
            .On<Lucid.Rtc.IceCandidateEvent>(e => SendIceCandidate(e.PeerId, e.Candidate))
            .On<Lucid.Rtc.OfferReadyEvent>(e => SendOffer(e.PeerId, e.Sdp))
            .On<Lucid.Rtc.AnswerReadyEvent>(e => SendAnswer(e.PeerId, e.Sdp))
            .On<Lucid.Rtc.DataChannelOpenEvent>(e => OnDataChannelOpen(e.PeerId, e.Peer))
            .On<Lucid.Rtc.DataChannelClosedEvent>(e => OnDataChannelClosed(e.PeerId, e.Peer))
            .On<Lucid.Rtc.ErrorEvent>(e => _logger?.LogError("Lucid.Rtc error: {Message}", e.Message));

        _logger?.LogInformation("Lucid.Rtc connection initialized");
    }

    private void OnPeerConnected(string peerId, Peer peer)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                lock (_connectionLock)
                {
                    _activeConnections++;
                }

                _peers[peerId] = peer;

                _logger?.LogInformation("Peer connected: {PeerId}. Active connections: {Count}",
                    peerId, _activeConnections);

                // E2EE鍵交換を開始
                if (_settings.EnableE2ee)
                {
                    await StartE2eeKeyExchangeAsync(peerId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error handling peer connected event for {PeerId}", peerId);
            }
        });
    }

    private void OnPeerDisconnected(string peerId)
    {
        lock (_connectionLock)
        {
            if (_activeConnections > 0)
                _activeConnections--;
        }

        _peers.TryRemove(peerId, out _);

        _logger?.LogInformation("Peer disconnected: {PeerId}. Active connections: {Count}",
            peerId, _activeConnections);
    }

    private void HandleMessage(string peerId, byte[] data)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleDataChannelMessageAsync(peerId, data);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error handling message from {PeerId}", peerId);
            }
        });
    }

    private void SendIceCandidate(string peerId, IceCandidate candidate)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SendIceCandidateToSignalingAsync(peerId, candidate);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error sending ICE candidate for {PeerId}", peerId);
            }
        });
    }

    private void SendOffer(string peerId, string sdp)
    {
        _ = Task.Run(async () =>
        {
            if (_hubConnection == null) return;

            try
            {
                await _hubConnection.InvokeAsync("SendOffer", peerId, sdp);
                _logger?.LogDebug("Sent offer to {PeerId}", peerId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error sending offer for {PeerId}", peerId);
            }
        });
    }

    private void SendAnswer(string peerId, string sdp)
    {
        _ = Task.Run(async () =>
        {
            if (_hubConnection == null) return;

            try
            {
                await _hubConnection.InvokeAsync("SendAnswer", peerId, sdp);
                _logger?.LogDebug("Sent answer to {PeerId}", peerId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error sending answer for {PeerId}", peerId);
            }
        });
    }

    private void OnDataChannelOpen(string peerId, Peer peer)
    {
        _logger?.LogInformation("DataChannel opened with peer {PeerId}", peerId);

        lock (_connectionLock)
        {
            _activeConnections++;
        }

        if (_settings.EnableE2ee)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await StartE2eeKeyExchangeAsync(peerId);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error starting E2EE key exchange with {PeerId}", peerId);
                }
            });
        }
    }

    private void OnDataChannelClosed(string peerId, Peer peer)
    {
        _logger?.LogInformation("DataChannel closed with peer {PeerId}", peerId);

        lock (_connectionLock)
        {
            if (_activeConnections > 0)
                _activeConnections--;
        }
    }

    private async Task SendIceCandidateToSignalingAsync(string peerId, IceCandidate candidate)
    {
        if (_hubConnection == null) return;

        try
        {
            await _hubConnection.InvokeAsync("SendIceCandidate",
                peerId, candidate.Candidate, candidate.SdpMid, candidate.SdpMlineIndex);
            _logger?.LogDebug("Sent ICE candidate to signaling server for peer {PeerId}", peerId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send ICE candidate for peer {PeerId}", peerId);
        }
    }

    private void SetupSignalREventHandlers()
    {
        // 登録完了
        _hubConnection!.On<RegisteredEvent>("registered", data =>
        {
            _localPeerId = data.PeerId;
            _logger?.LogInformation("Registered with signaling server. PeerId: {PeerId}", data.PeerId);
        });

        // 新規ピア登録通知
        _hubConnection!.On<PeerRegisteredEvent>("peer-registered", async data =>
        {
            _logger?.LogDebug("New peer registered: {PeerId} ({ClientType})", data.PeerId, data.ClientType);

            // 自動的に新しいピアに接続
            if (data.Capabilities != null &&
                data.Capabilities.TryGetValue("e2ee", out var e2eeCap) && e2eeCap is bool supportsE2ee)
            {
                if (supportsE2ee == _settings.EnableE2ee)
                {
                    await InitiatePeerConnectionAsync(data.PeerId);
                }
            }
            else
            {
                await InitiatePeerConnectionAsync(data.PeerId);
            }
        });

        // Offer受信
        _hubConnection!.On<OfferEvent>("offer", async data =>
        {
            await HandleOfferAsync(data.FromPeerId, data.Sdp);
        });

        // Answer受信
        _hubConnection!.On<AnswerEvent>("answer", async data =>
        {
            await HandleAnswerAsync(data.FromPeerId, data.Sdp);
        });

        // ICE候補受信
        _hubConnection!.On<SignalRIceCandidateEvent>("ice-candidate", async data =>
        {
            await HandleIceCandidateAsync(data.FromPeerId, data.Candidate, data.SdpMid, data.SdpMlineIndex);
        });

        // ピア接続完了
        _hubConnection!.On<PeerConnectedSignalREvent>("peer-connected", async data =>
        {
            await OnPeerConnectedSignalRAsync(data.PeerId);
        });

        // ピア切断
        _hubConnection!.On<PeerDisconnectedSignalREvent>("peer-disconnected", data =>
        {
            OnPeerDisconnectedSignalR(data.PeerId);
        });

        // E2EE鍵交換
        _hubConnection!.On<E2eeKeyExchangeEvent>("e2ee-key-exchange", async data =>
        {
            await HandleE2eeKeyExchangeAsync(data.FromPeerId, data.SessionId, data.PublicKey);
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
                    ["version"] = "1.0",
                    ["backend"] = "lucid.rtc"
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
        if (_rtcConnection == null || string.IsNullOrEmpty(_localPeerId)) return;

        _logger?.LogInformation("Initiating connection to peer: {PeerId}", peerId);

        try
        {
            // Lucid.Rtc高レベルAPIでピア作成
            var peer = await _rtcConnection.CreatePeerAsync(peerId);
            _peers[peerId] = peer;

            // Offerは自動的に生成され、OfferReadyEventで送信される
            _logger?.LogDebug("Created peer for {PeerId}, waiting for offer", peerId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initiate connection to {PeerId}", peerId);
        }
    }

    private async Task HandleOfferAsync(string fromPeerId, string sdp)
    {
        if (_rtcConnection == null) return;

        _logger?.LogDebug("Received offer from {PeerId}", fromPeerId);

        try
        {
            // 既存のピアを取得、または新規作成
            Peer peer;
            if (_peers.TryGetValue(fromPeerId, out var existingPeer))
            {
                peer = existingPeer;
            }
            else
            {
                peer = await _rtcConnection.CreatePeerAsync(fromPeerId);
                _peers[fromPeerId] = peer;
            }

            // Offerを設定（Answerは自動的に生成され、AnswerReadyEventで送信される）
            peer.SetRemoteOffer(sdp);
            _logger?.LogDebug("Set remote offer for {PeerId}", fromPeerId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to handle offer from {PeerId}", fromPeerId);
        }
    }

    private Task HandleAnswerAsync(string fromPeerId, string sdp)
    {
        _logger?.LogDebug("Received answer from {PeerId}", fromPeerId);

        if (_peers.TryGetValue(fromPeerId, out var peer))
        {
            try
            {
                peer.SetRemoteAnswer(sdp);
                _logger?.LogDebug("Set remote answer for {PeerId}", fromPeerId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to set remote answer for {PeerId}", fromPeerId);
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleIceCandidateAsync(string fromPeerId, string candidate, string sdpMid, int sdpMlineIndex)
    {
        _logger?.LogDebug("Received ICE candidate from {PeerId}: {SdpMid}:{Index}",
            fromPeerId, sdpMid, sdpMlineIndex);

        if (_peers.TryGetValue(fromPeerId, out var peer))
        {
            try
            {
                var iceCandidate = new IceCandidate
                {
                    Candidate = candidate,
                    SdpMid = sdpMid,
                    SdpMlineIndex = sdpMlineIndex
                };
                peer.AddIceCandidate(iceCandidate);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to add ICE candidate for {PeerId}", fromPeerId);
            }
        }

        return Task.CompletedTask;
    }

    private Task OnPeerConnectedSignalRAsync(string peerId)
    {
        lock (_connectionLock)
        {
            _activeConnections++;
        }

        _logger?.LogInformation("Peer connected (SignalR): {PeerId}. Active connections: {Count}",
            peerId, _activeConnections);

        return Task.CompletedTask;
    }

    private void OnPeerDisconnectedSignalR(string peerId)
    {
        lock (_connectionLock)
        {
            if (_activeConnections > 0)
                _activeConnections--;
        }

        _peers.TryRemove(peerId, out _);

        _logger?.LogInformation("Peer disconnected (SignalR): {PeerId}. Active connections: {Count}",
            peerId, _activeConnections);
    }

    private void ClearAllPeerConnections()
    {
        lock (_connectionLock)
        {
            _activeConnections = 0;
        }

        // Close all peers
        foreach (var kvp in _peers)
        {
            try
            {
                kvp.Value.CloseAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error closing peer {PeerId}", kvp.Key);
            }
        }

        _peers.Clear();
    }

    private async Task HandleDataChannelMessageAsync(string fromPeerId, byte[] payload)
    {
        if (payload == null || payload.Length == 0)
            return;

        try
        {
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
                SenderName = $"Peer-{fromPeerId[..Math.Min(8, fromPeerId.Length)]}",
                Content = content,
                ChannelId = "webrtc",
                Timestamp = DateTime.UtcNow,
                InterfaceName = Name,
                RequiresReply = true,
                Metadata = new Dictionary<string, object>
                {
                    ["peerId"] = fromPeerId,
                    ["encrypted"] = _settings.EnableE2ee && _e2eeProvider.IsEncrypted,
                    ["backend"] = "lucid.rtc"
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
        if (_rtcConnection == null || string.IsNullOrEmpty(_localPeerId))
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

        // 特定のピアに送信（返信の場合）
        if (!string.IsNullOrEmpty(replyToMessageId) && _channelTracking.TryGetValue(replyToMessageId, out var targetPeerId))
        {
            if (_peers.TryGetValue(targetPeerId, out var peer))
            {
                try
                {
                    peer.Send(payload);
                    _logger?.LogDebug("Sent message to peer {PeerId}", targetPeerId);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to send message to peer {PeerId}", targetPeerId);
                }
            }
        }
        else
        {
            // 全ピアにブロードキャスト
            try
            {
                _rtcConnection.Broadcast(payload);
                _logger?.LogDebug("Broadcast message to all peers");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to broadcast message");
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

        if (_rtcConnection != null)
        {
            await _rtcConnection.DisposeAsync();
            _rtcConnection = null;
        }

        _hubConnection?.DisposeAsync();
        _cts?.Dispose();
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
        if (_disposed)
            return ValueTask.CompletedTask;

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

/// <summary>
/// SignalRイベント用DTO
/// </summary>
internal class RegisteredEvent
{
    public string PeerId { get; set; } = string.Empty;
    public bool Success { get; set; }
}

internal class PeerRegisteredEvent
{
    public string PeerId { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public Dictionary<string, object>? Capabilities { get; set; }
}

internal class OfferEvent
{
    public string FromPeerId { get; set; } = string.Empty;
    public string Sdp { get; set; } = string.Empty;
}

internal class AnswerEvent
{
    public string FromPeerId { get; set; } = string.Empty;
    public string Sdp { get; set; } = string.Empty;
}

internal class SignalRIceCandidateEvent
{
    public string FromPeerId { get; set; } = string.Empty;
    public string Candidate { get; set; } = string.Empty;
    public string SdpMid { get; set; } = string.Empty;
    public int SdpMlineIndex { get; set; }
}

internal class PeerConnectedSignalREvent
{
    public string PeerId { get; set; } = string.Empty;
    public string? RemotePeerId { get; set; }
}

internal class PeerDisconnectedSignalREvent
{
    public string PeerId { get; set; } = string.Empty;
}

internal class E2eeKeyExchangeEvent
{
    public string FromPeerId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
}
