using Microsoft.Extensions.Logging;

namespace Clawleash.Services.Handlers;

/// <summary>
/// 自動入力ハンドラー（ユーザー介入なし）
/// 事前定義されたコマンドキューまたはスクリプトから入力を提供
/// バッチ処理、テスト、自動化シナリオで使用
/// </summary>
public class BatchInputHandler : IInputHandler
{
    private readonly ILogger<BatchInputHandler> _logger;
    private readonly Queue<string> _commandQueue = new();
    private readonly List<InputHistoryEntry> _history = new();
    private readonly BatchInputSettings _settings;
    private bool _isWaiting;

    /// <summary>
    /// 常に利用可能
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// 入力待機中かどうか
    /// </summary>
    public bool IsWaitingForInput => _isWaiting;

    /// <summary>
    /// 残りのコマンド数
    /// </summary>
    public int RemainingCommands => _commandQueue.Count;

    /// <summary>
    /// キューが空かどうか
    /// </summary>
    public bool IsQueueEmpty => _commandQueue.Count == 0;

    public BatchInputHandler(
        ILogger<BatchInputHandler> logger,
        BatchInputSettings? settings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? new BatchInputSettings();
    }

    /// <summary>
    /// コマンドをキューに追加
    /// </summary>
    public void EnqueueCommand(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return;
        }

        _commandQueue.Enqueue(command);
        _logger.LogDebug("コマンドをキューに追加: {Command}", command);
    }

    /// <summary>
    /// 複数のコマンドを一括追加
    /// </summary>
    public void EnqueueCommands(IEnumerable<string> commands)
    {
        foreach (var cmd in commands)
        {
            EnqueueCommand(cmd);
        }
    }

    /// <summary>
    /// スクリプトファイルからコマンドを読み込む
    /// </summary>
    public void LoadScript(string scriptPath)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"スクリプトファイルが見つかりません: {scriptPath}");
        }

        var lines = File.ReadAllLines(scriptPath);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // 空行とコメントをスキップ
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
            {
                continue;
            }

            // インラインコメントを削除
            var commentIndex = trimmed.IndexOf('#');
            if (commentIndex > 0)
            {
                trimmed = trimmed[..commentIndex].Trim();
            }

            if (!string.IsNullOrEmpty(trimmed))
            {
                EnqueueCommand(trimmed);
            }
        }

        _logger.LogInformation("スクリプトから {Count} コマンドを読み込みました: {Path}", _commandQueue.Count, scriptPath);
    }

    /// <summary>
    /// ユーザーから指示を取得する（キューから取り出し）
    /// </summary>
    public Task<InputResult> GetInputAsync(string? prompt = null, CancellationToken cancellationToken = default)
    {
        _isWaiting = true;

        try
        {
            if (_commandQueue.Count == 0)
            {
                _logger.LogDebug("コマンドキューが空です");

                // 空の時のデフォルト動作
                if (_settings.EmptyQueueBehavior == EmptyQueueBehavior.Exit)
                {
                    return Task.FromResult(new InputResult
                    {
                        Type = InputType.Command,
                        Text = "exit",
                        IsExitRequest = true
                    });
                }

                if (_settings.EmptyQueueBehavior == EmptyQueueBehavior.Wait)
                {
                    // 指定時間待機
                    Thread.Sleep(_settings.WaitTimeoutMs);
                    return Task.FromResult(new InputResult
                    {
                        IsCancelled = true,
                        Text = string.Empty
                    });
                }

                // Cancel
                return Task.FromResult(new InputResult
                {
                    IsCancelled = true,
                    Text = string.Empty
                });
            }

            var command = _commandQueue.Dequeue();
            _logger.LogDebug("コマンドを実行: {Command}", command);

            var inputType = DetermineInputType(command);

            var result = new InputResult
            {
                Type = inputType,
                Text = command
            };

            // 履歴に追加
            _history.Add(new InputHistoryEntry
            {
                Text = command,
                Timestamp = DateTime.UtcNow,
                Type = inputType
            });

            // コマンド間の遅延
            if (_settings.CommandDelayMs > 0)
            {
                Thread.Sleep(_settings.CommandDelayMs);
            }

            return Task.FromResult(result);
        }
        finally
        {
            _isWaiting = false;
        }
    }

    /// <summary>
    /// 選択肢付きでユーザーから指示を取得する
    /// </summary>
    public Task<SelectionResult> GetSelectionAsync(
        IReadOnlyList<SelectionOption> options,
        string? prompt = null,
        CancellationToken cancellationToken = default)
    {
        // デフォルト選択を返す
        var defaultIndex = _settings.DefaultSelectionIndex;

        if (defaultIndex >= 0 && defaultIndex < options.Count)
        {
            return Task.FromResult(new SelectionResult
            {
                SelectedIndex = defaultIndex,
                SelectedOption = options[defaultIndex]
            });
        }

        // 推奨オプションを探す
        var recommended = options.FirstOrDefault(o => o.IsRecommended && !o.IsDisabled);
        if (recommended != null)
        {
            return Task.FromResult(new SelectionResult
            {
                SelectedIndex = FindIndex(options, recommended),
                SelectedOption = recommended
            });
        }

        // 最初の有効なオプション
        var firstValid = options.FirstOrDefault(o => !o.IsDisabled);
        if (firstValid != null)
        {
            return Task.FromResult(new SelectionResult
            {
                SelectedIndex = FindIndex(options, firstValid),
                SelectedOption = firstValid
            });
        }

        return Task.FromResult(new SelectionResult
        {
            IsCancelled = true
        });
    }

    /// <summary>
    /// 複数行入力を取得する
    /// </summary>
    public Task<MultiLineResult> GetMultiLineInputAsync(
        string? prompt = null,
        string? endMarker = null,
        CancellationToken cancellationToken = default)
    {
        // 複数行入力もキューから取得
        var lines = new List<string>();

        while (_commandQueue.Count > 0)
        {
            var line = _commandQueue.Peek();
            if (line == endMarker || line == _settings.DefaultEndMarker)
            {
                _commandQueue.Dequeue();
                break;
            }

            lines.Add(_commandQueue.Dequeue());
        }

        return Task.FromResult(new MultiLineResult
        {
            Lines = lines
        });
    }

    /// <summary>
    /// 入力をキャンセルする
    /// </summary>
    public void CancelInput()
    {
        _isWaiting = false;
    }

    /// <summary>
    /// 入力履歴を取得
    /// </summary>
    public IReadOnlyList<InputHistoryEntry> GetHistory(int maxCount = 100)
    {
        return _history.TakeLast(maxCount).ToList().AsReadOnly();
    }

    /// <summary>
    /// オートコンプリートプロバイダーを設定（何もしない）
    /// </summary>
    public void SetAutoCompleteProvider(Func<string, IEnumerable<string>>? provider)
    {
        // バッチモードではオートコンプリートは不要
    }

    /// <summary>
    /// キューをクリア
    /// </summary>
    public void ClearQueue()
    {
        _commandQueue.Clear();
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
        if (input.StartsWith("/") || input.StartsWith("!"))
        {
            return InputType.Command;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            return InputType.Url;
        }

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
/// バッチ入力の設定
/// </summary>
public class BatchInputSettings
{
    /// <summary>
    /// キューが空の時の動作
    /// </summary>
    public EmptyQueueBehavior EmptyQueueBehavior { get; set; } = EmptyQueueBehavior.Exit;

    /// <summary>
    /// コマンド間の遅延（ミリ秒）
    /// </summary>
    public int CommandDelayMs { get; set; } = 0;

    /// <summary>
    /// 待機タイムアウト（ミリ秒）
    /// </summary>
    public int WaitTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// デフォルトの選択インデックス
    /// </summary>
    public int DefaultSelectionIndex { get; set; } = 0;

    /// <summary>
    /// 複数行入力の終了マーカー
    /// </summary>
    public string DefaultEndMarker { get; set; } = "END";
}

/// <summary>
/// キューが空の時の動作
/// </summary>
public enum EmptyQueueBehavior
{
    /// <summary>
    /// 終了する
    /// </summary>
    Exit,

    /// <summary>
    /// キャンセルを返す
    /// </summary>
    Cancel,

    /// <summary>
    /// 待機する
    /// </summary>
    Wait
}
