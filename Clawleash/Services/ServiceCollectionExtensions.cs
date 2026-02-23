using Clawleash.Services.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawleash.Services;

/// <summary>
/// 承認・入力ハンドラーのDI登録用拡張メソッド
/// </summary>
public static class ApprovalServiceExtensions
{
    #region 承認ハンドラー

    /// <summary>
    /// CLI承認ハンドラーを登録
    /// </summary>
    public static IServiceCollection AddCliApprovalHandler(
        this IServiceCollection services,
        ApprovalDisplaySettings? settings = null)
    {
        services.AddSingleton<ICliApprovalHandler>(sp =>
            new CliApprovalHandler(
                sp.GetRequiredService<ILogger<CliApprovalHandler>>(),
                settings));

        services.AddSingleton<IApprovalHandler>(sp => sp.GetRequiredService<ICliApprovalHandler>());

        return services;
    }

    /// <summary>
    /// 自動承認ハンドラーを登録（ユーザー介入なし）
    /// </summary>
    public static IServiceCollection AddSilentApprovalHandler(
        this IServiceCollection services,
        SilentApprovalSettings? settings = null)
    {
        services.AddSingleton<IApprovalHandler>(sp =>
            new SilentApprovalHandler(
                sp.GetRequiredService<ILogger<SilentApprovalHandler>>(),
                settings));

        return services;
    }

    /// <summary>
    /// 承認マネージャーを登録（複数ハンドラー管理）
    /// </summary>
    public static IServiceCollection AddApprovalManager(
        this IServiceCollection services)
    {
        services.AddSingleton<ApprovalManager>();
        services.AddSingleton<IApprovalHandler>(sp => sp.GetRequiredService<ApprovalManager>());

        return services;
    }

    /// <summary>
    /// デフォルトの承認設定を追加（CLI + フォールバックとしてSilent）
    /// </summary>
    public static IServiceCollection AddDefaultApprovalSystem(
        this IServiceCollection services,
        SilentApprovalSettings? silentSettings = null,
        ApprovalDisplaySettings? cliSettings = null)
    {
        // SilentHandlerをフォールバックとして登録
        services.AddSilentApprovalHandler(silentSettings);

        // CLIハンドラーを登録（プライマリ）
        services.AddCliApprovalHandler(cliSettings);

        // マネージャーを登録
        services.AddApprovalManager();

        return services;
    }

    /// <summary>
    /// 承認マネージャーを初期化し、ハンドラーを登録
    /// </summary>
    public static ApprovalManager ConfigureHandlers(
        this ApprovalManager manager,
        ICliApprovalHandler? cliHandler = null,
        SilentApprovalHandler? silentHandler = null,
        IEnumerable<IApprovalHandler>? customHandlers = null)
    {
        // CLIハンドラーをデフォルトとして登録
        if (cliHandler != null)
        {
            manager.RegisterHandler(cliHandler, "CLI", isDefault: true);
        }

        // SilentHandlerをフォールバックとして登録
        if (silentHandler != null)
        {
            manager.RegisterHandler(silentHandler, "Silent", isFallback: true);
        }

        // カスタムハンドラーを登録
        if (customHandlers != null)
        {
            foreach (var handler in customHandlers)
            {
                manager.RegisterHandler(handler, handler.GetType().Name);
            }
        }

        return manager;
    }

    #endregion

    #region 入力ハンドラー

    /// <summary>
    /// CLI入力ハンドラーを登録
    /// </summary>
    public static IServiceCollection AddCliInputHandler(
        this IServiceCollection services,
        CliInputSettings? settings = null)
    {
        services.AddSingleton<IInputHandler>(sp =>
            new CliInputHandler(
                sp.GetRequiredService<ILogger<CliInputHandler>>(),
                settings));

        return services;
    }

    /// <summary>
    /// バッチ入力ハンドラーを登録（自動化用）
    /// </summary>
    public static IServiceCollection AddBatchInputHandler(
        this IServiceCollection services,
        BatchInputSettings? settings = null)
    {
        services.AddSingleton<BatchInputHandler>(sp =>
            new BatchInputHandler(
                sp.GetRequiredService<ILogger<BatchInputHandler>>(),
                settings));

        services.AddSingleton<IInputHandler>(sp => sp.GetRequiredService<BatchInputHandler>());

        return services;
    }

    /// <summary>
    /// 入力マネージャーを登録（複数ハンドラー管理）
    /// </summary>
    public static IServiceCollection AddInputManager(
        this IServiceCollection services)
    {
        services.AddSingleton<InputManager>();
        services.AddSingleton<IInputHandler>(sp => sp.GetRequiredService<InputManager>());

        return services;
    }

    /// <summary>
    /// デフォルトの入力設定を追加
    /// </summary>
    public static IServiceCollection AddDefaultInputSystem(
        this IServiceCollection services,
        CliInputSettings? cliSettings = null)
    {
        // CLI入力ハンドラーを登録
        services.AddCliInputHandler(cliSettings);

        // マネージャーを登録
        services.AddInputManager();

        return services;
    }

    /// <summary>
    /// 入力マネージャーを初期化し、ハンドラーを登録
    /// </summary>
    public static InputManager ConfigureInputHandlers(
        this InputManager manager,
        CliInputHandler? cliHandler = null,
        BatchInputHandler? batchHandler = null,
        IEnumerable<IInputHandler>? customHandlers = null)
    {
        // CLIハンドラーをデフォルトとして登録
        if (cliHandler != null)
        {
            manager.RegisterHandler(cliHandler, "CLI", isDefault: true);
        }

        // BatchHandlerをフォールバックとして登録
        if (batchHandler != null)
        {
            manager.RegisterHandler(batchHandler, "Batch", isFallback: true);
        }

        // カスタムハンドラーを登録
        if (customHandlers != null)
        {
            foreach (var handler in customHandlers)
            {
                manager.RegisterHandler(handler, handler.GetType().Name);
            }
        }

        return manager;
    }

    #endregion

    #region 統合登録

    /// <summary>
    /// デフォルトの対話システム全体を登録（承認 + 入力）
    /// </summary>
    public static IServiceCollection AddDefaultInteractionSystem(
        this IServiceCollection services,
        SilentApprovalSettings? silentSettings = null,
        ApprovalDisplaySettings? approvalDisplaySettings = null,
        CliInputSettings? inputSettings = null)
    {
        services.AddDefaultApprovalSystem(silentSettings, approvalDisplaySettings);
        services.AddDefaultInputSystem(inputSettings);

        return services;
    }

    #endregion
}
