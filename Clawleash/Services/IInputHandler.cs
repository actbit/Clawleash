namespace Clawleash.Services;

/// <summary>
/// ユーザー指示入力ハンドラーのインターフェース
/// CLI、GUI、Web、APIなど異なるインターフェースに対応可能
/// </summary>
public interface IInputHandler
{
    /// <summary>
    /// このハンドラーが利用可能かどうか
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 入力を待機中かどうか
    /// </summary>
    bool IsWaitingForInput { get; }

    /// <summary>
    /// ユーザーから指示を取得する
    /// </summary>
    /// <param name="prompt">プロンプトメッセージ（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ユーザー入力結果</returns>
    Task<InputResult> GetInputAsync(string? prompt = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 選択肢付きでユーザーから指示を取得する
    /// </summary>
    /// <param name="options">選択肢</param>
    /// <param name="prompt">プロンプトメッセージ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ユーザー選択結果</returns>
    Task<SelectionResult> GetSelectionAsync(
        IReadOnlyList<SelectionOption> options,
        string? prompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数行入力を取得する
    /// </summary>
    /// <param name="prompt">プロンプトメッセージ</param>
    /// <param name="endMarker">終了マーカー（空行や特定文字列で終了）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>複数行入力結果</returns>
    Task<MultiLineResult> GetMultiLineInputAsync(
        string? prompt = null,
        string? endMarker = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 入力をキャンセルする
    /// </summary>
    void CancelInput();

    /// <summary>
    /// 入力履歴を取得
    /// </summary>
    IReadOnlyList<InputHistoryEntry> GetHistory(int maxCount = 100);

    /// <summary>
    /// オートコンプリート候補を設定
    /// </summary>
    void SetAutoCompleteProvider(Func<string, IEnumerable<string>>? provider);
}

/// <summary>
/// ユーザー入力結果
/// </summary>
public class InputResult
{
    /// <summary>
    /// 入力タイプ
    /// </summary>
    public InputType Type { get; set; } = InputType.Text;

    /// <summary>
    /// 入力されたテキスト
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// キャンセルされたかどうか
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// 終了要求かどうか
    /// </summary>
    public bool IsExitRequest { get; set; }

    /// <summary>
    /// 追加のコンテキスト情報
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();

    /// <summary>
    /// 入力時刻
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 正常なテキスト入力かどうか
    /// </summary>
    public bool HasText => !IsCancelled && !IsExitRequest && !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// 入力タイプ
/// </summary>
public enum InputType
{
    /// <summary>
    /// 通常のテキスト入力
    /// </summary>
    Text,

    /// <summary>
    /// コマンド（/で始まる等）
    /// </summary>
    Command,

    /// <summary>
    /// ファイルパス
    /// </summary>
    FilePath,

    /// <summary>
    /// URL
    /// </summary>
    Url,

    /// <summary>
    /// 選択（番号や文字）
    /// </summary>
    Selection,

    /// <summary>
    /// 音声入力
    /// </summary>
    Voice,

    /// <summary>
    /// ドラッグアンドドロップ
    /// </summary>
    DragDrop
}

/// <summary>
/// 選択結果
/// </summary>
public class SelectionResult
{
    /// <summary>
    /// 選択されたインデックス（-1はキャンセル）
    /// </summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>
    /// 選択されたオプション
    /// </summary>
    public SelectionOption? SelectedOption { get; set; }

    /// <summary>
    /// キャンセルされたかどうか
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// ユーザーが入力した追加テキスト（Other選択時など）
    /// </summary>
    public string? AdditionalInput { get; set; }

    /// <summary>
    /// 選択されたかどうか
    /// </summary>
    public bool HasSelection => !IsCancelled && SelectedIndex >= 0;
}

/// <summary>
/// 選択肢
/// </summary>
public class SelectionOption
{
    /// <summary>
    /// 表示ラベル
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 説明
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 値
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// ショートカットキー
    /// </summary>
    public string? Shortcut { get; set; }

    /// <summary>
    /// 非表示かどうか
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// 推奨オプションかどうか
    /// </summary>
    public bool IsRecommended { get; set; }
}

/// <summary>
/// 複数行入力結果
/// </summary>
public class MultiLineResult
{
    /// <summary>
    /// 入力行のリスト
    /// </summary>
    public List<string> Lines { get; set; } = new();

    /// <summary>
    /// 結合されたテキスト
    /// </summary>
    public string Text => string.Join(Environment.NewLine, Lines);

    /// <summary>
    /// キャンセルされたかどうか
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// 行数
    /// </summary>
    public int LineCount => Lines.Count;
}

/// <summary>
/// 入力履歴エントリ
/// </summary>
public class InputHistoryEntry
{
    /// <summary>
    /// 入力テキスト
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 入力時刻
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 入力タイプ
    /// </summary>
    public InputType Type { get; set; }

    /// <summary>
    /// 関連するタスクID（もしあれば）
    /// </summary>
    public string? TaskId { get; set; }
}
