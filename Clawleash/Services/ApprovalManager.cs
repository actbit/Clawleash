using Clawleash.Services.Handlers;
using Microsoft.Extensions.Logging;

namespace Clawleash.Services;

/// <summary>
/// 複数の承認ハンドラーを管理し、適切なハンドラーに処理を委譲する
/// CLI、GUI、Webなど複数のインターフェースを同時にサポート可能
/// </summary>
public class ApprovalManager : IApprovalHandler
{
    private readonly ILogger<ApprovalManager> _logger;
    private readonly List<IApprovalHandler> _handlers = new();
    private readonly Dictionary<string, IApprovalHandler> _handlerRegistry = new(StringComparer.OrdinalIgnoreCase);
    private IApprovalHandler? _defaultHandler;
    private IApprovalHandler? _fallbackHandler;

    /// <summary>
    /// マネージャー自体が利用可能かどうか（有効なハンドラーがあればtrue）
    /// </summary>
    public bool IsAvailable => _handlers.Any(h => h.IsAvailable);

    public ApprovalManager(ILogger<ApprovalManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 承認ハンドラーを登録する
    /// </summary>
    /// <param name="handler">ハンドラー</param>
    /// <param name="name">ハンドラー名（オプション、参照用）</param>
    /// <param name="isDefault">デフォルトハンドラーにするか</param>
    /// <param name="isFallback">フォールバックハンドラーにするか</param>
    public void RegisterHandler(
        IApprovalHandler handler,
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
            _logger.LogDebug("承認ハンドラーを登録: {Name} ({Type})", name, handler.GetType().Name);
        }

        if (isDefault)
        {
            _defaultHandler = handler;
            _logger.LogInformation("デフォルト承認ハンドラーを設定: {Type}", handler.GetType().Name);
        }

        if (isFallback)
        {
            _fallbackHandler = handler;
            _logger.LogInformation("フォールバック承認ハンドラーを設定: {Type}", handler.GetType().Name);
        }
    }

    /// <summary>
    /// 名前でハンドラーを取得
    /// </summary>
    public IApprovalHandler? GetHandler(string name)
    {
        return _handlerRegistry.TryGetValue(name, out var handler) ? handler : null;
    }

    /// <summary>
    /// 利用可能なハンドラー一覧を取得
    /// </summary>
    public IReadOnlyList<IApprovalHandler> GetAvailableHandlers()
    {
        return _handlers.Where(h => h.IsAvailable).ToList().AsReadOnly();
    }

    /// <summary>
    /// 承認リクエストを処理する
    /// デフォルトハンドラー、または最初に利用可能なハンドラーを使用
    /// </summary>
    public async Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogInformation(
            "承認リクエスト受信: {RequestId}, 危険度: {DangerLevel}, 操作: {OperationType}",
            request.RequestId, request.DangerLevel, request.OperationType);

        // 指定されたハンドラーがある場合はそれを使用
        if (!string.IsNullOrEmpty(request.Context.GetValueOrDefault("HandlerName") as string))
        {
            var handlerName = request.Context["HandlerName"] as string;
            var specificHandler = GetHandler(handlerName!);
            if (specificHandler != null && specificHandler.IsAvailable)
            {
                _logger.LogDebug("指定ハンドラーを使用: {HandlerName}", handlerName);
                return await specificHandler.RequestApprovalAsync(request, cancellationToken);
            }
        }

        // デフォルトハンドラーを試行
        if (_defaultHandler != null && _defaultHandler.IsAvailable)
        {
            _logger.LogDebug("デフォルトハンドラーを使用: {Type}", _defaultHandler.GetType().Name);
            return await _defaultHandler.RequestApprovalAsync(request, cancellationToken);
        }

        // 利用可能なハンドラーを優先度順に試行
        var availableHandler = GetBestAvailableHandler(request);
        if (availableHandler != null)
        {
            _logger.LogDebug("利用可能なハンドラーを使用: {Type}", availableHandler.GetType().Name);
            return await availableHandler.RequestApprovalAsync(request, cancellationToken);
        }

        // フォールバックハンドラー
        if (_fallbackHandler != null && _fallbackHandler.IsAvailable)
        {
            _logger.LogDebug("フォールバックハンドラーを使用: {Type}", _fallbackHandler.GetType().Name);
            return await _fallbackHandler.RequestApprovalAsync(request, cancellationToken);
        }

        // ハンドラーがない場合は安全側に倒して拒否
        _logger.LogWarning("利用可能な承認ハンドラーがありません。リクエストを拒否します: {RequestId}", request.RequestId);
        return new ApprovalResult
        {
            Approved = false,
            Action = ApprovalAction.Deny,
            Comment = "利用可能な承認ハンドラーがありません"
        };
    }

    /// <summary>
    /// リクエストに基づいて最適なハンドラーを選択
    /// </summary>
    private IApprovalHandler? GetBestAvailableHandler(ApprovalRequest request)
    {
        // 危険度に基づく優先度
        if (request.DangerLevel >= DangerLevel.High)
        {
            // 高リスク操作ではより対話的なハンドラーを優先
            var interactiveHandler = _handlers.FirstOrDefault(h =>
                h.IsAvailable && h is ICliApprovalHandler);
            if (interactiveHandler != null)
            {
                return interactiveHandler;
            }
        }

        // 最初に利用可能なハンドラー
        return _handlers.FirstOrDefault(h => h.IsAvailable);
    }

    /// <summary>
    /// すべてのハンドラーに承認を要求（全員の承認が必要）
    /// </summary>
    public async Task<ApprovalResult> RequestApprovalFromAllAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var availableHandlers = GetAvailableHandlers();
        if (availableHandlers.Count == 0)
        {
            return new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = "利用可能な承認ハンドラーがありません"
            };
        }

        var results = new List<ApprovalResult>();
        foreach (var handler in availableHandlers)
        {
            try
            {
                var result = await handler.RequestApprovalAsync(request, cancellationToken);
                results.Add(result);

                // キャンセルは即座に反映
                if (result.Action == ApprovalAction.Cancel)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ハンドラーでのエラー: {Type}", handler.GetType().Name);
            }
        }

        // すべての承認が必要
        var allApproved = results.All(r => r.Approved);
        var anyCancel = results.Any(r => r.Action == ApprovalAction.Cancel);
        var anyDeny = results.Any(r => r.Action == ApprovalAction.Deny);

        return new ApprovalResult
        {
            Approved = allApproved,
            Action = anyCancel ? ApprovalAction.Cancel
                     : anyDeny ? ApprovalAction.Deny
                     : ApprovalAction.Approve,
            Comment = string.Join("; ", results.Where(r => !string.IsNullOrEmpty(r.Comment)).Select(r => r.Comment))
        };
    }

    /// <summary>
    /// いずれかのハンドラーの承認を取得（いずれかの承認で十分）
    /// </summary>
    public async Task<ApprovalResult> RequestApprovalFromAnyAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var availableHandlers = GetAvailableHandlers();
        if (availableHandlers.Count == 0)
        {
            return new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = "利用可能な承認ハンドラーがありません"
            };
        }

        foreach (var handler in availableHandlers)
        {
            try
            {
                var result = await handler.RequestApprovalAsync(request, cancellationToken);

                // 承認またはキャンセルは即座に返す
                if (result.Approved || result.Action == ApprovalAction.Cancel)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ハンドラーでのエラー: {Type}", handler.GetType().Name);
            }
        }

        return new ApprovalResult
        {
            Approved = false,
            Action = ApprovalAction.Deny,
            Comment = "すべてのハンドラーで拒否されました"
        };
    }
}
