using Clawleash.Models;

namespace Clawleash.Services;

/// <summary>
/// PowerShellコマンド実行のインターフェース
/// </summary>
public interface IPowerShellExecutor
{
    /// <summary>
    /// 現在実行中のコマンドがあるかどうか
    /// </summary>
    bool IsExecuting { get; }

    /// <summary>
    /// 現在のカレントディレクトリ
    /// </summary>
    string CurrentDirectory { get; }

    /// <summary>
    /// セッションが初期化されているかどうか
    /// </summary>
    bool IsSessionInitialized { get; }

    /// <summary>
    /// セッションを初期化し、開始ディレクトリを設定します
    /// </summary>
    /// <param name="initialDirectory">初期ディレクトリ（省略時は現在の環境のディレクトリ）</param>
    /// <returns>初期化に成功したかどうか</returns>
    Task<bool> InitializeSessionAsync(string? initialDirectory = null);

    /// <summary>
    /// カレントディレクトリを変更します
    /// </summary>
    /// <param name="directory">移動先のディレクトリ</param>
    /// <returns>変更に成功したかどうか</returns>
    Task<bool> ChangeDirectoryAsync(string directory);

    /// <summary>
    /// PowerShellコマンドを実行する
    /// </summary>
    /// <param name="command">実行するコマンド</param>
    /// <param name="workingDirectory">作業ディレクトリ（オプション、省略時はCurrentDirectoryを使用）</param>
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
    /// 現在のセッション情報を取得します
    /// </summary>
    string GetSessionInfo();

    /// <summary>
    /// 現在のディレクトリの内容を一覧表示します
    /// </summary>
    Task<string> ListCurrentDirectoryAsync();
}
