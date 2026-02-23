using System.Text;
using System.Text.Json;
using Clawleash.Models;
using Microsoft.SemanticKernel;

namespace Clawleash.Services;

/// <summary>
/// 自律エージェントサービス
/// 目標の計画・実行・評価・修正を自律的に行う
/// IApprovalHandlerとIInputHandlerを通じて複数のインターフェースをサポート
/// </summary>
public class AutonomousAgentService : IDisposable
{
    private readonly Kernel _kernel;
    private readonly MemoryManager _memoryManager;
    private readonly AutonomousSettings _settings;
    private IApprovalHandler? _approvalHandler;
    private IInputHandler? _inputHandler;
    private AgentGoal? _currentGoal;
    private bool _isRunning;
    private bool _isPaused;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    // イベントベースの承認（レガシーサポート）
    private readonly SemaphoreSlim _approvalLock = new(1, 1);
    private TaskCompletionSource<bool>? _approvalTcs;

    public event EventHandler<ProgressEventArgs>? ProgressUpdated;
    public event EventHandler<ApprovalRequestEventArgs>? ApprovalRequired;
    public event EventHandler<GoalCompletedEventArgs>? GoalCompleted;
    public event EventHandler<UserInputEventArgs>? InputRequested;

    public bool IsRunning => _isRunning;
    public AgentGoal? CurrentGoal => _currentGoal;
    public AutonomousSettings Settings => _settings;

    /// <summary>
    /// 承認・入力ハンドラーありで初期化
    /// </summary>
    public AutonomousAgentService(
        Kernel kernel,
        IApprovalHandler approvalHandler,
        IInputHandler inputHandler,
        AutonomousSettings? settings = null)
        : this(kernel, settings)
    {
        _approvalHandler = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
        _inputHandler = inputHandler ?? throw new ArgumentNullException(nameof(inputHandler));
    }

    /// <summary>
    /// 承認ハンドラーのみで初期化
    /// </summary>
    public AutonomousAgentService(
        Kernel kernel,
        IApprovalHandler approvalHandler,
        AutonomousSettings? settings = null)
        : this(kernel, settings)
    {
        _approvalHandler = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
    }

    /// <summary>
    /// ハンドラーなしで初期化（イベントベースを使用）
    /// </summary>
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

    /// <summary>
    /// タスクから危険度レベルを判定
    /// </summary>
    private DangerLevel DetermineDangerLevel(AgentTask task)
    {
        var desc = task.Description.ToLowerInvariant();

        // 重要操作
        if (desc.Contains("決済") || desc.Contains("payment") || desc.Contains("purchase") ||
            desc.Contains("パスワード") || desc.Contains("password") || desc.Contains("認証"))
        {
            return DangerLevel.Critical;
        }

        // 高リスク操作
        if (desc.Contains("削除") || desc.Contains("delete") || desc.Contains("remove") ||
            desc.Contains("送信") || desc.Contains("submit"))
        {
            return DangerLevel.High;
        }

        // 中リスク操作
        if (desc.Contains("書き込み") || desc.Contains("write") || desc.Contains("編集") ||
            desc.Contains("edit") || desc.Contains("移動") || desc.Contains("move"))
        {
            return DangerLevel.Medium;
        }

        return DangerLevel.Low;
    }

    /// <summary>
    /// タスクから操作タイプを判定
    /// </summary>
    private OperationType DetermineOperationType(AgentTask task)
    {
        var desc = task.Description.ToLowerInvariant();

        if (desc.Contains("ファイル") || desc.Contains("file"))
        {
            if (desc.Contains("削除") || desc.Contains("delete")) return OperationType.FileDelete;
            if (desc.Contains("書き込み") || desc.Contains("write") || desc.Contains("作成")) return OperationType.FileWrite;
            if (desc.Contains("読み込み") || desc.Contains("read")) return OperationType.FileRead;
            if (desc.Contains("移動") || desc.Contains("move")) return OperationType.FileMove;
            return OperationType.FileWrite;
        }

        if (desc.Contains("フォルダ") || desc.Contains("directory") || desc.Contains("フォルダー"))
        {
            if (desc.Contains("削除")) return OperationType.FolderDelete;
            if (desc.Contains("作成")) return OperationType.FolderCreate;
            if (desc.Contains("移動")) return OperationType.FolderMove;
            return OperationType.FolderCreate;
        }

        if (desc.Contains("ブラウザ") || desc.Contains("browser") || desc.Contains("web"))
        {
            if (desc.Contains("送信") || desc.Contains("submit")) return OperationType.FormSubmit;
            if (desc.Contains("検索") || desc.Contains("search")) return OperationType.WebSearch;
            if (desc.Contains("移動") || desc.Contains("navigate")) return OperationType.WebNavigate;
            return OperationType.WebNavigate;
        }

        if (desc.Contains("コマンド") || desc.Contains("command") || desc.Contains("powershell"))
        {
            return OperationType.CommandExecute;
        }

        if (desc.Contains("決済") || desc.Contains("payment")) return OperationType.Payment;
        if (desc.Contains("認証") || desc.Contains("auth") || desc.Contains("login")) return OperationType.Authentication;
        if (desc.Contains("抽出") || desc.Contains("extract")) return OperationType.DataExtraction;

        return OperationType.Unknown;
    }

    private async Task<bool> RequestApprovalAsync(AgentTask task)
    {
        // IApprovalHandlerがある場合はそれを使用
        if (_approvalHandler != null && _approvalHandler.IsAvailable)
        {
            var request = new ApprovalRequest
            {
                TaskId = task.Id,
                TaskDescription = task.Description,
                DangerLevel = DetermineDangerLevel(task),
                OperationType = DetermineOperationType(task),
                Context = new Dictionary<string, object>
                {
                    ["GoalId"] = _currentGoal?.Id ?? string.Empty,
                    ["Priority"] = task.Priority,
                    ["RetryCount"] = task.RetryCount
                }
            };

            var result = await _approvalHandler.RequestApprovalAsync(request);

            // 結果に基づいて処理
            return result.Action switch
            {
                ApprovalAction.Approve => true,
                ApprovalAction.Deny => false,
                ApprovalAction.Skip => false,
                ApprovalAction.Cancel => throw new OperationCanceledException("ユーザーが操作をキャンセルしました"),
                _ => false
            };
        }

        // レガシー: イベントベースの承認
        await _approvalLock.WaitAsync();
        try
        {
            _approvalTcs = new TaskCompletionSource<bool>();

            OnApprovalRequired(new ApprovalRequestEventArgs
            {
                TaskId = task.Id,
                TaskDescription = task.Description,
                DangerLevel = DetermineDangerLevel(task),
                OperationType = DetermineOperationType(task),
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
    /// 承認を与える（レガシー）
    /// </summary>
    public void ApproveTask(string taskId)
    {
        _approvalTcs?.TrySetResult(true);
    }

    /// <summary>
    /// タスクを拒否する（レガシー）
    /// </summary>
    public void RejectTask(string taskId)
    {
        _approvalTcs?.TrySetResult(false);
    }

    /// <summary>
    /// 承認ハンドラーを設定または変更
    /// </summary>
    public void SetApprovalHandler(IApprovalHandler handler)
    {
        _approvalHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    #endregion

    #region ユーザー入力

    /// <summary>
    /// ユーザーから入力を取得する
    /// </summary>
    /// <param name="prompt">プロンプトメッセージ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ユーザー入力</returns>
    public async Task<InputResult> GetUserInputAsync(string? prompt = null, CancellationToken cancellationToken = default)
    {
        // IInputHandlerがある場合はそれを使用
        if (_inputHandler != null && _inputHandler.IsAvailable)
        {
            return await _inputHandler.GetInputAsync(prompt, cancellationToken);
        }

        // レガシー: イベントベースの入力
        var tcs = new TaskCompletionSource<string>();
        OnInputRequested(new UserInputEventArgs
        {
            Prompt = prompt ?? string.Empty,
            ResponseTask = tcs
        });

        var input = await tcs.Task;
        return new InputResult
        {
            Text = input,
            Type = InputType.Text
        };
    }

    /// <summary>
    /// ユーザーに選択肢を提示して選択させる
    /// </summary>
    public async Task<SelectionResult> GetUserSelectionAsync(
        IReadOnlyList<SelectionOption> options,
        string? prompt = null,
        CancellationToken cancellationToken = default)
    {
        if (_inputHandler != null && _inputHandler.IsAvailable)
        {
            return await _inputHandler.GetSelectionAsync(options, prompt, cancellationToken);
        }

        // フォールバック: 最初の推奨オプション
        var recommended = options.FirstOrDefault(o => o.IsRecommended) ?? options.FirstOrDefault();
        return new SelectionResult
        {
            SelectedIndex = recommended != null ? FindIndex(options, recommended) : -1,
            SelectedOption = recommended,
            IsCancelled = recommended == null
        };
    }

    /// <summary>
    /// 入力ハンドラーを設定または変更
    /// </summary>
    public void SetInputHandler(IInputHandler handler)
    {
        _inputHandler = handler ?? throw new ArgumentNullException(nameof(handler));
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

    protected virtual void OnInputRequested(UserInputEventArgs e)
    {
        InputRequested?.Invoke(this, e);
    }

    #endregion

    #region ヘルパーメソッド

    private static int FindIndex<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], item))
            {
                return i;
            }
        }
        return -1;
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
    public DangerLevel DangerLevel { get; set; } = DangerLevel.Medium;
    public OperationType OperationType { get; set; } = OperationType.Unknown;
    public TaskCompletionSource<bool> ResponseTask { get; set; } = null!;
}

public class GoalCompletedEventArgs : EventArgs
{
    public AgentGoal Goal { get; set; } = null!;
    public bool Success { get; set; }
}

public class UserInputEventArgs : EventArgs
{
    public string Prompt { get; set; } = string.Empty;
    public TaskCompletionSource<string> ResponseTask { get; set; } = null!;
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
