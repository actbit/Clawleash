using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Clawleash.Server.Security;
using Microsoft.AspNetCore.SignalR;

namespace Clawleash.Server.Hubs;

/// <summary>
/// WebSocketチャット用SignalRハブ
/// E2EE対応（チャンネルごとの共通鍵方式）
/// </summary>
public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly KeyManager _keyManager;
    private static readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private static readonly ConcurrentDictionary<string, string> _userChannels = new();
    private static readonly ConcurrentDictionary<string, ChannelInfo> _channels = new();

    public ChatHub(ILogger<ChatHub> logger, KeyManager keyManager)
    {
        _logger = logger;
        _keyManager = keyManager;
    }

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        _clients[connectionId] = new ConnectedClient
        {
            ConnectionId = connectionId,
            ConnectedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Client connected: {ConnectionId}", connectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;

        if (_clients.TryRemove(connectionId, out var client))
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", connectionId);

            // Clean up session
            if (!string.IsNullOrEmpty(client.SessionId))
            {
                _keyManager.RemoveSession(client.SessionId);
            }

            // Leave all channels
            foreach (var channelId in client.JoinedChannels)
            {
                if (_channels.TryGetValue(channelId, out var channel))
                {
                    channel.MemberIds.Remove(connectionId);
                    if (channel.MemberIds.Count == 0)
                    {
                        _channels.TryRemove(channelId, out _);
                    }
                }
            }
        }

        _userChannels.TryRemove(connectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// E2EE鍵交換開始（クライアント-サーバー間）
    /// </summary>
    public async Task<KeyExchangeResponse> StartKeyExchange()
    {
        var connectionId = Context.ConnectionId;

        // Generate server key pair
        var serverPublicKey = _keyManager.GenerateServerPublicKey(out var sessionId);

        if (_clients.TryGetValue(connectionId, out var client))
        {
            client.SessionId = sessionId;
        }

        _logger.LogInformation("Key exchange started. SessionId: {SessionId}", sessionId);

        return new KeyExchangeResponse
        {
            SessionId = sessionId,
            ServerPublicKey = Convert.ToBase64String(serverPublicKey)
        };
    }

    /// <summary>
    /// E2EE鍵交換完了
    /// </summary>
    public async Task CompleteKeyExchange(string sessionId, string clientPublicKeyBase64)
    {
        var connectionId = Context.ConnectionId;

        if (_clients.TryGetValue(connectionId, out var client))
        {
            client.SessionId = sessionId;
            client.E2eeEnabled = true;
        }

        var clientPublicKey = Convert.FromBase64String(clientPublicKeyBase64);
        _keyManager.CompleteKeyExchange(sessionId, clientPublicKey);

        _logger.LogInformation("Key exchange completed. SessionId: {SessionId}", sessionId);

        await Clients.Caller.SendAsync("KeyExchangeCompleted", new { sessionId, success = true });
    }

    /// <summary>
    /// チャンネルに参加
    /// </summary>
    public async Task JoinChannel(string channelId)
    {
        var connectionId = Context.ConnectionId;

        await Groups.AddToGroupAsync(connectionId, channelId);
        _userChannels[connectionId] = channelId;

        ConnectedClient? client = null;
        if (_clients.TryGetValue(connectionId, out var existingClient))
        {
            client = existingClient;
            client.CurrentChannel = channelId;
            client.JoinedChannels.Add(channelId);
        }

        // チャンネル情報を取得または作成
        var channel = _channels.GetOrAdd(channelId, _ => new ChannelInfo
        {
            ChannelId = channelId,
            ChannelKey = RandomNumberGenerator.GetBytes(32) // AES-256鍵
        });

        channel.MemberIds.Add(connectionId);

        _logger.LogDebug("Client {ConnectionId} joined channel {ChannelId}", connectionId, channelId);

        // チャンネル鍵をクライアントに送信（クライアントのセッション鍵で暗号化）
        if (client != null && client.E2eeEnabled && !string.IsNullOrEmpty(client.SessionId))
        {
            var sessionKey = _keyManager.GetSharedSecret(client.SessionId);
            if (sessionKey != null)
            {
                // チャンネル鍵をセッション鍵で暗号化
                var encryptedChannelKey = EncryptChannelKey(channel.ChannelKey, sessionKey);

                await Clients.Caller.SendAsync("ChannelKey", new
                {
                    channelId,
                    encryptedKey = Convert.ToBase64String(encryptedChannelKey)
                });

                _logger.LogDebug("Sent encrypted channel key for {ChannelId} to {ConnectionId}", channelId, connectionId);
            }
        }
        else
        {
            // E2EE無効の場合は平文で送信（開発/テスト用）
            await Clients.Caller.SendAsync("ChannelKey", new
            {
                channelId,
                encryptedKey = (string?)null,
                plainKey = Convert.ToBase64String(channel.ChannelKey) // 警告: 本番環境では使用しない
            });

            _logger.LogWarning("Sent plain channel key for {ChannelId} (E2EE disabled)", channelId);
        }

        await Clients.Group(channelId).SendAsync("UserJoined", new
        {
            connectionId,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// チャンネルから離脱
    /// </summary>
    public async Task LeaveChannel(string channelId)
    {
        var connectionId = Context.ConnectionId;

        await Groups.RemoveFromGroupAsync(connectionId, channelId);
        _userChannels.TryRemove(connectionId, out _);

        if (_clients.TryGetValue(connectionId, out var client))
        {
            client.JoinedChannels.Remove(channelId);
        }

        if (_channels.TryGetValue(channelId, out var channel))
        {
            channel.MemberIds.Remove(connectionId);
        }

        _logger.LogDebug("Client {ConnectionId} left channel {ChannelId}", connectionId, channelId);

        await Clients.Group(channelId).SendAsync("UserLeft", new
        {
            connectionId,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// メッセージ送信
    /// </summary>
    public async Task SendMessage(ChatMessageRequest request)
    {
        var connectionId = Context.ConnectionId;
        var channelId = request.ChannelId ?? _userChannels.GetValueOrDefault(connectionId, "default");

        // 送信者名を取得
        var senderName = request.SenderName ?? "Anonymous";
        if (_clients.TryGetValue(connectionId, out var client))
        {
            client.DisplayName = senderName;
        }

        var message = new
        {
            messageId = Guid.NewGuid().ToString(),
            senderId = connectionId,
            senderName,
            content = request.Content,
            channelId,
            encrypted = request.Encrypted,
            ciphertext = request.Ciphertext,
            timestamp = DateTime.UtcNow
        };

        _logger.LogDebug("Message received on channel {ChannelId}. Encrypted: {Encrypted}",
            channelId, request.Encrypted);

        // Broadcast to channel (including sender for confirmation)
        await Clients.Group(channelId).SendAsync("MessageReceived", message);
    }

    /// <summary>
    /// Ping/Pong for heartbeat
    /// </summary>
    public async Task Ping()
    {
        await Clients.Caller.SendAsync("Pong", DateTime.UtcNow);
    }

    /// <summary>
    /// チャンネル鍵をセッション鍵で暗号化
    /// </summary>
    private static byte[] EncryptChannelKey(byte[] channelKey, byte[] sessionKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[channelKey.Length];
        var tag = new byte[16];

        using var aesGcm = new AesGcm(sessionKey, 16);
        aesGcm.Encrypt(nonce, channelKey, ciphertext, tag);

        // nonce + ciphertext + tag
        var result = new byte[12 + ciphertext.Length + 16];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(ciphertext, 0, result, 12, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, 12 + ciphertext.Length, 16);

        return result;
    }
}

/// <summary>
/// 接続クライアント情報
/// </summary>
internal class ConnectedClient
{
    public string ConnectionId { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public string? SessionId { get; set; }
    public bool E2eeEnabled { get; set; }
    public string? CurrentChannel { get; set; }
    public string DisplayName { get; set; } = "Anonymous";
    public HashSet<string> JoinedChannels { get; set; } = new();
}

/// <summary>
/// チャンネル情報
/// </summary>
internal class ChannelInfo
{
    public string ChannelId { get; set; } = string.Empty;
    public byte[] ChannelKey { get; set; } = Array.Empty<byte>();
    public HashSet<string> MemberIds { get; set; } = new();
}

/// <summary>
/// 鍵交換レスポンス
/// </summary>
public class KeyExchangeResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string ServerPublicKey { get; set; } = string.Empty;
}

/// <summary>
/// チャットメッセージリクエスト
/// </summary>
public class ChatMessageRequest
{
    public string? Content { get; set; }
    public string? ChannelId { get; set; }
    public string? SenderName { get; set; }
    public bool Encrypted { get; set; }
    public string? Ciphertext { get; set; }
}
