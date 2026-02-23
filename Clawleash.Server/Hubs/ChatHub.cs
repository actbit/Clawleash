using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawleash.Abstractions.Security;
using Clawleash.Server.Security;
using Microsoft.AspNetCore.SignalR;

namespace Clawleash.Server.Hubs;

/// <summary>
/// WebSocketチャット用SignalRハブ
/// E2EE対応
/// </summary>
public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly KeyManager _keyManager;
    private static readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private static readonly ConcurrentDictionary<string, string> _userChannels = new();

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
        }

        _userChannels.TryRemove(connectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// E2EE鍵交換開始
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

        if (_clients.TryGetValue(connectionId, out var client))
        {
            client.CurrentChannel = channelId;
        }

        _logger.LogDebug("Client {ConnectionId} joined channel {ChannelId}", connectionId, channelId);

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

        var message = new
        {
            messageId = Guid.NewGuid().ToString(),
            senderId = connectionId,
            senderName = request.SenderName ?? "Anonymous",
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
