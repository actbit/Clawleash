namespace Clawleash.Sandbox;

/// <summary>
/// フォルダーアクセスレベル
/// </summary>
public enum FolderAccessLevel
{
    /// <summary>
    /// アクセス禁止
    /// </summary>
    Deny,

    /// <summary>
    /// 読み取り専用
    /// </summary>
    ReadOnly,

    /// <summary>
    /// 読み書き可能
    /// </summary>
    ReadWrite,

    /// <summary>
    /// 完全制御（変更、削除含む）
    /// </summary>
    FullControl
}

/// <summary>
/// ネットワークアクセス設定
/// </summary>
public enum NetworkAccessLevel
{
    /// <summary>
    /// 親から継承
    /// </summary>
    Inherit,

    /// <summary>
    /// 許可
    /// </summary>
    Allow,

    /// <summary>
    /// 禁止
    /// </summary>
    Deny
}

/// <summary>
/// プロセス実行設定
/// </summary>
public enum ExecuteLevel
{
    /// <summary>
    /// 親から継承
    /// </summary>
    Inherit,

    /// <summary>
    /// 実行許可
    /// </summary>
    Allow,

    /// <summary>
    /// 実行禁止
    /// </summary>
    Deny
}

/// <summary>
/// フォルダーごとのセキュリティポリシー
/// </summary>
public class FolderPolicy
{
    /// <summary>
    /// フォルダーパス（絶対パス）
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// ファイルシステムアクセスレベル
    /// </summary>
    public FolderAccessLevel Access { get; set; } = FolderAccessLevel.ReadWrite;

    /// <summary>
    /// ネットワークアクセス
    /// </summary>
    public NetworkAccessLevel Network { get; set; } = NetworkAccessLevel.Inherit;

    /// <summary>
    /// 実行権限
    /// </summary>
    public ExecuteLevel Execute { get; set; } = ExecuteLevel.Inherit;

    /// <summary>
    /// 許可するファイル拡張子（空=すべて許可）
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = new();

    /// <summary>
    /// 禁止するファイル拡張子
    /// </summary>
    public List<string> DeniedExtensions { get; set; } = new();

    /// <summary>
    /// 最大ファイルサイズ（MB）, 0=無制限
    /// </summary>
    public int MaxFileSizeMB { get; set; } = 0;

    /// <summary>
    /// 監査ログを有効にする
    /// </summary>
    public bool EnableAudit { get; set; } = false;

    /// <summary>
    /// ポリシー名（識別用）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 説明
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// 解決済みの有効なポリシー
/// </summary>
public class EffectiveFolderPolicy
{
    public string Path { get; set; } = string.Empty;
    public FolderAccessLevel Access { get; set; }
    public bool NetworkAllowed { get; set; }
    public bool ExecuteAllowed { get; set; }
    public List<string> AllowedExtensions { get; set; } = new();
    public List<string> DeniedExtensions { get; set; } = new();
    public int MaxFileSizeMB { get; set; }
    public bool EnableAudit { get; set; }
    public FolderPolicy? SourcePolicy { get; set; }
}
