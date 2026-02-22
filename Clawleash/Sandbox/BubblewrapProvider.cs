using System.Diagnostics;
using System.Text;
using Clawleash.Configuration;
using Clawleash.Models;

namespace Clawleash.Sandbox;

/// <summary>
/// bubblewrap (bwrap) を使用したLinux向けサンドボックスプロバイダー
/// namespaceベースの軽量なサンドボックスを提供
/// </summary>
public class BubblewrapProvider : ISandboxProvider
{
    private readonly ClawleashSettings _settings;
    private readonly List<string> _allowedDirectories = new();
    private bool _disposed;

    public SandboxType SandboxType => SandboxType.Bubblewrap;
    public bool IsInitialized { get; private set; }

    public BubblewrapProvider(ClawleashSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("bubblewrapはLinuxでのみサポートされています");
        }
    }

    public Task InitializeAsync(IEnumerable<string> allowedDirectories, CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            return Task.CompletedTask;
        }

        _allowedDirectories.Clear();
        foreach (var dir in allowedDirectories)
        {
            var fullPath = Path.GetFullPath(dir);
            _allowedDirectories.Add(fullPath);

            // ディレクトリが存在しない場合は作成
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        IsInitialized = true;
        return Task.CompletedTask;
    }

    public async Task<CommandResult> ExecuteAsync(
        string executable,
        string args,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("サンドボックスが初期化されていません");
        }

        var bwrapArgs = BuildBubblewrapArgs(executable, args, workingDirectory);
        return await ExecuteBubblewrapAsync(bwrapArgs, cancellationToken);
    }

    public async Task<CommandResult> ExecuteShellAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync("/bin/sh", $"-c \"{command}\"", workingDirectory, cancellationToken);
    }

    private List<string> BuildBubblewrapArgs(string executable, string args, string? workingDirectory)
    {
        var bwrapArgs = new List<string>();

        // システムディレクトリを読み取り専用でマウント
        bwrapArgs.AddRange(new[] { "--ro-bind", "/usr", "/usr" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/lib", "/lib" });
        bwrapArgs.AddRange(new[] { "--ro-bind", "/lib64", "/lib64" });

        // /binはbash/shなどの基本コマンドに必要
        if (Directory.Exists("/bin"))
        {
            bwrapArgs.AddRange(new[] { "--ro-bind", "/bin", "/bin" });
        }

        // 一時ファイルシステム
        bwrapArgs.AddRange(new[] { "--tmpfs", "/tmp" });
        bwrapArgs.AddRange(new[] { "--tmpfs", "/run" });
        bwrapArgs.AddRange(new[] { "--proc", "/proc" });
        bwrapArgs.AddRange(new[] { "--dev", "/dev" });

        // 全namespaceを分離
        bwrapArgs.Add("--unshare-all");

        // 親プロセスが終了したら終了
        bwrapArgs.Add("--die-with-parent");

        // 新しいセッションを作成
        bwrapArgs.Add("--new-session");

        // ネットワークアクセスを制限（必要に応じて）
        // bwrapArgs.Add("--unshare-net");

        // 許可されたディレクトリのみバインドマウント
        foreach (var dir in _allowedDirectories)
        {
            var dirName = Path.GetFileName(dir);
            var containerPath = $"/workspace/{dirName}";

            bwrapArgs.AddRange(new[] { "--bind", dir, containerPath });

            // 作業ディレクトリが許可ディレクトリ内にある場合はコンテナパスに変換
            if (workingDirectory != null && workingDirectory.StartsWith(dir))
            {
                workingDirectory = workingDirectory.Replace(dir, containerPath);
            }
        }

        // 作業ディレクトリを設定
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            bwrapArgs.AddRange(new[] { "--chdir", workingDirectory });
        }

        // 実行するコマンド
        bwrapArgs.Add(executable);
        if (!string.IsNullOrEmpty(args))
        {
            bwrapArgs.Add("--");
            // argsをスペースで分割して追加
            bwrapArgs.AddRange(ParseCommandLine(args));
        }

        return bwrapArgs;
    }

    private async Task<CommandResult> ExecuteBubblewrapAsync(
        List<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bwrap",
                Arguments = string.Join(" ", arguments.Select(EscapeArg)),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timeout = TimeSpan.FromSeconds(_settings.PowerShell.TimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // プロセス終了エラーは無視
            }

            return new CommandResult(-1, "", "操作がタイムアウトしました");
        }

        return new CommandResult(process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }

    private static string EscapeArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        if (!arg.Any(c => char.IsWhiteSpace(c) || c == '\"' || c == '\''))
        {
            return arg;
        }

        return $"\"{arg.Replace("\"", "\\\"")}\"";
    }

    private static List<string> ParseCommandLine(string commandLine)
    {
        var args = new List<string>();
        var currentArg = new StringBuilder();
        var inQuotes = false;

        foreach (var c in commandLine)
        {
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (currentArg.Length > 0)
                {
                    args.Add(currentArg.ToString());
                    currentArg.Clear();
                }
            }
            else
            {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0)
        {
            args.Add(currentArg.ToString());
        }

        return args;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _allowedDirectories.Clear();
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
