using System.ComponentModel;
using Clawleash.Services;
using Microsoft.SemanticKernel;

namespace Clawleash.Plugins;

/// <summary>
/// 制限付きPowerShell実行プラグイン
/// コマンドフィルタリングを適用して安全にPowerShellコマンドを実行
/// </summary>
public class RestrictedPowerShellPlugin
{
    private readonly IPowerShellExecutor _executor;

    public RestrictedPowerShellPlugin(IPowerShellExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    [KernelFunction, Description("PowerShellコマンドを実行します。許可されたコマンドのみ実行可能です")]
    public async Task<string> ExecuteCommand(
        [Description("実行するPowerShellコマンド")] string command,
        [Description("作業ディレクトリ（オプション）")] string? workingDirectory = null)
    {
        try
        {
            if (_executor.IsExecuting)
            {
                return "エラー: 現在別のコマンドを実行中です。しばらくお待ちください";
            }

            var result = await _executor.ExecuteAsync(command, workingDirectory);

            if (result.Success)
            {
                return string.IsNullOrEmpty(result.StandardOutput)
                    ? "コマンドは正常に完了しました（出力なし）"
                    : result.StandardOutput;
            }

            return $"エラー (Exit code: {result.ExitCode}): {result.StandardError}";
        }
        catch (OperationCanceledException)
        {
            return "エラー: コマンドがタイムアウトしました";
        }
        catch (Exception ex)
        {
            return $"エラー: {ex.Message}";
        }
    }

    [KernelFunction, Description("現在のディレクトリを取得します")]
    public async Task<string> GetCurrentDirectory()
    {
        var result = await _executor.ExecuteAsync("Get-Location");
        return result.Success ? result.StandardOutput.Trim() : $"エラー: {result.StandardError}";
    }

    [KernelFunction, Description("現在の日時を取得します")]
    public async Task<string> GetCurrentDateTime()
    {
        var result = await _executor.ExecuteAsync("Get-Date -Format 'yyyy-MM-dd HH:mm:ss'");
        return result.Success ? result.StandardOutput.Trim() : $"エラー: {result.StandardError}";
    }

    [KernelFunction, Description("環境変数を取得します")]
    public async Task<string> GetEnvironmentVariable(
        [Description("環境変数名")] string name)
    {
        // セキュリティチェック: 機密情報を含む可能性のある変数を制限
        var restrictedVars = new[] { "APIKEY", "SECRET", "PASSWORD", "TOKEN", "CREDENTIAL" };

        if (restrictedVars.Any(rv => name.ToUpperInvariant().Contains(rv)))
        {
            return $"エラー: '{name}' は機密情報の可能性があるため取得できません";
        }

        var result = await _executor.ExecuteAsync($"[Environment]::GetEnvironmentVariable('{name}')");
        return result.Success ? result.StandardOutput.Trim() : $"エラー: {result.StandardError}";
    }

    [KernelFunction, Description("ディレクトリを作成します")]
    public async Task<string> CreateDirectory(
        [Description("作成するディレクトリのパス")] string path)
    {
        var result = await _executor.ExecuteAsync($"New-Item -ItemType Directory -Path '{path}' -Force");
        return result.Success
            ? $"成功: ディレクトリ '{path}' を作成しました"
            : $"エラー: {result.StandardError}";
    }

    [KernelFunction, Description("ファイルが存在するか確認します")]
    public async Task<string> TestPath(
        [Description("確認するパス")] string path)
    {
        var result = await _executor.ExecuteAsync($"Test-Path '{path}'");
        var exists = result.Success && result.StandardOutput.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        return $"パス '{path}' は{(exists ? "存在します" : "存在しません")}";
    }

    [KernelFunction, Description("ファイルの内容を検索します")]
    public async Task<string> SearchInFile(
        [Description("検索するファイルのパス")] string filePath,
        [Description("検索パターン")] string pattern,
        [Description("コンテキスト行数")] int? contextLines = null)
    {
        var command = contextLines.HasValue
            ? $"Select-String -Path '{filePath}' -Pattern '{pattern}' -Context {contextLines},{contextLines}"
            : $"Select-String -Path '{filePath}' -Pattern '{pattern}'";

        var result = await _executor.ExecuteAsync(command);
        return result.Success ? result.StandardOutput : $"エラー: {result.StandardError}";
    }
}
