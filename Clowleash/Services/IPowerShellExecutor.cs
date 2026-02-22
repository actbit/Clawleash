using Clowleash.Models;

namespace Clowleash.Services;

/// <summary>
/// PowerShellコマンド実行のインターフェース
/// </summary>
public interface IPowerShellExecutor
{
    /// <summary>
    /// PowerShellコマンドを実行する
    /// </summary>
    /// <param name="command">実行するコマンド</param>
    /// <param name="workingDirectory">作業ディレクトリ（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>コマンド実行結果</returns>
    Task<CommandResult> ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PowerShellスクリプトファイルを実行する
    /// </summary>
    /// <param name="scriptPath">スクリプトファイルのパス</param>
    /// <param name="arguments">スクリプトへの引数</param>
    /// <param name="workingDirectory">作業ディレクトリ（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>コマンド実行結果</returns>
    Task<CommandResult> ExecuteScriptAsync(
        string scriptPath,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在実行中のコマンドがあるかどうか
    /// </summary>
    bool IsExecuting { get; }
}
