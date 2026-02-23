using Clawleash.Shell.IPC;
using Microsoft.Extensions.Logging;

namespace Clawleash.Shell;

/// <summary>
/// Clawleash.Shell - 制約付きPowerShell実行プロセス
/// ZeroMQ + MessagePack によるIPCでメインアプリと通信
/// Main アプリに接続するクライアントとして動作
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // ログ設定
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<Program>();

        // コマンドライン引数を解析
        var serverAddress = GetArgument(args, "--server") ?? GetArgument(args, "-s");
        var verbose = HasArgument(args, "--verbose") || HasArgument(args, "-v");

        if (string.IsNullOrEmpty(serverAddress))
        {
            logger.LogError("サーバーアドレスが指定されていません。--server <address> を指定してください");
            return 1;
        }

        if (verbose)
        {
            logger.LogInformation("Clawleash.Shell 起動中...");
            logger.LogInformation("Runtime: {Runtime}", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            logger.LogInformation("OS: {OS}", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            logger.LogInformation("Server: {Server}", serverAddress);
        }

        // IPCクライアントを作成
        using var client = new IpcClient(loggerFactory);

        try
        {
            // サーバーに接続
            var connected = await client.ConnectAsync(serverAddress);
            if (!connected)
            {
                logger.LogError("Main アプリへの接続に失敗しました");
                return 1;
            }

            logger.LogInformation("Clawleash.Shell 準備完了");

            // 終了シグナルを待機
            var tcs = new TaskCompletionSource<bool>();

            // Ctrl+C で終了
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                logger.LogInformation("終了シグナルを受信");
                tcs.TrySetResult(true);
            };

            // AppDomain終了イベント
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                logger.LogInformation("プロセス終了");
                tcs.TrySetResult(true);
            };

            await tcs.Task;

            logger.LogInformation("Clawleash.Shell 終了中...");
            await client.DisconnectAsync();

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "致命的エラー");
            return 1;
        }
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static bool HasArgument(string[] args, string name)
    {
        return args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
