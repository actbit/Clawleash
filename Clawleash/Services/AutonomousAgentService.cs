using System.Text;
using System.Text.Json;
using Clawleash.Models;
using Microsoft.SemanticKernel;

namespace Clawleash.Services;

/// <summary>
/// 自律エージェントサービス
/// 目標の計画・実行・評価・修正を自律的に行う
/// </summary>
public class AutonomousAgentService : IDisposable
{
    private readonly Kernel _kernel;
    private readonly MemoryManager _memoryManager;
    private readonly AutonomousSettings _settings;
    private AgentGoal? _currentGoal;
    private bool _isRunning;
    private bool _isPaused;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly SemaphoreSlim _approvalLock = new(1, 1);
    private TaskCompletionSource<bool>? _approvalTcs;
    private bool _disposed;

    public event EventHandler<ProgressEventArgs>? ProgressUpdated;
    public event EventHandler<ApprovalRequestEventArgs>? ApprovalRequired;
    public event EventHandler<GoalCompletedEventArgs>? GoalCompleted;

    public bool IsRunning => _isRunning;
    public AgentGoal? CurrentGoal => _currentGoal;
    public AutonomousSettings Settings => _settings;

    public AutonomousAgentService(Kernel kernel, AutonomousSettings? settings = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _memoryManager = new MemoryManager();
        _settings = settings ?? new AutonomousSettings();
    }

    #region 目標の計画

    /// <summary>
    /// 目標からタスクを計画する
    /// </summary>
    public async Task<AgentGoal> PlanGoalAsync(string goalDescription, CancellationToken cancellationToken = default)
    {
        var goal = new AgentGoal
        {
            Description = goalDescription,
            MaxSteps = _settings.MaxSteps,
            Status = GoalStatus.Planning
        };

        _memoryManager.RecordConversation("user", goalDescription);

        var context = _memoryManager.BuildContext(goalDescription);
        var jsonTemplate = """
            {
              "tasks": [
                {
                  "description": "タスクの説明",
                  "priority": 1-10,
                  "requiresApproval": true/false
                }
              ]
            }
            """;

        var prompt = $"""
            あなたは自律的なAIエージェントのタスクプランナーです。
            以下の目標を達成するための詳細なタスクリストを作成してください。

            ## 目標
            {goalDescription}

            ## 過去のコンテキスト
            {context}

            ## 利用可能なツール
            - ファイル操作: 読み込み、書き込み、一覧
            - ブラウザ操作: ナビゲート、クリック、入力、スクロール
            - Webクロール: スクレイプ、クロール、検索
            - データ抽出: 構造化データ抽出、要約

            ## 出力形式
            以下のJSON形式でタスクリストを出力してください:
            ```json
            {jsonTemplate}
            ```

            ## 重要なルール
            - 各タスクは具体的で実行可能なものにする
            - 優先順位は1（低）から10（高）で設定
            - 危険な操作（ファイル削除、フォーム送信）は requiresApproval: true にする
            - タスクは最大{_settings.MaxSteps}個まで
            """;

        try
        {
            var result = await _kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var response = result.GetValue<string>() ?? "";

            // JSONを抽出
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(response, @"```json\s*([\s\S]*?)\s*```");
            if (jsonMatch.Success)
            {
                var json = jsonMatch.Groups[1].Value;
                var plan = JsonSerializer.Deserialize<TaskPlan>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (plan?.Tasks != null)
                {
                    foreach (var task in plan.Tasks)
                    {
                        goal.Tasks.Add(new AgentTask
                        {
                            Description = task.Description,
                            Priority = task.Priority,
                            Status = task.RequiresApproval ? AgentTaskStatus.RequiresApproval : AgentTaskStatus.Pending
                        });
                    }
                }
            }

            // タスクを優先順位でソート
            goal.Tasks = [.. goal.Tasks.OrderByDescending(t => t.Priority)];

            _memoryManager.RecordAction("plan_goal", $"Created {goal.Tasks.Count} tasks", true);
            goal.Status = GoalStatus.NotStarted;
        }
        catch (Exception ex)
        {
            _memoryManager.RecordAction("plan_goal", $"Error: {ex.Message}", false);
            goal.Status = GoalStatus.Failed;
        }

        return goal;
    }

    #endregion

    #region 目標の実行

    /// <summary>
    /// 目標の実行を開始
    /// </summary>
    public async Task<AgentGoal> ExecuteGoalAsync(string goalDescription, CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("既に別の目標を実行中です");
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentGoal = await PlanGoalAsync(goalDescription, _cancellationTokenSource.Token);

        if (_currentGoal.Status == GoalStatus.Failed)
        {
            return _currentGoal;
        }

        _isRunning = true;
        _isPaused = false;
        _currentGoal.Status = GoalStatus.InProgress;

        try
        {
            await ExecuteTasksAsync(_currentGoal, _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            _currentGoal.Status = GoalStatus.Cancelled;
        }
        finally
        {
            _isRunning = false;
        }

        return _currentGoal;
    }

    private async Task ExecuteTasksAsync(AgentGoal goal, CancellationToken cancellationToken)
    {
        foreach (var task in goal.Tasks.ToList())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            while (_isPaused)
            {
                await Task.Delay(100, cancellationToken);
            }

            goal.CurrentStep++;
            task.Status = AgentTaskStatus.InProgress;

            OnProgressUpdated(new ProgressEventArgs
            {
                GoalId = goal.Id,
                TaskId = task.Id,
                Step = goal.CurrentStep,
                TotalSteps = goal.Tasks.Count,
                Status = $"実行中: {task.Description}"
            });

            // 承認が必要なタスクの場合
            if (task.Status == AgentTaskStatus.RequiresApproval || RequiresApproval(task))
            {
                var approved = await RequestApprovalAsync(task);
                if (!approved)
                {
                    task.Status = AgentTaskStatus.Skipped;
                    task.Result = "ユーザーによってスキップされました";
                    continue;
                }
            }

            // タスクを実行
            var evaluation = await ExecuteTaskAsync(task, cancellationToken);

            // 評価に基づいて処理
            if (evaluation.Success)
            {
                task.Status = AgentTaskStatus.Completed;
                task.Result = evaluation.Reason;
                task.CompletedAt = DateTime.UtcNow;

                _memoryManager.RecordAction("task_complete", task.Description, true);
            }
            else if (evaluation.RequiresRetry && task.RetryCount < task.MaxRetries)
            {
                task.RetryCount++;
                task.Error = evaluation.Reason;

                // 代替アプローチがある場合は試行
                if (!string.IsNullOrEmpty(evaluation.AlternativeApproach))
                {
                    task.Description = evaluation.AlternativeApproach;
                }

                _memoryManager.RecordAction("task_retry", $"{task.Description} (Retry {task.RetryCount})", false);

                // 再実行
                var retryEvaluation = await ExecuteTaskAsync(task, cancellationToken);
                if (retryEvaluation.Success)
                {
                    task.Status = AgentTaskStatus.Completed;
                    task.Result = retryEvaluation.Reason;
                }
                else
                {
                    task.Status = AgentTaskStatus.Failed;
                    task.Error = retryEvaluation.Reason;
                }
            }
            else
            {
                task.Status = AgentTaskStatus.Failed;
                task.Error = evaluation.Reason;
                _memoryManager.RecordAction("task_failed", $"{task.Description}: {evaluation.Reason}", false);
            }

            // 進捗を報告
            if (goal.CurrentStep % _settings.ProgressReportInterval == 0)
            {
                OnProgressUpdated(new ProgressEventArgs
                {
                    GoalId = goal.Id,
                    TaskId = task.Id,
                    Step = goal.CurrentStep,
                    TotalSteps = goal.Tasks.Count,
                    Status = $"完了: {task.Status}"
                });
            }

            // ステップ間の待機
            if (_settings.StepDelayMs > 0)
            {
                await Task.Delay(_settings.StepDelayMs, cancellationToken);
            }
        }

        // 目標完了
        goal.CompletedAt = DateTime.UtcNow;
        goal.Status = goal.Tasks.All(t => t.Status == AgentTaskStatus.Completed || t.Status == AgentTaskStatus.Skipped)
            ? GoalStatus.Completed
            : GoalStatus.Failed;

        OnGoalCompleted(new GoalCompletedEventArgs
        {
            Goal = goal,
            Success = goal.Status == GoalStatus.Completed
        });
    }

    private async Task<ExecutionEvaluation> ExecuteTaskAsync(AgentTask task, CancellationToken cancellationToken)
    {
        var context = _memoryManager.BuildContext(task.Description);
        var jsonTemplate = """
            {
              "success": true/false,
              "reason": "成功/失敗の理由",
              "issues": ["問題点があれば"],
              "requiresRetry": true/false,
              "alternativeApproach": "別のアプローチがあれば"
            }
            """;

        var prompt = $"""
            あなたは自律的なAIエージェントです。
            以下のタスクを実行し、結果を報告してください。

            ## 現在のタスク
            {task.Description}

            ## コンテキスト
            {context}

            ## 出力形式
            タスクを実行した後、以下のJSON形式で結果を報告してください:
            ```json
            {jsonTemplate}
            ```

            ## 重要なルール
            - 安全に実行する
            - ユーザーの意図しない操作は避ける
            - エラーが発生した場合は報告する
            """;

        try
        {
            var result = await _kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var response = result.GetValue<string>() ?? "";

            // JSONを抽出
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(response, @"```json\s*([\s\S]*?)\s*```");
            if (jsonMatch.Success)
            {
                var json = jsonMatch.Groups[1].Value;
                return JsonSerializer.Deserialize<ExecutionEvaluation>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new ExecutionEvaluation { Success = false, Reason = "評価の解析に失敗" };
            }

            // JSONがない場合は成功とみなす
            return new ExecutionEvaluation
            {
                Success = true,
                Reason = response
            };
        }
        catch (Exception ex)
        {
            return new ExecutionEvaluation
            {
                Success = false,
                Reason = ex.Message,
                RequiresRetry = true
            };
        }
    }

    #endregion

    #region 承認制御

    private bool RequiresApproval(AgentTask task)
    {
        var desc = task.Description.ToLowerInvariant();

        // 設定に基づいて判断
        if (_settings.RequireApprovalForFileDeletion && desc.Contains("削除"))
        {
            return true;
        }

        if (_settings.RequireApprovalForFormSubmission && (desc.Contains("送信") || desc.Contains("submit")))
        {
            return true;
        }

        // 危険な操作のパターン
        var dangerousPatterns = new[]
        {
            "削除", "delete", "remove",
            "送信", "submit", "form",
            "決済", "payment", "purchase",
            "パスワード", "password",
            "認証", "auth", "login"
        };

        return dangerousPatterns.Any(p => desc.Contains(p));
    }

    private async Task<bool> RequestApprovalAsync(AgentTask task)
    {
        await _approvalLock.WaitAsync();
        try
        {
            _approvalTcs = new TaskCompletionSource<bool>();

            OnApprovalRequired(new ApprovalRequestEventArgs
            {
                TaskId = task.Id,
                TaskDescription = task.Description,
                ResponseTask = _approvalTcs
            });

            return await _approvalTcs.Task;
        }
        finally
        {
            _approvalLock.Release();
        }
    }

    /// <summary>
    /// 承認を与える
    /// </summary>
    public void ApproveTask(string taskId)
    {
        _approvalTcs?.TrySetResult(true);
    }

    /// <summary>
    /// タスクを拒否する
    /// </summary>
    public void RejectTask(string taskId)
    {
        _approvalTcs?.TrySetResult(false);
    }

    #endregion

    #region 制御メソッド

    /// <summary>
    /// 実行を一時停止
    /// </summary>
    public void Pause()
    {
        _isPaused = true;
        _currentGoal!.Status = GoalStatus.Paused;
    }

    /// <summary>
    /// 実行を再開
    /// </summary>
    public void Resume()
    {
        _isPaused = false;
        if (_currentGoal != null)
        {
            _currentGoal.Status = GoalStatus.InProgress;
        }
    }

    /// <summary>
    /// 実行をキャンセル
    /// </summary>
    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        if (_currentGoal != null)
        {
            _currentGoal.Status = GoalStatus.Cancelled;
        }
    }

    #endregion

    #region イベント発火

    protected virtual void OnProgressUpdated(ProgressEventArgs e)
    {
        ProgressUpdated?.Invoke(this, e);
    }

    protected virtual void OnApprovalRequired(ApprovalRequestEventArgs e)
    {
        ApprovalRequired?.Invoke(this, e);
    }

    protected virtual void OnGoalCompleted(GoalCompletedEventArgs e)
    {
        GoalCompleted?.Invoke(this, e);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cancellationTokenSource?.Dispose();
        _approvalLock.Dispose();
        _memoryManager.Dispose();
        _disposed = true;
    }
}

#region イベント引数

public class ProgressEventArgs : EventArgs
{
    public string GoalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public int Step { get; set; }
    public int TotalSteps { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ApprovalRequestEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskDescription { get; set; } = string.Empty;
    public TaskCompletionSource<bool> ResponseTask { get; set; } = null!;
}

public class GoalCompletedEventArgs : EventArgs
{
    public AgentGoal Goal { get; set; } = null!;
    public bool Success { get; set; }
}

#endregion

#region 内部クラス

internal class TaskPlan
{
    public List<TaskPlanItem> Tasks { get; set; } = [];
}

internal class TaskPlanItem
{
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool RequiresApproval { get; set; }
}

#endregion
