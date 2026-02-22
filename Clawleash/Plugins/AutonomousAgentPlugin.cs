using System.ComponentModel;
using System.Text.Json;
using Clawleash.Models;
using Clawleash.Services;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// 自律エージェントプラグイン
/// 目標の計画・実行・評価・修正を自律的に行う機能を提供
/// </summary>
public class AutonomousAgentPlugin
{
    private readonly AutonomousAgentService _agentService;
    private readonly AutonomousSettings _settings;

    public AutonomousAgentPlugin(Kernel kernel, AutonomousSettings? settings = null)
    {
        _settings = settings ?? new AutonomousSettings();
        _agentService = new AutonomousAgentService(kernel, _settings);

        // イベントハンドラーを設定
        _agentService.ProgressUpdated += (s, e) =>
        {
            Console.WriteLine($"[進捗] ステップ {e.Step}/{e.TotalSteps}: {e.Status}");
        };

        _agentService.ApprovalRequired += (s, e) =>
        {
            Console.WriteLine($"\n⚠️ 承認が必要な操作です:");
            Console.WriteLine($"タスク: {e.TaskDescription}");
            Console.Write("承認しますか？ (y/n): ");
            var response = Console.ReadLine();
            if (response?.ToLowerInvariant() == "y")
            {
                _agentService.ApproveTask(e.TaskId);
            }
            else
            {
                _agentService.RejectTask(e.TaskId);
            }
        };

        _agentService.GoalCompleted += (s, e) =>
        {
            var status = e.Success ? "✅ 完了" : "❌ 失敗";
            Console.WriteLine($"\n{status}: {e.Goal.Description}");
        };
    }

    #region 目標設定・実行

    [KernelFunction, Description("自律的に目標を達成するための計画を立てて実行します")]
    public async Task<string> ExecuteGoalAutonomously(
        [Description("達成したい目標の説明")] string goalDescription,
        [Description("最大ステップ数（デフォルト: 10）")] int maxSteps = 10)
    {
        try
        {
            if (_agentService.IsRunning)
            {
                return "エラー: 既に別の目標を実行中です。まず CancelExecution を実行してください。";
            }

            _settings.MaxSteps = maxSteps;

            // 非同期で実行開始
            var goal = await _agentService.ExecuteGoalAsync(goalDescription);

            var summary = $"""
                ## 実行結果

                **目標**: {goal.Description}
                **ステータス**: {goal.Status}
                **総ステップ数**: {goal.CurrentStep}

                ### タスク結果
                {FormatTaskResults(goal.Tasks)}

                ### 完了率
                完了: {goal.Tasks.Count(t => t.Status == AgentTaskStatus.Completed)}/{goal.Tasks.Count}
                """;

            return summary;
        }
        catch (Exception ex)
        {
            return $"エラー: 目標の実行に失敗しました: {ex.Message}";
        }
    }

    [KernelFunction, Description("目標の計画だけを作成します（実行はしない）")]
    public async Task<string> PlanGoal(
        [Description("達成したい目標の説明")] string goalDescription)
    {
        try
        {
            var goal = await _agentService.PlanGoalAsync(goalDescription);

            if (goal.Status == GoalStatus.Failed)
            {
                return "エラー: 計画の作成に失敗しました";
            }

            var plan = $"""
                ## タスク計画

                **目標**: {goal.Description}
                **タスク数**: {goal.Tasks.Count}

                ### タスクリスト
                {string.Join("\n", goal.Tasks.Select((t, i) => $"{i + 1}. [{t.Priority}] {t.Description}{(t.Status == AgentTaskStatus.RequiresApproval ? " ⚠️(要承認)" : "")}"))}

                ExecuteGoalAutonomously でこの計画を実行できます。
                """;

            return plan;
        }
        catch (Exception ex)
        {
            return $"エラー: 計画の作成に失敗しました: {ex.Message}";
        }
    }

    #endregion

    #region 実行制御

    [KernelFunction, Description("現在の実行を一時停止します")]
    public string PauseExecution()
    {
        if (!_agentService.IsRunning)
        {
            return "実行中のタスクはありません";
        }

        _agentService.Pause();
        return "実行を一時停止しました。ResumeExecution で再開できます。";
    }

    [KernelFunction, Description("一時停止中の実行を再開します")]
    public string ResumeExecution()
    {
        _agentService.Resume();
        return "実行を再開しました。";
    }

    [KernelFunction, Description("現在の実行をキャンセルします")]
    public string CancelExecution()
    {
        _agentService.Cancel();
        return "実行をキャンセルしました。";
    }

    [KernelFunction, Description("現在の実行状態を取得します")]
    public string GetExecutionStatus()
    {
        var goal = _agentService.CurrentGoal;
        if (goal == null)
        {
            return "実行中のタスクはありません";
        }

        return $"""
            ## 現在の実行状態

            **目標**: {goal.Description}
            **ステータス**: {goal.Status}
            **進捗**: {goal.CurrentStep}/{goal.Tasks.Count}

            ### タスク状況
            {FormatTaskResults(goal.Tasks)}
            """;
    }

    #endregion

    #region 設定

    [KernelFunction, Description("自律実行の設定を変更します")]
    public string UpdateSettings(
        [Description("最大実行ステップ数")] int? maxSteps = null,
        [Description("最大リトライ回数")] int? maxRetries = null,
        [Description("危険な操作に承認を必要とするか")] bool? requireApprovalForDangerous = null,
        [Description("ファイル削除に承認を必要とするか")] bool? requireApprovalForDeletion = null,
        [Description("フォーム送信に承認を必要とするか")] bool? requireApprovalForForm = null)
    {
        if (maxSteps.HasValue) _settings.MaxSteps = maxSteps.Value;
        if (maxRetries.HasValue) _settings.MaxRetries = maxRetries.Value;
        if (requireApprovalForDangerous.HasValue) _settings.RequireApprovalForDangerousOperations = requireApprovalForDangerous.Value;
        if (requireApprovalForDeletion.HasValue) _settings.RequireApprovalForFileDeletion = requireApprovalForDeletion.Value;
        if (requireApprovalForForm.HasValue) _settings.RequireApprovalForFormSubmission = requireApprovalForForm.Value;

        return $"""
            設定を更新しました:
            - 最大ステップ数: {_settings.MaxSteps}
            - 最大リトライ回数: {_settings.MaxRetries}
            - 危険操作に承認必要: {_settings.RequireApprovalForDangerousOperations}
            - ファイル削除に承認必要: {_settings.RequireApprovalForFileDeletion}
            - フォーム送信に承認必要: {_settings.RequireApprovalForFormSubmission}
            """;
    }

    [KernelFunction, Description("現在の設定を取得します")]
    public string GetSettings()
    {
        return JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
    }

    #endregion

    #region 自己評価

    [KernelFunction, Description("最後の実行結果を評価し、改善点を提案します")]
    public async Task<string> EvaluateLastExecution()
    {
        var goal = _agentService.CurrentGoal;
        if (goal == null)
        {
            return "評価する実行結果がありません";
        }

        var failedTasks = goal.Tasks.Where(t => t.Status == AgentTaskStatus.Failed).ToList();
        if (failedTasks.Count == 0)
        {
            return "すべてのタスクが成功しました。改善点はありません。";
        }

        var evaluation = $"""
            ## 実行評価

            **成功率**: {goal.Tasks.Count(t => t.Status == AgentTaskStatus.Completed)}/{goal.Tasks.Count}

            ### 失敗したタスク
            {string.Join("\n", failedTasks.Select(t => $"- {t.Description}: {t.Error}"))}

            ### 推奨される改善策
            1. タスクをより小さな単位に分割する
            2. エラーハンドリングを強化する
            3. 事前にリソースの可用性を確認する
            """;

        return evaluation;
    }

    #endregion

    #region プライベートメソッド

    private static string FormatTaskResults(List<AgentTask> tasks)
    {
        var result = new System.Text.StringBuilder();

        foreach (var task in tasks)
        {
            var status = task.Status switch
            {
                AgentTaskStatus.Completed => "✅",
                AgentTaskStatus.Failed => "❌",
                AgentTaskStatus.InProgress => "🔄",
                AgentTaskStatus.Skipped => "⏭️",
                AgentTaskStatus.RequiresApproval => "⚠️",
                _ => "⏳"
            };

            result.AppendLine($"{status} {task.Description}");
            if (!string.IsNullOrEmpty(task.Result))
            {
                result.AppendLine($"   結果: {task.Result}");
            }
            if (!string.IsNullOrEmpty(task.Error))
            {
                result.AppendLine($"   エラー: {task.Error}");
            }
        }

        return result.ToString();
    }

    #endregion
}
