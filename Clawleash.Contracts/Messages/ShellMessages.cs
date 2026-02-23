using MessagePack;

namespace Clawleash.Contracts;

/// <summary>
/// IPCメッセージのベース
/// </summary>
[MessagePackObject]
[Union(0, typeof(ShellExecuteRequest))]
[Union(1, typeof(ShellExecuteResponse))]
[Union(2, typeof(ShellInitializeRequest))]
[Union(3, typeof(ShellInitializeResponse))]
[Union(4, typeof(ShellShutdownRequest))]
[Union(5, typeof(ShellShutdownResponse))]
[Union(6, typeof(ShellPingRequest))]
[Union(7, typeof(ShellPingResponse))]
[Union(8, typeof(ToolInvokeRequest))]
[Union(9, typeof(ToolInvokeResponse))]
[Union(10, typeof(ShellReadyMessage))]
public abstract class ShellMessage
{
    [Key(0)]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    [Key(1)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// シェル準備完了メッセージ（Shell→Main）
/// </summary>
[MessagePackObject]
public class ShellReadyMessage : ShellMessage
{
    [Key(10)]
    public int ProcessId { get; set; }

    [Key(11)]
    public string Runtime { get; set; } = string.Empty;

    [Key(12)]
    public string OS { get; set; } = string.Empty;
}

/// <summary>
/// 初期化リクエスト（Main→Shell）
/// </summary>
[MessagePackObject]
public class ShellInitializeRequest : ShellMessage
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
/// 初期化レスポンス（Shell→Main）
/// </summary>
[MessagePackObject]
public class ShellInitializeResponse : ShellMessage
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
/// コマンド実行リクエスト（Main→Shell）
/// </summary>
[MessagePackObject]
public class ShellExecuteRequest : ShellMessage
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
    public ShellExecutionMode Mode { get; set; } = ShellExecutionMode.Constrained;
}

/// <summary>
/// コマンド実行レスポンス（Shell→Main）
/// </summary>
[MessagePackObject]
public class ShellExecuteResponse : ShellMessage
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
/// Tool 呼び出しリクエスト（Main→Shell）
/// </summary>
[MessagePackObject]
public class ToolInvokeRequest : ShellMessage
{
    [Key(10)]
    public string ToolName { get; set; } = string.Empty;

    [Key(11)]
    public string MethodName { get; set; } = string.Empty;

    [Key(12)]
    public object?[] Arguments { get; set; } = Array.Empty<object?>();
}

/// <summary>
/// Tool 呼び出しレスポンス（Shell→Main）
/// </summary>
[MessagePackObject]
public class ToolInvokeResponse : ShellMessage
{
    [Key(10)]
    public string RequestId { get; set; } = string.Empty;

    [Key(11)]
    public bool Success { get; set; }

    [Key(12)]
    public object? Result { get; set; }

    [Key(13)]
    public string? Error { get; set; }
}

/// <summary>
/// Ping リクエスト
/// </summary>
[MessagePackObject]
public class ShellPingRequest : ShellMessage
{
    [Key(10)]
    public string Payload { get; set; } = "ping";
}

/// <summary>
/// Ping レスポンス
/// </summary>
[MessagePackObject]
public class ShellPingResponse : ShellMessage
{
    [Key(10)]
    public string Payload { get; set; } = "pong";

    [Key(11)]
    public long ProcessingTimeMs { get; set; }
}

/// <summary>
/// シャットダウンリクエスト（Main→Shell）
/// </summary>
[MessagePackObject]
public class ShellShutdownRequest : ShellMessage
{
    [Key(10)]
    public bool Force { get; set; }
}

/// <summary>
/// シャットダウンレスポンス（Shell→Main）
/// </summary>
[MessagePackObject]
public class ShellShutdownResponse : ShellMessage
{
    [Key(10)]
    public bool Success { get; set; }
}
