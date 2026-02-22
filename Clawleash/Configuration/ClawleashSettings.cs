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
}

public enum SandboxType
{
    AppContainer,
    Bubblewrap,
    Docker
}

public class FileSystemSettings
{
    public List<string> AllowedDirectories { get; set; } = new();
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
