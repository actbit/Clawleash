using Clawleash.Configuration;
using Clawleash.Models;
using Clawleash.Sandbox;
using Clawleash.Security;
using System.Diagnostics;

namespace Clawleash.Services;

/// <summary>
/// PowerShell 7を実行するサービス
/// サンドボックス内で別プロセスとして実行し、コマンドフィルタリングを適用
/// </summary>
public class PowerShellExecutor : IPowerShellExecutor, IAsyncDisposable
{
    private readonly ClawleashSettings _settings;
    private readonly ISandboxProvider _sandboxProvider;
    private readonly CommandValidator _commandValidator;
    private readonly PathValidator _pathValidator;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private bool _disposed;

    public bool IsExecuting { get; private set; }

    public PowerShellExecutor(
        ClawleashSettings settings,
        ISandboxProvider sandboxProvider,
        CommandValidator commandValidator,
        PathValidator pathValidator)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sandboxProvider = sandboxProvider ?? throw new ArgumentNullException(nameof(sandboxProvider));
        _commandValidator = commandValidator ?? throw new ArgumentNullException(nameof(commandValidator));
        _pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    public async Task<CommandResult> ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        await _executionLock.WaitAsync(cancellationToken);
        try
        {
            IsExecuting = true;

            // コマンドを検証
            var validationResult = _commandValidator.Validate(command);
            if (!validationResult.IsAllowed)
            {
                return new CommandResult(-1, string.Empty,
                    $"コマンドが拒否されました: {validationResult.ErrorMessage}");
            }

            // 作業ディレクトリを検証
            if (!string.IsNullOrEmpty(workingDirectory) && !_pathValidator.IsPathAllowed(workingDirectory))
            {
                return new CommandResult(-1, string.Empty,
                    $"作業ディレクトリが許可されていません: {workingDirectory}");
            }

            // サンドボックス内でPowerShellを実行
            var psPath = _settings.PowerShell.PowerShellPath;
            var args = BuildPowerShellArgs(command);

            return await _sandboxProvider.ExecuteAsync(psPath, args, workingDirectory, cancellationToken);
        }
        finally
        {
            IsExecuting = false;
            _executionLock.Release();
        }
    }

    public async Task<CommandResult> ExecuteScriptAsync(
        string scriptPath,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        // スクリプトパスを検証
        if (!_pathValidator.IsPathAllowed(scriptPath))
        {
            return new CommandResult(-1, string.Empty,
                $"スクリプトパスが許可されていません: {scriptPath}");
        }

        // ファイルの存在確認
        if (!File.Exists(scriptPath))
        {
            return new CommandResult(-1, string.Empty,
                $"スクリプトファイルが見つかりません: {scriptPath}");
        }

        // サンドボックス内で実行
        var psPath = _settings.PowerShell.PowerShellPath;
        var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";

        if (!string.IsNullOrEmpty(arguments))
        {
            args += $" {arguments}";
        }

        await _executionLock.WaitAsync(cancellationToken);
        try
        {
            IsExecuting = true;
            return await _sandboxProvider.ExecuteAsync(psPath, args, workingDirectory, cancellationToken);
        }
        finally
        {
            IsExecuting = false;
            _executionLock.Release();
        }
    }

    private string BuildPowerShellArgs(string command)
    {
        var timeout = _settings.PowerShell.TimeoutSeconds;
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _executionLock.Dispose();
        _disposed = true;
        await ValueTask.CompletedTask;
    }
}
