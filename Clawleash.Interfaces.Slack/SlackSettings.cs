using Clawleash.Abstractions.Configuration;

namespace Clawleash.Interfaces.Slack;

/// <summary>
/// Slack Bot設定
/// </summary>
public class SlackSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// Bot User OAuth Token (xoxb-...)
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// App-Level Token (xapp-...)
    /// Socket Mode用
    /// </summary>
    public string AppToken { get; set; } = string.Empty;

    /// <summary>
    /// Signing Secret
    /// HTTPリクエスト検証用（オプション）
    /// </summary>
    public string? SigningSecret { get; set; }

    /// <summary>
    /// スレッドで返信するかどうか
    /// </summary>
    public bool UseThreads { get; set; } = true;

    /// <summary>
    /// Block Kitを使用するかどうか
    /// </summary>
    public bool UseBlockKit { get; set; } = false;
}
