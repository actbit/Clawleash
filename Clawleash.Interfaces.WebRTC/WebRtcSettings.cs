using Clawleash.Abstractions.Configuration;

namespace Clawleash.Interfaces.WebRTC;

/// <summary>
/// WebRTC設定
/// </summary>
public class WebRtcSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// シグナリングサーバーURL
    /// </summary>
    public string SignalingServerUrl { get; set; } = "ws://localhost:8080/signaling";

    /// <summary>
    /// STUNサーバーリスト
    /// </summary>
    public List<string> StunServers { get; set; } = new()
    {
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302"
    };

    /// <summary>
    /// TURNサーバーURL（オプション）
    /// </summary>
    public string? TurnServerUrl { get; set; }

    /// <summary>
    /// TURNサーバーユーザー名
    /// </summary>
    public string? TurnUsername { get; set; }

    /// <summary>
    /// TURNサーバーパスワード
    /// </summary>
    public string? TurnPassword { get; set; }

    /// <summary>
    /// 再接続間隔（ミリ秒）
    /// </summary>
    public int ReconnectIntervalMs { get; set; } = 5000;

    /// <summary>
    /// ICE接続タイムアウト（ミリ秒）
    /// </summary>
    public int IceConnectionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// DataChannel名
    /// </summary>
    public string DataChannelName { get; set; } = "clawleash-chat";

    /// <summary>
    /// 最大再接続試行回数
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// ネイティブWebRTCライブラリの使用を試みる
    /// 無効な場合はシミュレーションモードで動作
    /// </summary>
    public bool TryUseNativeClient { get; set; } = true;

    /// <summary>
    /// ICE候補の収集タイムアウト（ミリ秒）
    /// </summary>
    public int IceGatheringTimeoutMs { get; set; } = 10000;

    /// <summary>
    /// DataChannelの信頼性モード
    /// </summary>
    public bool DataChannelReliable { get; set; } = true;

    /// <summary>
    /// ハートビート間隔（ミリ秒）- 0で無効
    /// </summary>
    public int HeartbeatIntervalMs { get; set; } = 30000;

    /// <summary>
    /// ピア接続アイドルタイムアウト（ミリ秒）
    /// </summary>
    public int PeerIdleTimeoutMs { get; set; } = 60000;
}
