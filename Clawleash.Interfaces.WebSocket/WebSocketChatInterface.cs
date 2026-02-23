using System.Text;
using Clawleash.Abstractions.Security;
using Clawleash.Abstractions.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Clawleash.Interfaces.WebSocket;

/// <summary>
/// WebSocketチャットインターフェース（E2EE対応）
/// SignalRクライアントを使用してサーバーと通信
/// </summary>
public class WebSocketChatInterface : IChatInterface
{
    private readonly WebSocketSettings _settings;
    private readonly ILogger<WebSocketChatInterface>? _logger;
    private readonly AesGcmE2eeProvider _e2eeProvider;
    private readonly Dictionary<string, string> _channelTracking = new();
    private HubConnection? _hubConnection;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isConnected;

    public string Name => "WebSocket";
    public bool IsConnected => _isConnected && _hubConnection?.State == HubConnectionState.Connected;

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

        try
        {
            // SignalRハブ接続を構築
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_settings.ServerUrl)
                .WithAutomaticReconnect(new RetryPolicy(_settings.ReconnectIntervalMs, _settings.MaxReconnectAttempts))
                .Build();

            // メッセージ受信ハンドラー
            _hubConnection.On<object>("MessageReceived", async (message) =>
            {
                await HandleMessageReceivedAsync(message);
            });

            // キー交換完了ハンドラー
            _hubConnection.On<object>("KeyExchangeCompleted", (result) =>
            {
                _logger?.LogInformation("Key exchange confirmed by server");
            });

            // チャンネル鍵受信ハンドラー
            _hubConnection.On<object>("ChannelKey", async (data) =>
            {
                await HandleChannelKeyAsync(data);
            });

            // 接続状態変更ハンドラー
            _hubConnection.Reconnecting += (exception) =>
            {
                _isConnected = false;
                _logger?.LogWarning(exception, "Reconnecting to SignalR hub...");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async (connectionId) =>
            {
                _logger?.LogInformation("Reconnected to SignalR hub. ConnectionId: {ConnectionId}", connectionId);
                _isConnected = true;

                // E2EE有効時は鍵交換を再実行
                if (_settings.EnableE2ee)
                {
                    await PerformKeyExchangeAsync(_cts.Token);
                }
            };

            _hubConnection.Closed += (exception) =>
            {
                _isConnected = false;
                _logger?.LogWarning(exception, "Connection to SignalR hub closed");
                return Task.CompletedTask;
            };

            _logger?.LogInformation("Connecting to SignalR hub: {Url}", _settings.ServerUrl);

            await _hubConnection.StartAsync(_cts.Token);
            _isConnected = true;

            // E2EE有効時は鍵交換を実行
            if (_settings.EnableE2ee)
            {
                await PerformKeyExchangeAsync(_cts.Token);
            }

            _logger?.LogInformation("WebSocket connected. E2EE: {E2ee}", _settings.EnableE2ee ? "Enabled" : "Disabled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to SignalR hub");
            _isConnected = false;
            throw;
        }
    }

    private async Task PerformKeyExchangeAsync(CancellationToken ct)
    {
        if (_hubConnection == null) return;

        try
        {
            // サーバーのStartKeyExchangeメソッドを呼び出し
            var response = await _hubConnection.InvokeAsync<KeyExchangeResponse>(
                "StartKeyExchange", ct);

            // サーバーの公開鍵で鍵交換を完了
            var serverPublicKey = Convert.FromBase64String(response.ServerPublicKey);
            await _e2eeProvider.CompleteKeyExchangeAsync(new KeyExchangeResult
            {
                PublicKey = serverPublicKey,
                SessionId = response.SessionId
            }, ct);

            // クライアントの公開鍵をサーバーに送信
            var clientPublicKey = _e2eeProvider.GetPublicKey();
            await _hubConnection.InvokeAsync("CompleteKeyExchange",
                response.SessionId,
                Convert.ToBase64String(clientPublicKey),
                ct);

            _logger?.LogInformation("Key exchange completed. SessionId: {SessionId}", response.SessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Key exchange failed");
        }
    }

    private Task HandleChannelKeyAsync(object data)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var channelId = root.TryGetProperty("channelId", out var chEl)
                ? chEl.GetString() ?? ""
                : "";

            if (string.IsNullOrEmpty(channelId)) return Task.CompletedTask;

            if (root.TryGetProperty("encryptedKey", out var encKeyEl) &&
                !string.IsNullOrEmpty(encKeyEl.GetString()))
            {
                // E2EE有効: 暗号化されたチャンネル鍵を復号化
                var encryptedKey = Convert.FromBase64String(encKeyEl.GetString()!);
                _e2eeProvider.SetChannelKey(channelId, encryptedKey);
                _logger?.LogInformation("Channel key set for {ChannelId}", channelId);
            }
            else if (root.TryGetProperty("plainKey", out var plainKeyEl) &&
                     !string.IsNullOrEmpty(plainKeyEl.GetString()))
            {
                // E2EE無効: 平文のチャンネル鍵を設定
                var plainKey = Convert.FromBase64String(plainKeyEl.GetString()!);
                _e2eeProvider.SetPlainChannelKey(channelId, plainKey);
                _logger?.LogWarning("Plain channel key set for {ChannelId} (E2EE disabled)", channelId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to set channel key");
        }

        return Task.CompletedTask;
    }

    private async Task HandleMessageReceivedAsync(object messageObj)
    {
        try
        {
            // 動的にメッセージを解析
            var json = System.Text.Json.JsonSerializer.Serialize(messageObj);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var content = root.TryGetProperty("content", out var contentEl)
                ? contentEl.GetString() ?? ""
                : "";

            var messageId = root.TryGetProperty("messageId", out var msgIdEl)
                ? msgIdEl.GetString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();

            var senderId = root.TryGetProperty("senderId", out var senderEl)
                ? senderEl.GetString() ?? "unknown"
                : "unknown";

            var senderName = root.TryGetProperty("senderName", out var nameEl)
                ? nameEl.GetString() ?? senderId
                : senderId;

            var channelId = root.TryGetProperty("channelId", out var chEl)
                ? chEl.GetString() ?? "default"
                : "default";

            var encrypted = root.TryGetProperty("encrypted", out var encEl)
                && encEl.GetBoolean();

            var ciphertextBase64 = root.TryGetProperty("ciphertext", out var cipherEl)
                ? cipherEl.GetString()
                : null;

            // E2EE有効時は復号化
            if (encrypted && _settings.EnableE2ee && _e2eeProvider.IsEncrypted && !string.IsNullOrEmpty(ciphertextBase64))
            {
                try
                {
                    var ciphertext = Convert.FromBase64String(ciphertextBase64);
                    // チャンネル鍵があれば使用、なければセッション鍵を使用
                    content = await _e2eeProvider.DecryptAsync(ciphertext, channelId);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to decrypt message");
                    content = "[Encrypted - Decryption Failed]";
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
                    ["encrypted"] = encrypted && _e2eeProvider.IsEncrypted
                }
            };

            _channelTracking[messageId] = channelId;
            MessageReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to process received message");
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
        if (!IsConnected || _hubConnection == null)
        {
            _logger?.LogWarning("WebSocket is not connected");
            return;
        }

        string? channelId = null;
        if (!string.IsNullOrEmpty(replyToMessageId) && _channelTracking.TryGetValue(replyToMessageId, out var trackedChannel))
        {
            channelId = trackedChannel;
        }

        var effectiveChannelId = channelId ?? "default";

        try
        {
            // チャンネル鍵がある場合はチャンネル鍵で暗号化
            var canEncrypt = _settings.EnableE2ee && _e2eeProvider.IsEncrypted &&
                              _e2eeProvider.HasChannelKey(effectiveChannelId);

            if (canEncrypt)
            {
                // チャンネル鍵で暗号化
                var ciphertext = await _e2eeProvider.EncryptAsync(message, effectiveChannelId, cancellationToken);
                await _hubConnection.InvokeAsync("SendMessage", new
                {
                    content = "",
                    channelId = effectiveChannelId,
                    senderName = "Clawleash",
                    encrypted = true,
                    ciphertext = Convert.ToBase64String(ciphertext)
                }, cancellationToken);
            }
            else
            {
                // 平文で送信
                await _hubConnection.InvokeAsync("SendMessage", new
                {
                    content = message,
                    channelId = effectiveChannelId,
                    senderName = "Clawleash",
                    encrypted = false,
                    ciphertext = (string?)null
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message");
        }
    }

    /// <summary>
    /// チャンネルに参加
    /// </summary>
    public async Task JoinChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        if (_hubConnection == null || !IsConnected)
        {
            _logger?.LogWarning("WebSocket is not connected");
            return;
        }

        await _hubConnection.InvokeAsync("JoinChannel", channelId, cancellationToken);
        _logger?.LogDebug("Joined channel: {ChannelId}", channelId);
    }

    /// <summary>
    /// チャンネルから離脱
    /// </summary>
    public async Task LeaveChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        if (_hubConnection == null || !IsConnected)
        {
            return;
        }

        await _hubConnection.InvokeAsync("LeaveChannel", channelId, cancellationToken);
        _logger?.LogDebug("Left channel: {ChannelId}", channelId);
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
        _hubConnection?.DisposeAsync();
        _cts?.Dispose();
    }

    /// <summary>
    /// SignalR再接続ポリシー
    /// </summary>
    private class RetryPolicy : IRetryPolicy
    {
        private readonly int _reconnectIntervalMs;
        private readonly int _maxReconnectAttempts;

        public RetryPolicy(int reconnectIntervalMs, int maxReconnectAttempts)
        {
            _reconnectIntervalMs = reconnectIntervalMs;
            _maxReconnectAttempts = maxReconnectAttempts;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount >= _maxReconnectAttempts)
            {
                return null; // 再接続試行回数上限に達した
            }

            // 指数バックオフ
            var delay = Math.Min(_reconnectIntervalMs * Math.Pow(2, retryContext.PreviousRetryCount), 60000);
            return TimeSpan.FromMilliseconds(delay);
        }
    }
}

/// <summary>
/// キー交換レスポンス
/// </summary>
internal class KeyExchangeResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string ServerPublicKey { get; set; } = string.Empty;
}

/// <summary>
/// WebSocket用ストリーミングライター
/// </summary>
internal class WebSocketStreamingWriter : IStreamingMessageWriter
{
    private readonly StringBuilder _content = new();
    private readonly WebSocketChatInterface _interface;

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
        return ValueTask.CompletedTask;
    }
}
