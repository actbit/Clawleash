namespace Clawleash.Services;

/// <summary>
/// 承認リクエストハンドラーのインターフェース
/// CLI、GUI、Webなど異なるインターフェースに対応可能
/// </summary>
public interface IApprovalHandler
{
    /// <summary>
    /// 承認リクエストを処理する
    /// </summary>
    /// <param name="request">承認リクエスト情報</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>承認結果</returns>
    Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// このハンドラーが利用可能かどうか
    /// </summary>
    bool IsAvailable { get; }
}

/// <summary>
/// 承認リクエスト情報
/// </summary>
public class ApprovalRequest
{
    /// <summary>
    /// リクエストID
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// タスクID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// タスクの説明
    /// </summary>
    public string TaskDescription { get; set; } = string.Empty;

    /// <summary>
    /// 危険度レベル
    /// </summary>
    public DangerLevel DangerLevel { get; set; } = DangerLevel.Medium;

    /// <summary>
    /// 操作の種類
    /// </summary>
    public OperationType OperationType { get; set; } = OperationType.Unknown;

    /// <summary>
    /// 追加のコンテキスト情報
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();

    /// <summary>
    /// タイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 承認結果
/// </summary>
public class ApprovalResult
{
    /// <summary>
    /// 承認されたかどうか
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// ユーザーのコメント（オプション）
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// 選択されたアクション
    /// </summary>
    public ApprovalAction Action { get; set; } = ApprovalAction.Deny;

    /// <summary>
    /// 処理時刻
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 危険度レベル
/// </summary>
public enum DangerLevel
{
    /// <summary>
    /// 低リスク（情報表示など）
    /// </summary>
    Low,

    /// <summary>
    /// 中リスク（ファイル編集など）
    /// </summary>
    Medium,

    /// <summary>
    /// 高リスク（ファイル削除、フォーム送信など）
    /// </summary>
    High,

    /// <summary>
    /// 重要（決済、認証など）
    /// </summary>
    Critical
}

/// <summary>
/// 操作の種類
/// </summary>
public enum OperationType
{
    Unknown,
    FileRead,
    FileWrite,
    FileDelete,
    FileMove,
    FolderCreate,
    FolderDelete,
    FolderMove,
    WebNavigate,
    FormSubmit,
    WebSearch,
    CommandExecute,
    DataExtraction,
    Authentication,
    Payment
}

/// <summary>
/// 承認アクション
/// </summary>
public enum ApprovalAction
{
    /// <summary>
    /// 許可
    /// </summary>
    Approve,

    /// <summary>
    /// 拒否
    /// </summary>
    Deny,

    /// <summary>
    /// スキップ
    /// </summary>
    Skip,

    /// <summary>
    /// キャンセル（全体を中止）
    /// </summary>
    Cancel
}
