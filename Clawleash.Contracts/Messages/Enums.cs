namespace Clawleash.Contracts;

/// <summary>
/// 実行モード
/// </summary>
public enum ShellExecutionMode
{
    /// <summary>
    /// 制約付き実行（デフォルト）
    /// </summary>
    Constrained,

    /// <summary>
    /// 完全実行（信頼済みコマンドのみ）
    /// </summary>
    Full,

    /// <summary>
    /// 言語なし（コマンドのみ）
    /// </summary>
    NoLanguage
}

/// <summary>
/// シェル言語モード
/// </summary>
public enum ShellLanguageMode
{
    FullLanguage,
    ConstrainedLanguage,
    RestrictedLanguage,
    NoLanguage
}
