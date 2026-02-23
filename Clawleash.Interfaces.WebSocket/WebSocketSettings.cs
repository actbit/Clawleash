using Clawleash.Abstractions.Configuration;

namespace Clawleash.Interfaces.WebSocket;

/// <summary>
/// WebSocket設定
/// </summary>
public class WebSocketSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// サーバーURL (ws:// または wss://)
    /// </summary>
    public string ServerUrl { get; set; } = "ws://localhost:8080/chat";

    /// <summary>
    /// 再接続間隔（ミリ秒）
    /// </summary>
    public int ReconnectIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 最大再接続試行回数
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// ハートビート間隔（ミリ秒）
    /// </summary>
    public int HeartbeatIntervalMs { get; set; } = 30000;

    /// <summary>
    /// 接続タイムアウト（ミリ秒）
    /// </summary>
    public int ConnectionTimeoutMs { get; set; } = 10000;
}
