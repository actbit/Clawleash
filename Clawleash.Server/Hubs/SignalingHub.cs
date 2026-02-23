using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Clawleash.Server.Hubs;

/// <summary>
/// WebRTCシグナリング用SignalRハブ
/// </summary>
public class SignalingHub : Hub
{
    private readonly ILogger<SignalingHub> _logger;
    private static readonly ConcurrentDictionary<string, PeerInfo> _peers = new();
    private static readonly ConcurrentDictionary<string, string> _peerConnections = new();

    public SignalingHub(ILogger<SignalingHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogInformation("Signaling client connected: {ConnectionId}", connectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;

        if (_peers.TryRemove(connectionId, out var peer))
        {
            _logger.LogInformation("Peer disconnected: {PeerId}", peer.PeerId);

            // Notify other peers about disconnection
            await Clients.All.SendAsync("peer-disconnected", new
            {
                peerId = peer.PeerId
            });

            // Clean up connections
            foreach (var kvp in _peerConnections)
            {
                if (kvp.Value == connectionId || kvp.Key.StartsWith(connectionId + "-"))
                {
                    _peerConnections.TryRemove(kvp.Key, out _);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// ピアとして登録
    /// </summary>
    public async Task<RegisterResponse> Register(RegisterRequest request)
    {
        var connectionId = Context.ConnectionId;
        var peerId = Guid.NewGuid().ToString("N")[..8];

        var peer = new PeerInfo
        {
            PeerId = peerId,
            ConnectionId = connectionId,
            ClientType = request.ClientType ?? "unknown",
            Capabilities = request.Capabilities ?? new Dictionary<string, object>(),
            RegisteredAt = DateTime.UtcNow
        };

        _peers[connectionId] = peer;

        _logger.LogInformation("Peer registered: {PeerId} ({ClientType})", peerId, peer.ClientType);

        // Notify other peers
        await Clients.Others.SendAsync("peer-registered", new
        {
            peerId,
            clientType = peer.ClientType,
            capabilities = peer.Capabilities
        });

        return new RegisterResponse
        {
            PeerId = peerId,
            Success = true
        };
    }

    /// <summary>
    /// 接続可能なピア一覧を取得
    /// </summary>
    public async Task<IEnumerable<PeerInfoResponse>> GetPeers()
    {
        var currentConnectionId = Context.ConnectionId;

        return _peers.Values
            .Where(p => p.ConnectionId != currentConnectionId)
            .Select(p => new PeerInfoResponse
            {
                PeerId = p.PeerId,
                ClientType = p.ClientType,
                Capabilities = p.Capabilities
            });
    }

    /// <summary>
    /// WebRTC Offerを送信
    /// </summary>
    public async Task SendOffer(string toPeerId, string sdp)
    {
        var fromConnectionId = Context.ConnectionId;

        if (!_peers.TryGetValue(fromConnectionId, out var fromPeer))
            return;

        var toPeer = _peers.Values.FirstOrDefault(p => p.PeerId == toPeerId);
        if (toPeer == null)
        {
            _logger.LogWarning("Target peer not found: {ToPeerId}", toPeerId);
            return;
        }

        _logger.LogDebug("Offer from {FromPeerId} to {ToPeerId}", fromPeer.PeerId, toPeerId);

        await Clients.Client(toPeer.ConnectionId).SendAsync("offer", new
        {
            fromPeerId = fromPeer.PeerId,
            sdp
        });
    }

    /// <summary>
    /// WebRTC Answerを送信
    /// </summary>
    public async Task SendAnswer(string toPeerId, string sdp)
    {
        var fromConnectionId = Context.ConnectionId;

        if (!_peers.TryGetValue(fromConnectionId, out var fromPeer))
            return;

        var toPeer = _peers.Values.FirstOrDefault(p => p.PeerId == toPeerId);
        if (toPeer == null)
            return;

        _logger.LogDebug("Answer from {FromPeerId} to {ToPeerId}", fromPeer.PeerId, toPeerId);

        await Clients.Client(toPeer.ConnectionId).SendAsync("answer", new
        {
            fromPeerId = fromPeer.PeerId,
            sdp
        });

        // Record connection
        var connectionKey = $"{fromConnectionId}-{toPeer.ConnectionId}";
        _peerConnections[connectionKey] = "connected";

        // Notify both peers that connection is ready
        await Clients.Clients(fromConnectionId, toPeer.ConnectionId)
            .SendAsync("peer-connected", new
            {
                peerId = fromPeer.PeerId,
                remotePeerId = toPeerId
            });
    }

    /// <summary>
    /// ICE Candidateを送信
    /// </summary>
    public async Task SendIceCandidate(string toPeerId, string candidate, string sdpMid, int sdpMlineIndex)
    {
        var fromConnectionId = Context.ConnectionId;

        if (!_peers.TryGetValue(fromConnectionId, out var fromPeer))
            return;

        var toPeer = _peers.Values.FirstOrDefault(p => p.PeerId == toPeerId);
        if (toPeer == null)
            return;

        await Clients.Client(toPeer.ConnectionId).SendAsync("ice-candidate", new
        {
            fromPeerId = fromPeer.PeerId,
            candidate,
            sdpMid,
            sdpMlineIndex
        });
    }

    /// <summary>
    /// DataChannel経由でメッセージ送信（サーバー経由）
    /// </summary>
    public async Task SendDataChannelMessage(string toPeerId, string payloadBase64)
    {
        var fromConnectionId = Context.ConnectionId;

        if (!_peers.TryGetValue(fromConnectionId, out var fromPeer))
            return;

        var toPeer = _peers.Values.FirstOrDefault(p => p.PeerId == toPeerId);
        if (toPeer == null)
            return;

        await Clients.Client(toPeer.ConnectionId).SendAsync("datachannel-message", new
        {
            fromPeerId = fromPeer.PeerId,
            payload = payloadBase64
        });
    }

    /// <summary>
    /// E2EE鍵交換
    /// </summary>
    public async Task E2eeKeyExchange(string toPeerId, string sessionId, string publicKeyBase64)
    {
        var fromConnectionId = Context.ConnectionId;

        if (!_peers.TryGetValue(fromConnectionId, out var fromPeer))
            return;

        var toPeer = _peers.Values.FirstOrDefault(p => p.PeerId == toPeerId);
        if (toPeer == null)
            return;

        _logger.LogDebug("E2EE key exchange from {FromPeerId} to {ToPeerId}", fromPeer.PeerId, toPeerId);

        await Clients.Client(toPeer.ConnectionId).SendAsync("e2ee-key-exchange", new
        {
            fromPeerId = fromPeer.PeerId,
            sessionId,
            publicKey = publicKeyBase64
        });
    }
}

/// <summary>
/// ピア情報
/// </summary>
internal class PeerInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public Dictionary<string, object> Capabilities { get; set; } = new();
    public DateTime RegisteredAt { get; set; }
}

/// <summary>
/// 登録リクエスト
/// </summary>
public class RegisterRequest
{
    public string? ClientType { get; set; }
    public Dictionary<string, object>? Capabilities { get; set; }
}

/// <summary>
/// 登録レスポンス
/// </summary>
public class RegisterResponse
{
    public string PeerId { get; set; } = string.Empty;
    public bool Success { get; set; }
}

/// <summary>
/// ピア情報レスポンス
/// </summary>
public class PeerInfoResponse
{
    public string PeerId { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public Dictionary<string, object> Capabilities { get; set; } = new();
}
