namespace Clawleash.Abstractions.Configuration;

/// <summary>
/// チャットインターフェース設定のベースクラス
/// </summary>
public abstract class ChatInterfaceSettingsBase
{
    /// <summary>
    /// インターフェースが有効かどうか
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// E2EE暗号化を有効にするか
    /// </summary>
    public bool EnableE2ee { get; set; } = false;
}

/// <summary>
/// Discord設定
/// </summary>
public class DiscordSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// Bot Token
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// コマンドプレフィックス
    /// </summary>
    public string CommandPrefix { get; set; } = "!";
}

/// <summary>
/// Slack設定
/// </summary>
public class SlackSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// Bot Token (xoxb-...)
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// App Token (xapp-...)
    /// </summary>
    public string AppToken { get; set; } = string.Empty;

    /// <summary>
    /// Signing Secret
    /// </summary>
    public string? SigningSecret { get; set; }
}

/// <summary>
/// WebSocket設定
/// </summary>
public class WebSocketSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// サーバーURL
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
}

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
        "stun:stun.l.google.com:19302"
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
}
