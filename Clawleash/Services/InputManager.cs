using Microsoft.Extensions.Logging;

namespace Clawleash.Services;

/// <summary>
/// 複数の入力ハンドラーを管理し、適切なハンドラーに処理を委譲する
/// CLI、GUI、Webなど複数のインターフェースを同時にサポート可能
/// </summary>
public class InputManager : IInputHandler
{
    private readonly ILogger<InputManager> _logger;
    private readonly List<IInputHandler> _handlers = new();
    private readonly Dictionary<string, IInputHandler> _handlerRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InputHistoryEntry> _history = new();
    private readonly object _historyLock = new();
    private IInputHandler? _defaultHandler;
    private IInputHandler? _fallbackHandler;
    private Func<string, IEnumerable<string>>? _autoCompleteProvider;

    /// <summary>
    /// マネージャー自体が利用可能かどうか
    /// </summary>
    public bool IsAvailable => _handlers.Any(h => h.IsAvailable);

    /// <summary>
    /// いずれかのハンドラーが入力待機中かどうか
    /// </summary>
    public bool IsWaitingForInput => _handlers.Any(h => h.IsWaitingForInput);

    public InputManager(ILogger<InputManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 入力ハンドラーを登録する
    /// </summary>
    public void RegisterHandler(
        IInputHandler handler,
        string? name = null,
        bool isDefault = false,
        bool isFallback = false)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _handlers.Add(handler);

        if (!string.IsNullOrEmpty(name))
        {
            _handlerRegistry[name] = handler;
            _logger.LogDebug("入力ハンドラーを登録: {Name} ({Type})", name, handler.GetType().Name);
        }

        if (isDefault)
        {
            _defaultHandler = handler;
            _logger.LogInformation("デフォルト入力ハンドラーを設定: {Type}", handler.GetType().Name);
        }

        if (isFallback)
        {
            _fallbackHandler = handler;
            _logger.LogInformation("フォールバック入力ハンドラーを設定: {Type}", handler.GetType().Name);
        }

        // オートコンプリートプロバイダーを転送
        if (_autoCompleteProvider != null)
        {
            handler.SetAutoCompleteProvider(_autoCompleteProvider);
        }
    }

    /// <summary>
    /// 名前でハンドラーを取得
    /// </summary>
    public IInputHandler? GetHandler(string name)
    {
        return _handlerRegistry.TryGetValue(name, out var handler) ? handler : null;
    }

    /// <summary>
    /// 利用可能なハンドラー一覧を取得
    /// </summary>
    public IReadOnlyList<IInputHandler> GetAvailableHandlers()
    {
        return _handlers.Where(h => h.IsAvailable).ToList().AsReadOnly();
    }

    /// <summary>
    /// ユーザーから指示を取得する
    /// </summary>
    public async Task<InputResult> GetInputAsync(string? prompt = null, CancellationToken cancellationToken = default)
    {
        var handler = GetBestHandler();
        if (handler == null)
        {
            _logger.LogWarning("利用可能な入力ハンドラーがありません");
            return new InputResult
            {
                IsCancelled = true,
                Text = string.Empty
            };
        }

        _logger.LogDebug("入力ハンドラーを使用: {Type}", handler.GetType().Name);
        var result = await handler.GetInputAsync(prompt, cancellationToken);

        // 履歴に記録
        if (result.HasText)
        {
            AddToHistory(result);
        }

        return result;
    }

    /// <summary>
    /// 選択肢付きでユーザーから指示を取得する
    /// </summary>
    public async Task<SelectionResult> GetSelectionAsync(
        IReadOnlyList<SelectionOption> options,
        string? prompt = null,
        CancellationToken cancellationToken = default)
    {
        var handler = GetBestHandler();
        if (handler == null)
        {
            _logger.LogWarning("利用可能な入力ハンドラーがありません");
            return new SelectionResult
            {
                IsCancelled = true
            };
        }

        return await handler.GetSelectionAsync(options, prompt, cancellationToken);
    }

    /// <summary>
    /// 複数行入力を取得する
    /// </summary>
    public async Task<MultiLineResult> GetMultiLineInputAsync(
        string? prompt = null,
        string? endMarker = null,
        CancellationToken cancellationToken = default)
    {
        var handler = GetBestHandler();
        if (handler == null)
        {
            _logger.LogWarning("利用可能な入力ハンドラーがありません");
            return new MultiLineResult
            {
                IsCancelled = true
            };
        }

        return await handler.GetMultiLineInputAsync(prompt, endMarker, cancellationToken);
    }

    /// <summary>
    /// 入力をキャンセルする
    /// </summary>
    public void CancelInput()
    {
        foreach (var handler in _handlers)
        {
            try
            {
                handler.CancelInput();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "入力キャンセル中にエラー: {Type}", handler.GetType().Name);
            }
        }
    }

    /// <summary>
    /// 入力履歴を取得
    /// </summary>
    public IReadOnlyList<InputHistoryEntry> GetHistory(int maxCount = 100)
    {
        lock (_historyLock)
        {
            return _history.TakeLast(maxCount).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// オートコンプリートプロバイダーを設定
    /// </summary>
    public void SetAutoCompleteProvider(Func<string, IEnumerable<string>>? provider)
    {
        _autoCompleteProvider = provider;
        foreach (var handler in _handlers)
        {
            handler.SetAutoCompleteProvider(provider);
        }
    }

    /// <summary>
    /// 履歴をクリア
    /// </summary>
    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _history.Clear();
        }
    }

    /// <summary>
    /// 履歴から検索
    /// </summary>
    public IEnumerable<InputHistoryEntry> SearchHistory(string query, int maxCount = 50)
    {
        lock (_historyLock)
        {
            return _history
                .Where(h => h.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                .TakeLast(maxCount)
                .ToList();
        }
    }

    private IInputHandler? GetBestHandler()
    {
        // デフォルトハンドラー
        if (_defaultHandler != null && _defaultHandler.IsAvailable)
        {
            return _defaultHandler;
        }

        // 最初に利用可能なハンドラー
        var available = _handlers.FirstOrDefault(h => h.IsAvailable);
        if (available != null)
        {
            return available;
        }

        // フォールバック
        return _fallbackHandler;
    }

    private void AddToHistory(InputResult result)
    {
        lock (_historyLock)
        {
            _history.Add(new InputHistoryEntry
            {
                Text = result.Text,
                Timestamp = result.Timestamp,
                Type = result.Type,
                TaskId = result.Context.GetValueOrDefault("TaskId") as string
            });

            // 履歴サイズを制限
            while (_history.Count > 1000)
            {
                _history.RemoveAt(0);
            }
        }
    }
}
