using System.Text;
using Microsoft.Extensions.Logging;

namespace Clawleash.Services.Handlers;

/// <summary>
/// コンソールベースの入力ハンドラー
/// 標準入出力を使用してユーザーと対話
/// </summary>
public class CliInputHandler : IInputHandler
{
    private readonly ILogger<CliInputHandler> _logger;
    private readonly CliInputSettings _settings;
    private readonly List<InputHistoryEntry> _localHistory = new();
    private Func<string, IEnumerable<string>>? _autoCompleteProvider;
    private CancellationTokenSource? _currentInputCts;
    private bool _isWaiting;

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
                return true;
            }

            try
            {
                return !Console.IsInputRedirected || !Console.IsOutputRedirected;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 入力待機中かどうか
    /// </summary>
    public bool IsWaitingForInput => _isWaiting;

    public CliInputHandler(
        ILogger<CliInputHandler> logger,
        CliInputSettings? settings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? new CliInputSettings();
    }

    /// <summary>
    /// ユーザーから指示を取得する
    /// </summary>
    public async Task<InputResult> GetInputAsync(string? prompt = null, CancellationToken cancellationToken = default)
    {
        var output = OutputOverride ?? Console.Out;
        var input = InputOverride ?? Console.In;

        _currentInputCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isWaiting = true;

        try
        {
            // プロンプトを表示
            if (!string.IsNullOrEmpty(prompt))
            {
                await output.WriteLineAsync(prompt);
            }

            await WritePromptIndicator(output);

            // 入力を読み取る
            var line = await input.ReadLineAsync(_currentInputCts.Token);

            if (line == null)
            {
                return new InputResult
                {
                    IsCancelled = true,
                    Text = string.Empty
                };
            }

            var trimmedLine = line.Trim();

            // 空入力チェック
            if (string.IsNullOrEmpty(trimmedLine))
            {
                return new InputResult
                {
                    Type = InputType.Text,
                    Text = string.Empty
                };
            }

            // 終了コマンドチェック
            if (_settings.ExitCommands.Contains(trimmedLine.ToLowerInvariant()))
            {
                return new InputResult
                {
                    Type = InputType.Command,
                    Text = trimmedLine,
                    IsExitRequest = true
                };
            }

            // 入力タイプを判定
            var inputType = DetermineInputType(trimmedLine);

            // 履歴に追加
            if (_settings.EnableHistory && !string.IsNullOrWhiteSpace(trimmedLine))
            {
                _localHistory.Add(new InputHistoryEntry
                {
                    Text = trimmedLine,
                    Timestamp = DateTime.UtcNow,
                    Type = inputType
                });
            }

            return new InputResult
            {
                Type = inputType,
                Text = trimmedLine
            };
        }
        catch (OperationCanceledException)
        {
            return new InputResult
            {
                IsCancelled = true,
                Text = string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "入力処理中にエラーが発生しました");
            return new InputResult
            {
                IsCancelled = true,
                Text = string.Empty
            };
        }
        finally
        {
            _isWaiting = false;
            _currentInputCts?.Dispose();
            _currentInputCts = null;
        }
    }

    /// <summary>
    /// 選択肢付きでユーザーから指示を取得する
    /// </summary>
    public async Task<SelectionResult> GetSelectionAsync(
        IReadOnlyList<SelectionOption> options,
        string? prompt = null,
        CancellationToken cancellationToken = default)
    {
        var output = OutputOverride ?? Console.Out;
        var input = InputOverride ?? Console.In;

        _isWaiting = true;

        try
        {
            // プロンプトと選択肢を表示
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(prompt))
            {
                sb.AppendLine(prompt);
            }

            sb.AppendLine();

            for (var i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                var shortcut = !string.IsNullOrEmpty(opt.Shortcut) ? $"[{opt.Shortcut}] " : "";
                var recommended = opt.IsRecommended ? " (推奨)" : "";
                var disabled = opt.IsDisabled ? " [無効]" : "";

                sb.AppendLine($"  {i + 1}. {shortcut}{opt.Label}{recommended}{disabled}");

                if (!string.IsNullOrEmpty(opt.Description))
                {
                    sb.AppendLine($"     {opt.Description}");
                }
            }

            sb.AppendLine();
            sb.Append("選択してください (1-" + options.Count + " または q でキャンセル): ");

            await output.WriteAsync(sb.ToString());
            await output.FlushAsync();

            // 入力を読み取る
            var line = await input.ReadLineAsync(cancellationToken);

            if (string.IsNullOrEmpty(line))
            {
                return new SelectionResult { IsCancelled = true };
            }

            var trimmed = line.Trim().ToLowerInvariant();

            // キャンセル
            if (trimmed == "q" || trimmed == "c" || trimmed == "cancel")
            {
                return new SelectionResult { IsCancelled = true };
            }

            // ショートカットで検索
            var shortcutMatch = options.FirstOrDefault(o =>
                o.Shortcut?.Equals(trimmed, StringComparison.OrdinalIgnoreCase) == true);
            if (shortcutMatch != null)
            {
                return new SelectionResult
                {
                    SelectedIndex = FindIndex(options, shortcutMatch),
                    SelectedOption = shortcutMatch
                };
            }

            // 数値で検索
            if (int.TryParse(trimmed, out var index) && index >= 1 && index <= options.Count)
            {
                var selectedOption = options[index - 1];
                if (!selectedOption.IsDisabled)
                {
                    return new SelectionResult
                    {
                        SelectedIndex = index - 1,
                        SelectedOption = selectedOption
                    };
                }

                await output.WriteLineAsync("このオプションは選択できません。");
            }

            await output.WriteLineAsync("無効な選択です。もう一度お試しください。");
            await output.FlushAsync();

            // 再帰的に再試行
            return await GetSelectionAsync(options, prompt, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new SelectionResult { IsCancelled = true };
        }
        finally
        {
            _isWaiting = false;
        }
    }

    /// <summary>
    /// 複数行入力を取得する
    /// </summary>
    public async Task<MultiLineResult> GetMultiLineInputAsync(
        string? prompt = null,
        string? endMarker = null,
        CancellationToken cancellationToken = default)
    {
        var output = OutputOverride ?? Console.Out;
        var input = InputOverride ?? Console.In;

        endMarker ??= _settings.DefaultEndMarker;
        _isWaiting = true;

        try
        {
            if (!string.IsNullOrEmpty(prompt))
            {
                await output.WriteLineAsync(prompt);
            }

            await output.WriteLineAsync($"(空行または '{endMarker}' で終了)");
            await output.FlushAsync();

            var lines = new List<string>();

            while (!cancellationToken.IsCancellationRequested)
            {
                await WritePromptIndicator(output, ">>> ");

                var line = await input.ReadLineAsync(cancellationToken);

                if (line == null)
                {
                    break;
                }

                if (line == endMarker || (string.IsNullOrEmpty(line) && lines.Count > 0))
                {
                    break;
                }

                lines.Add(line);
            }

            return new MultiLineResult
            {
                Lines = lines
            };
        }
        catch (OperationCanceledException)
        {
            return new MultiLineResult
            {
                IsCancelled = true
            };
        }
        finally
        {
            _isWaiting = false;
        }
    }

    /// <summary>
    /// 入力をキャンセルする
    /// </summary>
    public void CancelInput()
    {
        _currentInputCts?.Cancel();
    }

    /// <summary>
    /// 入力履歴を取得
    /// </summary>
    public IReadOnlyList<InputHistoryEntry> GetHistory(int maxCount = 100)
    {
        return _localHistory.TakeLast(maxCount).ToList().AsReadOnly();
    }

    /// <summary>
    /// オートコンプリートプロバイダーを設定
    /// </summary>
    public void SetAutoCompleteProvider(Func<string, IEnumerable<string>>? provider)
    {
        _autoCompleteProvider = provider;
        // TODO: 高度なコンソールでタブ補完を実装
    }

    private async Task WritePromptIndicator(TextWriter output, string? indicator = null)
    {
        indicator ??= _settings.PromptIndicator;
        await output.WriteAsync(indicator);
        await output.FlushAsync();
    }

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

    private static InputType DetermineInputType(string input)
    {
        // コマンド
        if (input.StartsWith("/") || input.StartsWith("!"))
        {
            return InputType.Command;
        }

        // URL
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            return InputType.Url;
        }

        // ファイルパス
        if (input.Contains(Path.DirectorySeparatorChar) ||
            input.Contains(Path.AltDirectorySeparatorChar) ||
            (input.Length >= 2 && input[1] == ':'))
        {
            return InputType.FilePath;
        }

        return InputType.Text;
    }
}

/// <summary>
/// CLI入力の設定
/// </summary>
public class CliInputSettings
{
    /// <summary>
    /// プロンプト表示文字
    /// </summary>
    public string PromptIndicator { get; set; } = "> ";

    /// <summary>
    /// 複数行入力の終了マーカー
    /// </summary>
    public string DefaultEndMarker { get; set; } = "END";

    /// <summary>
    /// 終了コマンド
    /// </summary>
    public HashSet<string> ExitCommands { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "exit", "quit", "bye", "q"
    };

    /// <summary>
    /// 履歴を有効にするか
    /// </summary>
    public bool EnableHistory { get; set; } = true;

    /// <summary>
    /// 履歴の最大件数
    /// </summary>
    public int MaxHistoryCount { get; set; } = 1000;
}
