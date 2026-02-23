using Clawleash.Mcp;
using Clawleash.Sandbox;

namespace Clawleash.Configuration;

/// <summary>
/// Clawleashアプリケーションの全設定を統合するクラス
/// </summary>
public class ClawleashSettings
{
    public AISettings AI { get; set; } = new();
    public SandboxSettings Sandbox { get; set; } = new();
    public FileSystemSettings FileSystem { get; set; } = new();
    public PowerShellSettings PowerShell { get; set; } = new();
    public BrowserSettings Browser { get; set; } = new();
    public McpSettings Mcp { get; set; } = new();
    public ChatInterfaceSettings ChatInterface { get; set; } = new();
}

public class AISettings
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "gpt-4o";
}

public class SandboxSettings
{
    public SandboxType Type { get; set; } = SandboxType.AppContainer;
    public string DockerImage { get; set; } = "mcr.microsoft.com/powershell:7.4";
    public string AppContainerName { get; set; } = "Clawleash.Sandbox";

    /// <summary>
    /// AppContainerのケーパビリティ（ネットワーク、ライブラリアクセスなど）
    /// デフォルトはインターネットアクセスとプライベートネットワークアクセス
    /// </summary>
    public AppContainerCapability Capabilities { get; set; } =
        AppContainerCapability.InternetClient |
        AppContainerCapability.PrivateNetworkClientServer;

    /// <summary>
    /// フォルダーごとのセキュリティポリシー
    /// より具体的なパスが優先され、子フォルダーで親の設定を上書き可能
    /// </summary>
    public List<FolderPolicy> FolderPolicies { get; set; } = new();
}

public enum SandboxType
{
    AppContainer,
    Bubblewrap,
    Docker
}

public class FileSystemSettings
{
    /// <summary>
    /// 読み書きを許可するディレクトリ（古い形式、FolderPoliciesの使用を推奨）
    /// </summary>
    public List<string> AllowedDirectories { get; set; } = new();

    /// <summary>
    /// 読み取り専用ディレクトリ（古い形式、FolderPoliciesの使用を推奨）
    /// </summary>
    public List<string> ReadOnlyDirectories { get; set; } = new();

    public int MaxFileSizeMB { get; set; } = 10;
}

public class PowerShellSettings
{
    public string PowerShellPath { get; set; } = "pwsh";
    public CommandFilterMode Mode { get; set; } = CommandFilterMode.Whitelist;
    public List<string> AllowedCommands { get; set; } = new();
    public List<string> DeniedCommands { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 30;
}

public enum CommandFilterMode
{
    Whitelist,
    Blacklist
}

public class BrowserSettings
{
    public List<string> AllowedDomains { get; set; } = new() { "*" };
    public List<string> DeniedDomains { get; set; } = new();
    public bool Headless { get; set; } = true;
    public bool ScreenshotOnAction { get; set; } = true;
}

/// <summary>
/// チャットインターフェース設定
/// </summary>
public class ChatInterfaceSettings
{
    /// <summary>
    /// CLIを有効にするか
    /// </summary>
    public bool EnableCli { get; set; } = true;

    /// <summary>
    /// 外部インターフェースDLLを配置するディレクトリ
    /// nullの場合はデフォルト（%LocalAppData%\Clawleash\Interfaces）
    /// </summary>
    public string? InterfacesDirectory { get; set; }

    /// <summary>
    /// ホットリロードを有効にするか
    /// </summary>
    public bool EnableHotReload { get; set; } = true;

    /// <summary>
    /// Discord設定
    /// </summary>
    public DiscordInterfaceSettings Discord { get; set; } = new();

    /// <summary>
    /// Slack設定
    /// </summary>
    public SlackInterfaceSettings Slack { get; set; } = new();

    /// <summary>
    /// WebSocket設定
    /// </summary>
    public WebSocketInterfaceSettings WebSocket { get; set; } = new();

    /// <summary>
    /// WebRTC設定
    /// </summary>
    public WebRtcInterfaceSettings WebRtc { get; set; } = new();
}

public class DiscordInterfaceSettings
{
    public bool Enabled { get; set; } = false;
    public string Token { get; set; } = string.Empty;
    public string CommandPrefix { get; set; } = "!";
}

public class SlackInterfaceSettings
{
    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = string.Empty;
    public string AppToken { get; set; } = string.Empty;
}

public class WebSocketInterfaceSettings
{
    public bool Enabled { get; set; } = false;
    public string ServerUrl { get; set; } = "ws://localhost:8080/chat";
    public bool EnableE2ee { get; set; } = true;
    public int ReconnectIntervalMs { get; set; } = 5000;
    public int MaxReconnectAttempts { get; set; } = 10;
}

public class WebRtcInterfaceSettings
{
    public bool Enabled { get; set; } = false;
    public string SignalingServerUrl { get; set; } = "ws://localhost:8080/signaling";
    public bool EnableE2ee { get; set; } = true;
    public List<string> StunServers { get; set; } = new() { "stun:stun.l.google.com:19302" };
}
