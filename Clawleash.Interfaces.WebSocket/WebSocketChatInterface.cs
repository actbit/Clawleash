using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Clawleash.Abstractions.Services;
using Clawleash.Interfaces.WebSocket.Security;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.WebSocket;

/// <summary>
/// WebSocketチャットインターフェース（E2EE対応）
/// </summary>
public class WebSocketChatInterface : IChatInterface
{
    private readonly WebSocketSettings _settings;
    private readonly ILogger<WebSocketChatInterface>? _logger;
    private readonly AesGcmE2eeProvider _e2eeProvider;
    private readonly Dictionary<string, string> _channelTracking = new();
    private ClientWebSocket? _client;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isConnected;

    public string Name => "WebSocket";
    public bool IsConnected => _isConnected && (_client?.State == WebSocketState.Open);

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public WebSocketChatInterface(
        WebSocketSettings settings,
        ILogger<WebSocketChatInterface>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _e2eeProvider = new AesGcmE2eeProvider();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.ServerUrl))
        {
            _logger?.LogWarning("WebSocket server URL is not configured");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _client = new ClientWebSocket();

        try
        {
            var uri = new Uri(_settings.ServerUrl);
            _logger?.LogInformation("Connecting to WebSocket server: {Url}", _settings.ServerUrl);

            await _client.ConnectAsync(uri, _cts.Token);
            _isConnected = true;

            // E2EE有効時は鍵交換を実行
            if (_settings.EnableE2ee)
            {
                await PerformKeyExchangeAsync(_cts.Token);
            }

            _logger?.LogInformation("WebSocket connected. E2EE: {E2ee}", _settings.EnableE2ee ? "Enabled" : "Disabled");

            // メッセージ受信ループ開始
            _ = ReceiveLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to WebSocket server");
            _isConnected = false;

            // 再接続試行
            if (_settings.MaxReconnectAttempts > 0)
            {
                _ = ReconnectAsync(_cts.Token);
            }
        }
    }

    private async Task PerformKeyExchangeAsync(CancellationToken ct)
    {
        try
        {
            // 鍵交換開始
            var keyExchangeResult = await _e2eeProvider.StartKeyExchangeAsync(ct);

            // 公開鍵をサーバーに送信
            var keyExchangeMessage = new
            {
                type = "key_exchange",
                sessionId = keyExchangeResult.SessionId,
                publicKey = Convert.ToBase64String(keyExchangeResult.PublicKey)
            };

            await SendRawMessageAsync(JsonSerializer.Serialize(keyExchangeMessage), ct);

            // サーバーからの応答を待機（ReceiveLoopで処理）
            _logger?.LogInformation("Key exchange initiated. SessionId: {SessionId}", keyExchangeResult.SessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Key exchange failed");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            while (!ct.IsCancellationRequested && _client?.State == WebSocketState.Open)
            {
                var result = await _client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger?.LogInformation("WebSocket close received");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessMessageAsync(json, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常終了
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in receive loop");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _isConnected = false;
        }
    }

    private async Task ProcessMessageAsync(string json, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "key_exchange_response":
                    await HandleKeyExchangeResponseAsync(root, ct);
                    break;

                case "message":
                    await HandleChatMessageAsync(root, ct);
                    break;

                case "ping":
                    await SendPongAsync(ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to process message: {Json}", json);
        }
    }

    private async Task HandleKeyExchangeResponseAsync(JsonElement root, CancellationToken ct)
    {
        try
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

                _logger?.LogInformation("Key exchange completed. E2EE active.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to complete key exchange");
        }
    }

    private async Task HandleChatMessageAsync(JsonElement root, CancellationToken ct)
    {
        var content = root.GetProperty("content").GetString() ?? "";
        var messageId = root.TryGetProperty("messageId", out var msgIdEl) ? msgIdEl.GetString() ?? "" : Guid.NewGuid().ToString();
        var senderId = root.TryGetProperty("senderId", out var senderEl) ? senderEl.GetString() ?? "" : "unknown";
        var senderName = root.TryGetProperty("senderName", out var nameEl) ? nameEl.GetString() ?? "" : senderId;
        var channelId = root.TryGetProperty("channelId", out var chEl) ? chEl.GetString() ?? "" : "default";

        // E2EE有効時は復号化
        if (_settings.EnableE2ee && _e2eeProvider.IsEncrypted)
        {
            if (root.TryGetProperty("encrypted", out var encryptedEl) && encryptedEl.GetBoolean())
            {
                var ciphertextBase64 = root.GetProperty("ciphertext").GetString();
                if (!string.IsNullOrEmpty(ciphertextBase64))
                {
                    var ciphertext = Convert.FromBase64String(ciphertextBase64);
                    content = await _e2eeProvider.DecryptAsync(ciphertext, ct);
                }
            }
        }

        var args = new ChatMessageReceivedEventArgs
        {
            MessageId = messageId,
            SenderId = senderId,
            SenderName = senderName,
            Content = content,
            ChannelId = channelId,
            Timestamp = DateTime.UtcNow,
            InterfaceName = Name,
            Metadata = new Dictionary<string, object>
            {
                ["encrypted"] = _settings.EnableE2ee && _e2eeProvider.IsEncrypted
            }
        };

        _channelTracking[messageId] = channelId;

        MessageReceived?.Invoke(this, args);
    }

    private async Task SendPongAsync(CancellationToken ct)
    {
        var pong = JsonSerializer.Serialize(new { type = "pong" });
        await SendRawMessageAsync(pong, ct);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cts?.Cancel();

            if (_client?.State == WebSocketState.Open)
            {
                await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }

            _isConnected = false;
            _logger?.LogInformation("WebSocket disconnected");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disconnecting WebSocket");
        }
    }

    public async Task SendMessageAsync(string message, string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger?.LogWarning("WebSocket is not connected");
            return;
        }

        string? channelId = null;
        if (!string.IsNullOrEmpty(replyToMessageId) && _channelTracking.TryGetValue(replyToMessageId, out var trackedChannel))
        {
            channelId = trackedChannel;
        }

        var payload = new Dictionary<string, object>
        {
            ["type"] = "message",
            ["content"] = message,
            ["messageId"] = Guid.NewGuid().ToString(),
            ["channelId"] = channelId ?? "default"
        };

        // E2EE有効時は暗号化
        if (_settings.EnableE2ee && _e2eeProvider.IsEncrypted)
        {
            var ciphertext = await _e2eeProvider.EncryptAsync(message, cancellationToken);
            payload["content"] = "";
            payload["encrypted"] = true;
            payload["ciphertext"] = Convert.ToBase64String(ciphertext);
        }

        var json = JsonSerializer.Serialize(payload);
        await SendRawMessageAsync(json, cancellationToken);
    }

    private async Task SendRawMessageAsync(string message, CancellationToken ct)
    {
        if (_client?.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(message);
        await _client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        var attempts = 0;
        while (attempts < _settings.MaxReconnectAttempts && !ct.IsCancellationRequested)
        {
            attempts++;
            _logger?.LogInformation("Reconnection attempt {Attempt}/{Max}",
                attempts, _settings.MaxReconnectAttempts);

            await Task.Delay(_settings.ReconnectIntervalMs, ct);

            try
            {
                if (_client != null)
                {
                    _client.Dispose();
                }
                _client = new ClientWebSocket();

                var uri = new Uri(_settings.ServerUrl);
                await _client.ConnectAsync(uri, ct);
                _isConnected = true;

                if (_settings.EnableE2ee)
                {
                    _e2eeProvider.Reset();
                    await PerformKeyExchangeAsync(ct);
                }

                _ = ReceiveLoopAsync(ct);
                _logger?.LogInformation("Reconnected successfully");
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Reconnection attempt {Attempt} failed", attempts);
            }
        }

        _logger?.LogError("Max reconnection attempts reached");
    }

    public IStreamingMessageWriter StartStreamingMessage(CancellationToken cancellationToken = default)
    {
        return new WebSocketStreamingWriter(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync();
        _client?.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>
/// WebSocket用ストリーミングライター
/// </summary>
internal class WebSocketStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private readonly WebSocketChatInterface _interface;
    private bool _disposed;

    public string MessageId { get; } = Guid.NewGuid().ToString();

    public WebSocketStreamingWriter(WebSocketChatInterface iface)
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
