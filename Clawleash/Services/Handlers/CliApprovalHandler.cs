using System.Text;
using Microsoft.Extensions.Logging;

namespace Clawleash.Services.Handlers;

/// <summary>
/// コンソールベースの承認ハンドラー
/// 標準入出力を使用してユーザーに承認を求める
/// </summary>
public class CliApprovalHandler : ICliApprovalHandler
{
    private readonly ILogger<CliApprovalHandler> _logger;
    private readonly ApprovalDisplaySettings _displaySettings;
    private bool _isConsoleAvailable;

    /// <summary>
    /// 入力ストリーム（テスト用）
    /// </summary>
    public TextReader? InputOverride { get; set; }

    /// <summary>
    /// 出力ストリーム（テスト用）
    /// </summary>
    public TextWriter? OutputOverride { get; set; }

    /// <summary>
    /// コンソールが利用可能かどうか
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (InputOverride != null && OutputOverride != null)
            {
                return true; // テスト用オーバーライドがある場合
            }

            try
            {
                // コンソールが利用可能かチェック
                _isConsoleAvailable = !Console.IsInputRedirected || !Console.IsOutputRedirected;
                return _isConsoleAvailable;
            }
            catch
            {
                return false;
            }
        }
    }

    public CliApprovalHandler(
        ILogger<CliApprovalHandler> logger,
        ApprovalDisplaySettings? displaySettings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _displaySettings = displaySettings ?? new ApprovalDisplaySettings();
    }

    /// <summary>
    /// 承認リクエストを処理する
    /// </summary>
    public async Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var output = OutputOverride ?? Console.Out;
        var input = InputOverride ?? Console.In;

        try
        {
            // リクエスト情報を表示
            await DisplayRequestAsync(request, output, cancellationToken);

            // ユーザーの入力を待つ
            var result = await GetUserInputAsync(request, input, output, cancellationToken);

            _logger.LogInformation(
                "承認結果: {RequestId} -> {Action} ({Approved})",
                request.RequestId, result.Action, result.Approved);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("承認リクエストがキャンセルされました: {RequestId}", request.RequestId);
            return new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Cancel,
                Comment = "操作がキャンセルされました"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "承認処理中にエラーが発生しました: {RequestId}", request.RequestId);
            return new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = $"エラー: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// リクエスト情報を表示
    /// </summary>
    private async Task DisplayRequestAsync(
        ApprovalRequest request,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine(_displaySettings.SeparatorLine);
        sb.AppendLine("## 承認リクエスト");
        sb.AppendLine(_displaySettings.SeparatorLine);
        sb.AppendLine();

        // ヘッダー情報
        sb.AppendLine($"リクエストID: {request.RequestId}");
        sb.AppendLine($"時刻: {request.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 操作情報
        sb.AppendLine("### 操作内容");
        sb.AppendLine($"種類: {GetOperationTypeDisplay(request.OperationType)}");
        sb.AppendLine($"説明: {request.TaskDescription}");
        sb.AppendLine();

        // 危険度
        var (dangerColor, dangerText) = GetDangerLevelDisplay(request.DangerLevel);
        sb.AppendLine($"### 危険度: {dangerText}");
        sb.AppendLine();

        // 追加コンテキスト
        if (request.Context.Count > 0)
        {
            sb.AppendLine("### 詳細情報");
            foreach (var kvp in request.Context)
            {
                sb.AppendLine($"- {kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine();
        }

        // 選択肢
        sb.AppendLine("### アクションを選択してください:");
        sb.AppendLine("  [Y] 許可 - この操作を許可する");
        sb.AppendLine("  [N] 拒否 - この操作を拒否する");
        sb.AppendLine("  [S] スキップ - この操作をスキップして続行");
        sb.AppendLine("  [C] キャンセル - 全体を中止");
        sb.AppendLine("  [A] 常に許可 - 今後同様の操作を自動許可");
        sb.AppendLine();

        await output.WriteAsync(sb.ToString());
        await output.FlushAsync();
    }

    /// <summary>
    /// ユーザー入力を取得
    /// </summary>
    private async Task<ApprovalResult> GetUserInputAsync(
        ApprovalRequest request,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteAsync("選択 [Y/N/S/C/A]: ");
            await output.FlushAsync();

            var line = await input.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var choice = line.Trim().ToUpperInvariant()[0];

            return choice switch
            {
                'Y' => new ApprovalResult
                {
                    Approved = true,
                    Action = ApprovalAction.Approve,
                    Comment = "ユーザーが許可"
                },
                'N' => new ApprovalResult
                {
                    Approved = false,
                    Action = ApprovalAction.Deny,
                    Comment = "ユーザーが拒否"
                },
                'S' => new ApprovalResult
                {
                    Approved = false,
                    Action = ApprovalAction.Skip,
                    Comment = "ユーザーがスキップ"
                },
                'C' => new ApprovalResult
                {
                    Approved = false,
                    Action = ApprovalAction.Cancel,
                    Comment = "ユーザーがキャンセル"
                },
                'A' => new ApprovalResult
                {
                    Approved = true,
                    Action = ApprovalAction.Approve,
                    Comment = "常に許可"
                    // TODO: AlwaysApprove パターンの記録
                },
                _ => await HandleInvalidChoice(output)
            };
        }

        return new ApprovalResult
        {
            Approved = false,
            Action = ApprovalAction.Cancel,
            Comment = "タイムアウト"
        };
    }

    private async Task<ApprovalResult> HandleInvalidChoice(TextWriter output)
    {
        await output.WriteLineAsync("無効な選択です。Y, N, S, C, A のいずれかを入力してください。");
        await output.FlushAsync();
        // ループを続けるために特別な結果を返さない
        throw new InvalidOperationException("Invalid choice - should retry");
    }

    private static string GetOperationTypeDisplay(OperationType type)
    {
        return type switch
        {
            OperationType.FileRead => "ファイル読み込み",
            OperationType.FileWrite => "ファイル書き込み",
            OperationType.FileDelete => "ファイル削除",
            OperationType.FileMove => "ファイル移動",
            OperationType.FolderCreate => "フォルダ作成",
            OperationType.FolderDelete => "フォルダ削除",
            OperationType.FolderMove => "フォルダ移動",
            OperationType.WebNavigate => "Webページ移動",
            OperationType.FormSubmit => "フォーム送信",
            OperationType.WebSearch => "Web検索",
            OperationType.CommandExecute => "コマンド実行",
            OperationType.DataExtraction => "データ抽出",
            OperationType.Authentication => "認証操作",
            OperationType.Payment => "決済操作",
            _ => "不明な操作"
        };
    }

    private static (string Color, string Text) GetDangerLevelDisplay(DangerLevel level)
    {
        return level switch
        {
            DangerLevel.Low => ("Green", "低 [■□□□]"),
            DangerLevel.Medium => ("Yellow", "中 [■■□□]"),
            DangerLevel.High => ("Orange", "高 [■■■□]"),
            DangerLevel.Critical => ("Red", "重要 [■■■■]"),
            _ => ("Gray", "不明")
        };
    }
}

/// <summary>
/// 承認表示設定
/// </summary>
public class ApprovalDisplaySettings
{
    public string SeparatorLine { get; set; } = new('=', 60);
    public bool UseColors { get; set; } = true;
    public bool ShowStackTrace { get; set; } = false;
}
