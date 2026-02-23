using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Clawleash.Abstractions.Services;
using Clawleash.Interfaces.WebRTC.Security;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.WebRTC;

/// <summary>
/// WebRTCチャットインターフェース
/// シグナリングサーバー経由でWebRTC接続を確立し、DataChannelで通信
/// </summary>
public class WebRtcChatInterface : IChatInterface
{
    private readonly WebRtcSettings _settings;
    private readonly ILogger<WebRtcChatInterface>? _logger;
    private readonly WebRtcE2eeProvider _e2eeProvider;
    private ClientWebSocket? _signalingClient;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, string> _channelTracking = new();
    private bool _disposed;
    private bool _isConnected;
    private string? _peerId;

    // WebRTC connection state (simplified - in production use a proper WebRTC library)
    private bool _dataChannelReady;

    public string Name => "WebRTC";
    public bool IsConnected => _isConnected && _dataChannelReady;

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
            // Connect to signaling server
            _signalingClient = new ClientWebSocket();
            var uri = new Uri(_settings.SignalingServerUrl);

            _logger?.LogInformation("Connecting to WebRTC signaling server: {Url}", _settings.SignalingServerUrl);

            await _signalingClient.ConnectAsync(uri, _cts.Token);
            _isConnected = true;

            // Register with signaling server
            await SendSignalingMessageAsync(new
            {
                type = "register",
                clientType = "clawleash",
                capabilities = new { e2ee = _settings.EnableE2ee }
            }, _cts.Token);

            // Start signaling message loop
            _ = SignalingLoopAsync(_cts.Token);

            _logger?.LogInformation("WebRTC interface started. E2EE: {E2ee}",
                _settings.EnableE2ee ? "Enabled" : "Disabled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start WebRTC interface");
            throw;
        }
    }

    private async Task SignalingLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            while (!ct.IsCancellationRequested && _signalingClient?.State == WebSocketState.Open)
            {
                var result = await _signalingClient.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger?.LogInformation("Signaling server closed connection");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessSignalingMessageAsync(json, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in signaling loop");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ProcessSignalingMessageAsync(string json, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "registered":
                    _peerId = root.GetProperty("peerId").GetString();
                    _logger?.LogInformation("Registered with signaling server. PeerId: {PeerId}", _peerId);
                    break;

                case "offer":
                    await HandleOfferAsync(root, ct);
                    break;

                case "answer":
                    await HandleAnswerAsync(root, ct);
                    break;

                case "ice-candidate":
                    await HandleIceCandidateAsync(root, ct);
                    break;

                case "peer-connected":
                    _dataChannelReady = true;
                    _logger?.LogInformation("Peer connected, data channel ready");

                    // Start E2EE key exchange if enabled
                    if (_settings.EnableE2ee)
                    {
                        await StartE2eeKeyExchangeAsync(ct);
                    }
                    break;

                case "peer-disconnected":
                    _dataChannelReady = false;
                    _logger?.LogInformation("Peer disconnected");
                    break;

                case "datachannel-message":
                    await HandleDataChannelMessageAsync(root, ct);
                    break;

                case "e2ee-key-exchange":
                    await HandleE2eeKeyExchangeAsync(root, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to process signaling message");
        }
    }

    private async Task HandleOfferAsync(JsonElement root, CancellationToken ct)
    {
        var sdp = root.GetProperty("sdp").GetString();
        _logger?.LogDebug("Received WebRTC offer");

        // In a real implementation, we would create a WebRTC peer connection
        // and generate an answer. For now, we simulate the connection.
        await SendSignalingMessageAsync(new
        {
            type = "answer",
            sdp = "v=0\r\n...", // Placeholder SDP
            fromPeerId = _peerId
        }, ct);
    }

    private Task HandleAnswerAsync(JsonElement root, CancellationToken ct)
    {
        _logger?.LogDebug("Received WebRTC answer");
        return Task.CompletedTask;
    }

    private Task HandleIceCandidateAsync(JsonElement root, CancellationToken ct)
    {
        _logger?.LogDebug("Received ICE candidate");
        return Task.CompletedTask;
    }

    private async Task HandleDataChannelMessageAsync(JsonElement root, CancellationToken ct)
    {
        var fromPeerId = root.GetProperty("fromPeerId").GetString();
        var payloadBase64 = root.TryGetProperty("payload", out var payloadEl) ? payloadEl.GetString() : null;

        if (string.IsNullOrEmpty(payloadBase64))
            return;

        var payload = Convert.FromBase64String(payloadBase64);
        string content;

        if (_settings.EnableE2ee && _e2eeProvider.IsEncrypted)
        {
            content = await _e2eeProvider.UnwrapFromDataChannelAsync(payload, ct);
        }
        else
        {
            content = Encoding.UTF8.GetString(payload);
        }

        var messageId = Guid.NewGuid().ToString();
        var args = new ChatMessageReceivedEventArgs
        {
            MessageId = messageId,
            SenderId = fromPeerId ?? "unknown",
            SenderName = fromPeerId ?? "Peer",
            Content = content,
            ChannelId = "webrtc",
            Timestamp = DateTime.UtcNow,
            InterfaceName = Name,
            Metadata = new Dictionary<string, object>
            {
                ["peerId"] = fromPeerId ?? "",
                ["encrypted"] = _settings.EnableE2ee && _e2eeProvider.IsEncrypted
            }
        };

        _channelTracking[messageId] = "webrtc";
        MessageReceived?.Invoke(this, args);
    }

    private async Task StartE2eeKeyExchangeAsync(CancellationToken ct)
    {
        var result = await _e2eeProvider.StartKeyExchangeAsync(ct);

        await SendSignalingMessageAsync(new
        {
            type = "e2ee-key-exchange",
            sessionId = result.SessionId,
            publicKey = Convert.ToBase64String(result.PublicKey)
        }, ct);

        _logger?.LogInformation("E2EE key exchange initiated");
    }

    private async Task HandleE2eeKeyExchangeAsync(JsonElement root, CancellationToken ct)
    {
        var sessionId = root.GetProperty("sessionId").GetString();
        var publicKeyBase64 = root.GetProperty("publicKey").GetString();

        if (!string.IsNullOrEmpty(publicKeyBase64))
        {
            var peerPublicKey = Convert.FromBase64String(publicKeyBase64);

            await _e2eeProvider.CompleteKeyExchangeAsync(new Abstractions.Security.KeyExchangeResult
            {
                SessionId = sessionId ?? "",
                PublicKey = peerPublicKey
            }, ct);

            _logger?.LogInformation("E2EE key exchange completed");

            // If we initiated, send our public key back
            if (!_e2eeProvider.IsEncrypted)
            {
                await StartE2eeKeyExchangeAsync(ct);
            }
        }
    }

    private async Task SendSignalingMessageAsync(object message, CancellationToken ct)
    {
        if (_signalingClient?.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _signalingClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cts?.Cancel();

            if (_signalingClient?.State == WebSocketState.Open)
            {
                await _signalingClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }

            _isConnected = false;
            _dataChannelReady = false;
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
        if (!IsConnected || string.IsNullOrEmpty(_peerId))
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

        await SendSignalingMessageAsync(new
        {
            type = "datachannel-send",
            toPeerId = "*", // Broadcast or specific peer
            payload = Convert.ToBase64String(payload)
        }, cancellationToken);
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
        _signalingClient?.Dispose();
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
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
