using System.Diagnostics;
using System.Text;
using Clawleash.Configuration;
using Clawleash.Models;

namespace Clawleash.Sandbox;

/// <summary>
/// Dockerコンテナを使用したサンドボックスプロバイダー
/// クロスプラットフォームで動作し、コンテナによる強力な分離を提供
/// </summary>
public class DockerSandboxProvider : ISandboxProvider
{
    private readonly ClawleashSettings _settings;
    private string? _containerId;
    private readonly List<string> _allowedDirectories = new();
    private bool _disposed;

    public SandboxType SandboxType => SandboxType.Docker;
    public bool IsInitialized { get; private set; }

    public DockerSandboxProvider(ClawleashSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task InitializeAsync(IEnumerable<string> allowedDirectories, CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            return;
        }

        _allowedDirectories.Clear();
        foreach (var dir in allowedDirectories)
        {
            var fullPath = Path.GetFullPath(dir);
            if (Directory.Exists(fullPath) || !Path.IsPathRooted(dir))
            {
                _allowedDirectories.Add(fullPath);
            }
        }

        // Dockerコンテナを作成
        _containerId = await CreateContainerAsync(cancellationToken);
        if (string.IsNullOrEmpty(_containerId))
        {
            throw new InvalidOperationException("Dockerコンテナの作成に失敗しました");
        }

        // コンテナを起動
        await StartContainerAsync(_containerId, cancellationToken);

        IsInitialized = true;
    }

    public async Task<CommandResult> ExecuteAsync(
        string executable,
        string args,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInitialized || string.IsNullOrEmpty(_containerId))
        {
            throw new InvalidOperationException("サンドボックスが初期化されていません");
        }

        var command = string.IsNullOrEmpty(args) ? executable : $"{executable} {args}";
        var workDir = workingDirectory != null ? $"-w {ConvertToContainerPath(workingDirectory)}" : "";

        var dockerArgs = $"exec {workDir} {_containerId} {command}";
        return await ExecuteDockerCommandAsync(dockerArgs, cancellationToken);
    }

    public async Task<CommandResult> ExecuteShellAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInitialized || string.IsNullOrEmpty(_containerId))
        {
            throw new InvalidOperationException("サンドボックスが初期化されていません");
        }

        var workDir = workingDirectory != null ? $"-w {ConvertToContainerPath(workingDirectory)}" : "";

        var dockerArgs = $"exec {workDir} {_containerId} sh -c \"{EscapeForShell(command)}\"";
        return await ExecuteDockerCommandAsync(dockerArgs, cancellationToken);
    }

    private async Task<string?> CreateContainerAsync(CancellationToken cancellationToken)
    {
        var volumeMounts = new List<string>();

        // 許可されたディレクトリをボリュームマウントとして追加
        foreach (var dir in _allowedDirectories)
        {
            var containerPath = ConvertToContainerPath(dir);
            volumeMounts.Add($"-v \"{dir}:{containerPath}\"");

            // ローカルディレクトリが存在しない場合は作成
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        var mountArgs = string.Join(" ", volumeMounts);
        var imageName = _settings.Sandbox.DockerImage;

        // セキュリティオプション: 不要なケーパビリティを削除
        var securityArgs = "--cap-drop=ALL --security-opt=no-new-privileges";

        // ネットワーク制限（必要に応じて）
        // var networkArgs = "--network=none"; // 完全にネットワークを無効化

        var createArgs = $"create --name clawleash-sandbox-{Guid.NewGuid():N} {securityArgs} {mountArgs} {imageName} tail -f /dev/null";

        var result = await ExecuteDockerCommandAsync(createArgs, cancellationToken);
        return result.Success ? result.StandardOutput.Trim() : null;
    }

    private async Task StartContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        var startArgs = $"start {containerId}";
        var result = await ExecuteDockerCommandAsync(startArgs, cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException($"コンテナの起動に失敗しました: {result.StandardError}");
        }
    }

    private async Task<CommandResult> ExecuteDockerCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
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

        // タイムアウト付きで待機
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

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

    private string ConvertToContainerPath(string hostPath)
    {
        // ホストパスをコンテナ内パスに変換
        var dirName = Path.GetFileName(hostPath);
        return $"/workspace/{dirName}";
    }

    private static string EscapeForShell(string command)
    {
        // シェルコマンド用のエスケープ
        return command.Replace("\"", "\\\"");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        // コンテナを停止・削除
        if (!string.IsNullOrEmpty(_containerId))
        {
            try
            {
                await ExecuteDockerCommandAsync($"stop {_containerId}", CancellationToken.None);
                await ExecuteDockerCommandAsync($"rm {_containerId}", CancellationToken.None);
            }
            catch
            {
                // クリーンアップエラーは無視
            }
        }

        _disposed = true;
    }
}
