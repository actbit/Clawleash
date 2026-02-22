using Clowleash.Configuration;

namespace Clowleash.Sandbox;

/// <summary>
/// 設定に基づいて適切なサンドボックスプロバイダーを生成するファクトリー
/// </summary>
public static class SandboxFactory
{
    /// <summary>
    /// 設定に基づいてサンドボックスプロバイダーを作成する
    /// </summary>
    /// <param name="settings">アプリケーション設定</param>
    /// <returns>適切なサンドボックスプロバイダーのインスタンス</returns>
    /// <exception cref="PlatformNotSupportedException">現在のプラットフォームでサポートされていないサンドボックスタイプ</exception>
    public static ISandboxProvider Create(ClowleashSettings settings)
    {
        return settings.Sandbox.Type switch
        {
            SandboxType.AppContainer => new AppContainerProvider(settings),
            SandboxType.Bubblewrap => new BubblewrapProvider(settings),
            SandboxType.Docker => new DockerSandboxProvider(settings),
            _ => throw new NotSupportedException($"Unsupported sandbox type: {settings.Sandbox.Type}")
        };
    }

    /// <summary>
    /// 現在のプラットフォームで推奨されるサンドボックスタイプを取得する
    /// </summary>
    public static SandboxType GetRecommendedSandboxType()
    {
        if (OperatingSystem.IsWindows())
        {
            return SandboxType.AppContainer;
        }

        if (OperatingSystem.IsLinux())
        {
            // bubblewrapが利用可能かチェック
            if (IsBubblewrapAvailable())
            {
                return SandboxType.Bubblewrap;
            }
        }

        // フォールバックはDocker
        return SandboxType.Docker;
    }

    /// <summary>
    /// bubblewrapが利用可能かチェックする
    /// </summary>
    private static bool IsBubblewrapAvailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            var which = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "bwrap",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            which.Start();
            which.WaitForExit(1000);
            return which.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Dockerが利用可能かチェックする
    /// </summary>
    public static bool IsDockerAvailable()
    {
        try
        {
            var docker = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "docker" : "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            docker.Start();
            docker.WaitForExit(5000);
            return docker.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
