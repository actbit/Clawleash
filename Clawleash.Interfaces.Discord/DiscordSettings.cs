using Clawleash.Abstractions.Configuration;

namespace Clawleash.Interfaces.Discord;

/// <summary>
/// Discord Bot設定
/// </summary>
public class DiscordSettings : ChatInterfaceSettingsBase
{
    /// <summary>
    /// Bot Token
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// コマンドプレフィックス（例: "!"）
    /// DMでは無視されます
    /// </summary>
    public string CommandPrefix { get; set; } = "!";

    /// <summary>
    /// 返信をスレッドで行うかどうか
    /// </summary>
    public bool UseThreads { get; set; } = false;

    /// <summary>
    /// Embedメッセージを使用するかどうか
    /// </summary>
    public bool UseEmbeds { get; set; } = true;
}
