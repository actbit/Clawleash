namespace Clawleash.Services.Handlers;

/// <summary>
/// CLI承認ハンドラーを識別するマーカーインターフェース
/// </summary>
public interface ICliApprovalHandler : IApprovalHandler
{
    /// <summary>
    /// 入力ストリーム（テスト用に注入可能）
    /// </summary>
    TextReader? InputOverride { get; set; }

    /// <summary>
    /// 出力ストリーム（テスト用に注入可能）
    /// </summary>
    TextWriter? OutputOverride { get; set; }
}
