using Clawleash.Configuration;
using Clawleash.Models;
using Clawleash.Sandbox;
using Clawleash.Security;
using System.Diagnostics;

namespace Clawleash.Services;

/// <summary>
/// PowerShell 7を実行するサービス
/// サンドボックス内で別プロセスとして実行し、コマンドフィルタリングを適用
/// カレントディレクトリの状態を管理
/// </summary>
public class PowerShellExecutor : IPowerShellExecutor, IAsyncDisposable
{
    private readonly ClawleashSettings _settings;
    private readonly ISandboxProvider _sandboxProvider;
    private readonly CommandValidator _commandValidator;
    private readonly PathValidator _pathValidator;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// 現在実行中かどうか
    /// </summary>
    public bool IsExecuting { get; private set; }

    /// <summary>
    /// 現在のカレントディレクトリ
    /// </summary>
    public string CurrentDirectory { get; private set; }

    /// <summary>
    /// セッションが初期化されているかどうか
    /// </summary>
    public bool IsSessionInitialized { get; private set; }

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

        // 初期ディレクトリを設定
        CurrentDirectory = Environment.CurrentDirectory;
    }

    /// <summary>
    /// セッションを初期化し、開始ディレクトリを設定します
    /// </summary>
    /// <param name="initialDirectory">初期ディレクトリ（省略時は現在の環境のディレクトリ）</param>
    public async Task<bool> InitializeSessionAsync(string? initialDirectory = null)
    {
        if (!string.IsNullOrEmpty(initialDirectory))
        {
            if (!_pathValidator.IsPathAllowed(initialDirectory))
            {
                return false;
            }

            if (!Directory.Exists(initialDirectory))
            {
                return false;
            }

            CurrentDirectory = initialDirectory;
        }

        IsSessionInitialized = true;

        // 現在のディレクトリを確認
        var result = await ExecuteAsync("Get-Location");
        if (result.Success)
        {
            CurrentDirectory = result.StandardOutput.Trim();
        }

        return true;
    }

    /// <summary>
    /// カレントディレクトリを変更します
    /// </summary>
    /// <param name="directory">移動先のディレクトリ</param>
    public async Task<bool> ChangeDirectoryAsync(string directory)
    {
        if (!_pathValidator.IsPathAllowed(directory))
        {
            return false;
        }

        // ディレクトリの存在確認
        if (!Directory.Exists(directory))
        {
            return false;
        }

        // 絶対パスに変換
        if (!Path.IsPathRooted(directory))
        {
            directory = Path.GetFullPath(Path.Combine(CurrentDirectory, directory));
        }

        CurrentDirectory = directory;
        return true;
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

            // 作業ディレクトリを決定（指定がなければ現在のカレントディレクトリを使用）
            var actualWorkingDir = workingDirectory ?? CurrentDirectory;

            // 作業ディレクトリを検証
            if (!string.IsNullOrEmpty(actualWorkingDir) && !_pathValidator.IsPathAllowed(actualWorkingDir))
            {
                return new CommandResult(-1, string.Empty,
                    $"作業ディレクトリが許可されていません: {actualWorkingDir}");
            }

            // サンドボックス内でPowerShellを実行
            var psPath = _settings.PowerShell.PowerShellPath;

            // Set-Location を含めて実行し、カレントディレクトリを維持
            var fullCommand = BuildCommandWithLocation(command, actualWorkingDir);
            var args = BuildPowerShellArgs(fullCommand);

            var result = await _sandboxProvider.ExecuteAsync(psPath, args, actualWorkingDir, cancellationToken);

            // コマンドにSet-Locationが含まれている場合、カレントディレクトリを更新
            UpdateCurrentDirectoryIfNeeded(command, actualWorkingDir);

            return result;
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

        // 作業ディレクトリを決定
        var actualWorkingDir = workingDirectory ?? CurrentDirectory;

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
            return await _sandboxProvider.ExecuteAsync(psPath, args, actualWorkingDir, cancellationToken);
        }
        finally
        {
            IsExecuting = false;
            _executionLock.Release();
        }
    }

    /// <summary>
    /// 現在のセッション情報を取得します
    /// </summary>
    public string GetSessionInfo()
    {
        return $"""
            ## PowerShell セッション情報
            - 初期化済み: {(IsSessionInitialized ? "はい" : "いいえ")}
            - カレントディレクトリ: {CurrentDirectory}
            - 実行中: {(IsExecuting ? "はい" : "いいえ")}
            """;
    }

    /// <summary>
    /// 現在のディレクトリの内容を一覧表示します
    /// </summary>
    public async Task<string> ListCurrentDirectoryAsync()
    {
        var result = await ExecuteAsync("Get-ChildItem | Format-Table Name, Mode, Length -AutoSize");
        return result.Success ? result.StandardOutput : $"エラー: {result.StandardError}";
    }

    private string BuildCommandWithLocation(string command, string workingDirectory)
    {
        // カレントディレクトリを設定してからコマンドを実行
        return $"Set-Location '{workingDirectory.Replace("'", "''")}'; {command}";
    }

    private void UpdateCurrentDirectoryIfNeeded(string command, string currentWorkingDir)
    {
        // Set-Location または cd コマンドが含まれているか確認
        var setLocationPatterns = new[]
        {
            @"Set-Location\s+",
            @"cd\s+",
            @"sl\s+",
            @"chdir\s+"
        };

        foreach (var pattern in setLocationPatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(command, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                // パスを抽出しようとする（簡易的な実装）
                var match = System.Text.RegularExpressions.Regex.Match(command, $@"(?:Set-Location|cd|sl|chdir)\s+['""]?([^'""\s]+)['""]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var newPath = match.Groups[1].Value;
                    if (!Path.IsPathRooted(newPath))
                    {
                        newPath = Path.GetFullPath(Path.Combine(currentWorkingDir, newPath));
                    }

                    if (Directory.Exists(newPath) && _pathValidator.IsPathAllowed(newPath))
                    {
                        CurrentDirectory = newPath;
                    }
                }
                break;
            }
        }
    }

    private string BuildPowerShellArgs(string command)
    {
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"";
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
