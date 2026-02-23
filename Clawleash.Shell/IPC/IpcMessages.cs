using MessagePack;

namespace Clawleash.Shell.IPC;

/// <summary>
/// IPCメッセージのベース
/// </summary>
[MessagePackObject]
[Union(0, typeof(ExecuteRequest))]
[Union(1, typeof(ExecuteResponse))]
[Union(2, typeof(InitializeRequest))]
[Union(3, typeof(InitializeResponse))]
[Union(4, typeof(ShutdownRequest))]
[Union(5, typeof(ShutdownResponse))]
[Union(6, typeof(PingRequest))]
[Union(7, typeof(PingResponse))]
[Union(8, typeof(CustomCommandRequest))]
[Union(9, typeof(CustomCommandResponse))]
public abstract class IpcMessage
{
    [Key(0)]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    [Key(1)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// コマンド実行リクエスト
/// </summary>
[MessagePackObject]
public class ExecuteRequest : IpcMessage
{
    [Key(10)]
    public string Command { get; set; } = string.Empty;

    [Key(11)]
    public Dictionary<string, object?> Parameters { get; set; } = new();

    [Key(12)]
    public string? WorkingDirectory { get; set; }

    [Key(13)]
    public int TimeoutMs { get; set; } = 30000;

    [Key(14)]
    public ExecutionMode Mode { get; set; } = ExecutionMode.Constrained;
}

/// <summary>
/// コマンド実行レスポンス
/// </summary>
[MessagePackObject]
public class ExecuteResponse : IpcMessage
{
    [Key(10)]
    public string RequestId { get; set; } = string.Empty;

    [Key(11)]
    public bool Success { get; set; }

    [Key(12)]
    public string Output { get; set; } = string.Empty;

    [Key(13)]
    public string? Error { get; set; }

    [Key(14)]
    public int ExitCode { get; set; }

    [Key(15)]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

/// <summary>
/// 初期化リクエスト
/// </summary>
[MessagePackObject]
public class InitializeRequest : IpcMessage
{
    [Key(10)]
    public string[] AllowedCommands { get; set; } = Array.Empty<string>();

    [Key(11)]
    public string[] AllowedPaths { get; set; } = Array.Empty<string>();

    [Key(12)]
    public string[] ReadOnlyPaths { get; set; } = Array.Empty<string>();

    [Key(13)]
    public ShellLanguageMode LanguageMode { get; set; } = ShellLanguageMode.ConstrainedLanguage;
}

/// <summary>
/// 初期化レスポンス
/// </summary>
[MessagePackObject]
public class InitializeResponse : IpcMessage
{
    [Key(10)]
    public bool Success { get; set; }

    [Key(11)]
    public string? Error { get; set; }

    [Key(12)]
    public string Version { get; set; } = "1.0.0";

    [Key(13)]
    public string Runtime { get; set; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
}

/// <summary>
/// シャットダウンリクエスト
/// </summary>
[MessagePackObject]
public class ShutdownRequest : IpcMessage
{
    [Key(10)]
    public bool Force { get; set; }
}

/// <summary>
/// シャットダウンレスポンス
/// </summary>
[MessagePackObject]
public class ShutdownResponse : IpcMessage
{
    [Key(10)]
    public bool Success { get; set; }
}

/// <summary>
/// Ping リクエスト
/// </summary>
[MessagePackObject]
public class PingRequest : IpcMessage
{
    [Key(10)]
    public string Payload { get; set; } = "ping";
}

/// <summary>
/// Ping レスポンス
/// </summary>
[MessagePackObject]
public class PingResponse : IpcMessage
{
    [Key(10)]
    public string Payload { get; set; } = "pong";

    [Key(11)]
    public long ProcessingTimeMs { get; set; }
}

/// <summary>
/// カスタムコマンド実行リクエスト（Tool用）
/// </summary>
[MessagePackObject]
public class CustomCommandRequest : IpcMessage
{
    [Key(10)]
    public string ToolName { get; set; } = string.Empty;

    [Key(11)]
    public string Command { get; set; } = string.Empty;

    [Key(12)]
    public byte[]? Data { get; set; }
}

/// <summary>
/// カスタムコマンド実行レスポンス
/// </summary>
[MessagePackObject]
public class CustomCommandResponse : IpcMessage
{
    [Key(10)]
    public string RequestId { get; set; } = string.Empty;

    [Key(11)]
    public bool Success { get; set; }

    [Key(12)]
    public byte[]? Data { get; set; }

    [Key(13)]
    public string? Error { get; set; }
}

/// <summary>
/// 実行モード
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// 制約付き実行（デフォルト）
    /// </summary>
    Constrained,

    /// <summary>
    /// 完全実行（信頼済みコマンドのみ）
    /// </summary>
    Full,

    /// <summary>
    /// 言語なし（コマンドのみ）
    /// </summary>
    NoLanguage
}

/// <summary>
/// シェル言語モード
/// </summary>
public enum ShellLanguageMode
{
    FullLanguage,
    ConstrainedLanguage,
    RestrictedLanguage,
    NoLanguage
}
