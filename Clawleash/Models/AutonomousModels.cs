namespace Clawleash.Models;

/// <summary>
/// 自律エージェントのタスク
/// </summary>
public class AgentTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Description { get; set; } = string.Empty;
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;
    public int Priority { get; set; } = 0;
    public List<string> Dependencies { get; set; } = [];
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// タスクステータス
/// </summary>
public enum AgentTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped,
    RequiresApproval
}

/// <summary>
/// 自律実行の目標
/// </summary>
public class AgentGoal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Description { get; set; } = string.Empty;
    public List<AgentTask> Tasks { get; set; } = [];
    public GoalStatus Status { get; set; } = GoalStatus.NotStarted;
    public int CurrentStep { get; set; } = 0;
    public int MaxSteps { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// 目標ステータス
/// </summary>
public enum GoalStatus
{
    NotStarted,
    Planning,
    InProgress,
    Paused,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 実行結果の評価
/// </summary>
public class ExecutionEvaluation
{
    public bool Success { get; set; }
    public string? Reason { get; set; }
    public List<string> Issues { get; set; } = [];
    public List<string> Suggestions { get; set; } = [];
    public bool RequiresRetry { get; set; }
    public bool RequiresHumanApproval { get; set; }
    public string? AlternativeApproach { get; set; }
}

/// <summary>
/// 自律実行の設定
/// </summary>
public class AutonomousSettings
{
    /// <summary>
    /// 最大実行ステップ数
    /// </summary>
    public int MaxSteps { get; set; } = 10;

    /// <summary>
    /// 自動リトライの最大回数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 危険な操作に人間の承認を必要とするか
    /// </summary>
    public bool RequireApprovalForDangerousOperations { get; set; } = true;

    /// <summary>
    /// ファイル削除に承認を必要とするか
    /// </summary>
    public bool RequireApprovalForFileDeletion { get; set; } = true;

    /// <summary>
    /// フォーム送信に承認を必要とするか
    /// </summary>
    public bool RequireApprovalForFormSubmission { get; set; } = true;

    /// <summary>
    /// 自動スクロールの最大回数
    /// </summary>
    public int MaxAutoScrolls { get; set; } = 5;

    /// <summary>
    /// 自動クロールの最大ページ数
    /// </summary>
    public int MaxAutoCrawlPages { get; set; } = 10;

    /// <summary>
    /// 各ステップ間の待機時間（ミリ秒）
    /// </summary>
    public int StepDelayMs { get; set; } = 500;

    /// <summary>
    /// 進捗報告の頻度（ステップ数）
    /// </summary>
    public int ProgressReportInterval { get; set; } = 1;
}
