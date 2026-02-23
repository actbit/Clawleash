using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.Extensions.Logging;
using Clawleash.Contracts;

namespace Clawleash.Shell.Hosting;

/// <summary>
/// 制約付きPowerShell Runspaceホスト
/// セキュリティ制約を適用したPowerShell実行環境
/// </summary>
public class ConstrainedRunspaceHost : IDisposable
{
    private readonly ILogger<ConstrainedRunspaceHost> _logger;
    private Runspace? _runspace;
    private PowerShell? _powerShell;
    private bool _initialized;
    private bool _disposed;

    private readonly HashSet<string> _allowedCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _readOnlyPaths = new(StringComparer.OrdinalIgnoreCase);
    private PSLanguageMode _languageMode = PSLanguageMode.ConstrainedLanguage;

    public bool IsInitialized => _initialized;

    public ConstrainedRunspaceHost(ILogger<ConstrainedRunspaceHost> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 制約付きRunspaceを初期化
    /// </summary>
    public async Task<ShellInitializeResponse> InitializeAsync(ShellInitializeRequest request)
    {
        try
        {
            _logger.LogInformation("Runspace初期化開始: LanguageMode={LanguageMode}", request.LanguageMode);

            // 設定を保存
            _languageMode = ToPSLanguageMode(request.LanguageMode);
            foreach (var cmd in request.AllowedCommands)
            {
                _allowedCommands.Add(cmd);
            }
            foreach (var path in request.AllowedPaths)
            {
                _allowedPaths.Add(path);
            }
            foreach (var path in request.ReadOnlyPaths)
            {
                _readOnlyPaths.Add(path);
            }

            // InitialSessionStateを作成
            var sessionState = CreateConstrainedSessionState(request);

            // Runspaceを作成
            _runspace = RunspaceFactory.CreateRunspace(sessionState);
            _runspace.Open();

            // PowerShell インスタンスを作成
            _powerShell = PowerShell.Create();
            _powerShell.Runspace = _runspace;

            _initialized = true;
            _logger.LogInformation("Runspace初期化完了");

            return new ShellInitializeResponse
            {
                Success = true,
                Version = typeof(ConstrainedRunspaceHost).Assembly.GetName().Version?.ToString() ?? "1.0.0"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Runspace初期化エラー");
            return new ShellInitializeResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static PSLanguageMode ToPSLanguageMode(ShellLanguageMode mode)
    {
        return mode switch
        {
            ShellLanguageMode.FullLanguage => PSLanguageMode.FullLanguage,
            ShellLanguageMode.ConstrainedLanguage => PSLanguageMode.ConstrainedLanguage,
            ShellLanguageMode.RestrictedLanguage => PSLanguageMode.RestrictedLanguage,
            ShellLanguageMode.NoLanguage => PSLanguageMode.NoLanguage,
            _ => PSLanguageMode.ConstrainedLanguage
        };
    }

    /// <summary>
    /// 制約付きInitialSessionStateを作成
    /// </summary>
    private InitialSessionState CreateConstrainedSessionState(ShellInitializeRequest request)
    {
        InitialSessionState sessionState;

        if (request.AllowedCommands.Length > 0)
        {
            // 空のセッション状態から開始（ホワイトリストモード）
            sessionState = InitialSessionState.Create();

            // 許可されたコマンドのみ追加
            foreach (var cmdName in request.AllowedCommands)
            {
                AddCommandToSessionState(sessionState, cmdName);
            }
        }
        else
        {
            // デフォルトセッション状態から開始
            sessionState = InitialSessionState.CreateDefault();

            // 危険なコマンドを削除
            RemoveDangerousCommands(sessionState);
        }

        // 言語モードを設定
        sessionState.LanguageMode = _languageMode;

        // カスタムCmdletを登録
        RegisterCustomCmdlets(sessionState);

        return sessionState;
    }

    /// <summary>
    /// コマンドをセッション状態に追加
    /// </summary>
    private void AddCommandToSessionState(InitialSessionState sessionState, string commandName)
    {
        try
        {
            // 一般的なコマンドのマッピング
            var cmdletType = GetCmdletType(commandName);
            if (cmdletType != null)
            {
                sessionState.Commands.Add(new SessionStateCmdletEntry(commandName, cmdletType, null));
                _logger.LogDebug("コマンドを追加: {Command}", commandName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "コマンド追加エラー: {Command}", commandName);
        }
    }

    /// <summary>
    /// コマンド名からCmdletタイプを取得
    /// </summary>
    private static Type? GetCmdletType(string commandName)
    {
        // 基本的なコマンドのマッピング
        var commandMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "Get-Content", typeof(Microsoft.PowerShell.Commands.GetContentCommand) },
            { "Set-Content", typeof(Microsoft.PowerShell.Commands.SetContentCommand) },
            { "Get-ChildItem", typeof(Microsoft.PowerShell.Commands.GetChildItemCommand) },
            { "Get-Location", typeof(Microsoft.PowerShell.Commands.GetLocationCommand) },
            { "Set-Location", typeof(Microsoft.PowerShell.Commands.SetLocationCommand) },
            { "New-Item", typeof(Microsoft.PowerShell.Commands.NewItemCommand) },
            { "Remove-Item", typeof(Microsoft.PowerShell.Commands.RemoveItemCommand) },
            { "Copy-Item", typeof(Microsoft.PowerShell.Commands.CopyItemCommand) },
            { "Move-Item", typeof(Microsoft.PowerShell.Commands.MoveItemCommand) },
            { "Rename-Item", typeof(Microsoft.PowerShell.Commands.RenameItemCommand) },
            { "Test-Path", typeof(Microsoft.PowerShell.Commands.TestPathCommand) },
            { "Write-Host", typeof(Microsoft.PowerShell.Commands.WriteHostCommand) },
            { "Write-Output", typeof(Microsoft.PowerShell.Commands.WriteOutputCommand) },
            { "Get-Date", typeof(Microsoft.PowerShell.Commands.GetDateCommand) },
            { "Get-Random", typeof(Microsoft.PowerShell.Commands.GetRandomCommand) },
            { "Measure-Object", typeof(Microsoft.PowerShell.Commands.MeasureObjectCommand) },
            { "Select-Object", typeof(Microsoft.PowerShell.Commands.SelectObjectCommand) },
            { "Where-Object", typeof(Microsoft.PowerShell.Commands.WhereObjectCommand) },
            { "ForEach-Object", typeof(Microsoft.PowerShell.Commands.ForEachObjectCommand) },
            { "Sort-Object", typeof(Microsoft.PowerShell.Commands.SortObjectCommand) },
            { "Format-Table", typeof(Microsoft.PowerShell.Commands.FormatTableCommand) },
            { "Format-List", typeof(Microsoft.PowerShell.Commands.FormatListCommand) },
            { "Out-String", typeof(Microsoft.PowerShell.Commands.OutStringCommand) },
            { "ConvertTo-Json", typeof(Microsoft.PowerShell.Commands.ConvertToJsonCommand) },
            { "ConvertFrom-Json", typeof(Microsoft.PowerShell.Commands.ConvertFromJsonCommand) },
            { "Invoke-WebRequest", typeof(Microsoft.PowerShell.Commands.InvokeWebRequestCommand) },
            { "Invoke-RestMethod", typeof(Microsoft.PowerShell.Commands.InvokeRestMethodCommand) },
        };

        return commandMap.TryGetValue(commandName, out var type) ? type : null;
    }

    /// <summary>
    /// 危険なコマンドを削除
    /// </summary>
    private void RemoveDangerousCommands(InitialSessionState sessionState)
    {
        var dangerousCommands = new[]
        {
            "Invoke-Expression",
            "Invoke-Command",
            "Start-Process",
            "New-Service",
            "Remove-Service",
            "Set-Service",
            "Get-Credential",
            "Export-Certificate",
            "Import-Certificate",
            "Set-ExecutionPolicy",
            "New-SelfSignedCertificate",
            "Invoke-WmiMethod",
            "Get-WmiObject",
            "Set-WmiInstance",
            "Remove-WmiObject",
            "Add-Type",
            "New-Object" // 制限付きで許可する場合はProxy経由に
        };

        foreach (var cmd in dangerousCommands)
        {
            var index = FindCommandIndex(sessionState.Commands, cmd);
            if (index >= 0)
            {
                sessionState.Commands.RemoveItem(index);
                _logger.LogDebug("危険なコマンドを削除: {Command}", cmd);
            }
        }
    }

    /// <summary>
    /// コマンドのインデックスを検索
    /// </summary>
    private static int FindCommandIndex(InitialSessionStateEntryCollection<SessionStateCommandEntry> commands, string name)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            if (commands[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// カスタムCmdletを登録
    /// </summary>
    private void RegisterCustomCmdlets(InitialSessionState sessionState)
    {
        // Clawleash用のカスタムコマンドを登録
        // TODO: カスタムCmdletを実装したら追加
        _logger.LogDebug("カスタムCmdlet登録完了");
    }

    /// <summary>
    /// コマンドを実行
    /// </summary>
    public async Task<ShellExecuteResponse> ExecuteAsync(ShellExecuteRequest request)
    {
        if (!_initialized || _powerShell == null)
        {
            return new ShellExecuteResponse
            {
                RequestId = request.MessageId,
                Success = false,
                Error = "Runspaceが初期化されていません"
            };
        }

        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogDebug("コマンド実行: {Command}", request.Command);

            // パス検証（パラメータに含まれるパスをチェック）
            if (!ValidatePaths(request.Parameters))
            {
                return new ShellExecuteResponse
                {
                    RequestId = request.MessageId,
                    Success = false,
                    Error = "許可されていないパスが含まれています"
                };
            }

            // PowerShellをリセット
            _powerShell.Commands.Clear();
            _powerShell.Streams.ClearStreams();

            // 作業ディレクトリを設定
            if (!string.IsNullOrEmpty(request.WorkingDirectory))
            {
                if (!_allowedPaths.Contains(request.WorkingDirectory))
                {
                    return new ShellExecuteResponse
                    {
                        RequestId = request.MessageId,
                        Success = false,
                        Error = $"作業ディレクトリが許可されていません: {request.WorkingDirectory}"
                    };
                }

                _powerShell.AddScript($"Set-Location '{request.WorkingDirectory.Replace("'", "''")}'; ");
            }

            // コマンドを追加
            _powerShell.AddScript(request.Command);

            // タイムアウト付きで実行
            using var cts = new CancellationTokenSource(request.TimeoutMs);

            var results = await Task.Run(() => _powerShell.Invoke(), cts.Token);

            // 出力を収集
            var output = new System.Text.StringBuilder();
            foreach (var result in results)
            {
                output.AppendLine(result?.ToString());
            }

            // エラーを確認
            var hasErrors = _powerShell.Streams.Error.Count > 0;
            var errorOutput = new System.Text.StringBuilder();
            foreach (var error in _powerShell.Streams.Error)
            {
                errorOutput.AppendLine(error.ToString());
            }

            var elapsed = DateTime.UtcNow - startTime;
            _logger.LogDebug("コマンド完了: {Success}, 所要時間: {Elapsed}ms", !hasErrors, elapsed.TotalMilliseconds);

            return new ShellExecuteResponse
            {
                RequestId = request.MessageId,
                Success = !hasErrors && _powerShell.HadErrors == false,
                Output = output.ToString(),
                Error = hasErrors ? errorOutput.ToString() : null,
                ExitCode = _powerShell.HadErrors ? 1 : 0,
                Metadata = new Dictionary<string, object?>
                {
                    ["DurationMs"] = elapsed.TotalMilliseconds,
                    ["ResultCount"] = results.Count
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("コマンドタイムアウト: {Command}", request.Command);
            return new ShellExecuteResponse
            {
                RequestId = request.MessageId,
                Success = false,
                Error = $"コマンドがタイムアウトしました ({request.TimeoutMs}ms)",
                ExitCode = -1
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "コマンド実行エラー: {Command}", request.Command);
            return new ShellExecuteResponse
            {
                RequestId = request.MessageId,
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// パラメータ内のパスを検証
    /// </summary>
    private bool ValidatePaths(Dictionary<string, object?> parameters)
    {
        foreach (var kvp in parameters)
        {
            if (kvp.Value is string path && LooksLikePath(path))
            {
                if (!_allowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("許可されていないパス: {Path}", path);
                    return false;
                }
            }
        }
        return true;
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(Path.DirectorySeparatorChar) ||
               value.Contains(Path.AltDirectorySeparatorChar) ||
               (value.Length >= 2 && value[1] == ':');
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _powerShell?.Dispose();
        _runspace?.Dispose();
        _disposed = true;
    }
}
