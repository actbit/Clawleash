using Microsoft.Extensions.Logging;

namespace Clawleash.Services.Handlers;

/// <summary>
/// 自動承認ハンドラー（ユーザー介入なし）
/// 設定に基づいて自動的に許可/拒否を決定
/// 危険度に基づくルールベースの承認が可能
/// </summary>
public class SilentApprovalHandler : IApprovalHandler
{
    private readonly ILogger<SilentApprovalHandler> _logger;
    private readonly SilentApprovalSettings _settings;

    /// <summary>
    /// 常に利用可能
    /// </summary>
    public bool IsAvailable => true;

    public SilentApprovalHandler(
        ILogger<SilentApprovalHandler> logger,
        SilentApprovalSettings? settings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? new SilentApprovalSettings();
    }

    /// <summary>
    /// 設定に基づいて自動的に承認を決定
    /// </summary>
    public Task<ApprovalResult> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _logger.LogDebug(
            "自動承認処理: {RequestId}, 危険度: {DangerLevel}, 操作: {OperationType}",
            request.RequestId, request.DangerLevel, request.OperationType);

        // デフォルト動作を確認
        if (_settings.DefaultAction == ApprovalAction.Deny)
        {
            return Task.FromResult(new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = "デフォルトで拒否"
            });
        }

        // 許可する操作タイプかチェック
        if (_settings.AllowedOperations.Count > 0 &&
            !_settings.AllowedOperations.Contains(request.OperationType))
        {
            _logger.LogInformation("操作タイプが許可リストにありません: {OperationType}", request.OperationType);
            return Task.FromResult(new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = $"操作タイプ {request.OperationType} は許可されていません"
            });
        }

        // 拒否する操作タイプかチェック
        if (_settings.DeniedOperations.Contains(request.OperationType))
        {
            _logger.LogInformation("操作タイプが拒否リストにあります: {OperationType}", request.OperationType);
            return Task.FromResult(new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = $"操作タイプ {request.OperationType} は拒否されています"
            });
        }

        // 最大危険度をチェック
        if (request.DangerLevel > _settings.MaxAllowedDangerLevel)
        {
            _logger.LogWarning(
                "危険度が許可レベルを超えています: {DangerLevel} > {MaxAllowed}",
                request.DangerLevel, _settings.MaxAllowedDangerLevel);
            return Task.FromResult(new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = $"危険度 {request.DangerLevel} が許可値 {_settings.MaxAllowedDangerLevel} を超えています"
            });
        }

        // Critical操作は別途確認
        if (request.DangerLevel == DangerLevel.Critical && !_settings.AllowCriticalOperations)
        {
            _logger.LogWarning("重要操作が許可されていません: {OperationType}", request.OperationType);
            return Task.FromResult(new ApprovalResult
            {
                Approved = false,
                Action = ApprovalAction.Deny,
                Comment = "重要操作は自動許可できません"
            });
        }

        // カスタムルールを評価
        foreach (var rule in _settings.CustomRules)
        {
            if (rule.Condition(request))
            {
                _logger.LogDebug("カスタムルールに一致: {RuleName}", rule.Name);
                return Task.FromResult(new ApprovalResult
                {
                    Approved = rule.Approve,
                    Action = rule.Action,
                    Comment = $"ルール '{rule.Name}' により{(rule.Approve ? "許可" : "拒否")}"
                });
            }
        }

        // すべてのチェックを通過したら許可
        _logger.LogInformation("自動承認: {RequestId}", request.RequestId);
        return Task.FromResult(new ApprovalResult
        {
            Approved = true,
            Action = ApprovalAction.Approve,
            Comment = "自動承認"
        });
    }
}

/// <summary>
/// 自動承認の設定
/// </summary>
public class SilentApprovalSettings
{
    /// <summary>
    /// デフォルトアクション
    /// </summary>
    public ApprovalAction DefaultAction { get; set; } = ApprovalAction.Approve;

    /// <summary>
    /// 許可する最大危険度
    /// </summary>
    public DangerLevel MaxAllowedDangerLevel { get; set; } = DangerLevel.Medium;

    /// <summary>
    /// 重要操作を許可するか
    /// </summary>
    public bool AllowCriticalOperations { get; set; } = false;

    /// <summary>
    /// 許可する操作タイプ（空の場合はすべて許可）
    /// </summary>
    public HashSet<OperationType> AllowedOperations { get; set; } = new();

    /// <summary>
    /// 拒否する操作タイプ
    /// </summary>
    public HashSet<OperationType> DeniedOperations { get; set; } = new();

    /// <summary>
    /// カスタムルール
    /// </summary>
    public List<ApprovalRule> CustomRules { get; set; } = new();
}

/// <summary>
/// 承認ルール
/// </summary>
public class ApprovalRule
{
    public string Name { get; set; } = string.Empty;
    public Func<ApprovalRequest, bool> Condition { get; set; } = _ => false;
    public bool Approve { get; set; }
    public ApprovalAction Action { get; set; }
}
