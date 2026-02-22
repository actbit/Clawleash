using Clawleash.Configuration;
using Clawleash.Models;

namespace Clawleash.Sandbox;

/// <summary>
/// サンドボックスプロバイダーの抽象インターフェース
/// 異なるサンドボックス実装（AppContainer、Docker、bubblewrap）を統一的に扱う
/// </summary>
public interface ISandboxProvider : IAsyncDisposable
{
    /// <summary>
    /// サンドボックスを初期化する
    /// 許可されたディレクトリへのアクセス権限設定などを含む
    /// </summary>
    /// <param name="allowedDirectories">アクセスを許可するディレクトリのパス一覧</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task InitializeAsync(IEnumerable<string> allowedDirectories, CancellationToken cancellationToken = default);

    /// <summary>
    /// サンドボックス内でコマンドを実行する
    /// </summary>
    /// <param name="executable">実行可能ファイルのパス</param>
    /// <param name="args">コマンドライン引数</param>
    /// <param name="workingDirectory">作業ディレクトリ（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>コマンド実行結果</returns>
    Task<CommandResult> ExecuteAsync(
        string executable,
        string args,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// サンドボックス内でシェルコマンドを実行する
    /// </summary>
    /// <param name="command">シェルコマンド</param>
    /// <param name="workingDirectory">作業ディレクトリ（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>コマンド実行結果</returns>
    Task<CommandResult> ExecuteShellAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// サンドボックスの種類を取得する
    /// </summary>
    SandboxType SandboxType { get; }

    /// <summary>
    /// サンドボックスが初期化済みかどうか
    /// </summary>
    bool IsInitialized { get; }
}
