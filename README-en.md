<div align="center">

# Clawleash

**Autonomous AI Agent with Sandbox Execution**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-58%20passed-brightgreen?style=flat-square)](Clawleash.Tests)

*Semantic Kernel × Playwright × PowerShell × MCP × Sandbox Architecture × Multi-Interface*

English | [**日本語**](README.md)

</div>

---

## Overview

Clawleash is an **autonomous AI agent** that runs in a secure sandbox environment. Built on Microsoft Semantic Kernel and Playwright, it provides Firecrawl-style web scraping and autonomous browser operation.

### Key Features

- **Multi-Interface Support**: CLI / Discord / Slack / WebSocket / WebRTC
- **E2EE Support**: End-to-end encryption for WebSocket and WebRTC
- **Sandboxed Execution**: AppContainer (Windows) / Bubblewrap (Linux) isolation
- **Folder Policies**: Directory-level access control and network restrictions
- **Tool Package System**: Add tools via ZIP/DLL packages
- **Skill System**: Define and reuse prompt templates in YAML/JSON
- **MCP Client**: Integrate tools from external MCP servers
- **Approval System**: User approval required for dangerous operations

---

## Features

### Multi-Interface Support

Clawleash supports multiple input interfaces simultaneously.

| Interface | Description | E2EE |
|-----------|-------------|------|
| **CLI** | Standard console input (built-in) | - |
| **Discord** | Discord Bot message reception | - |
| **Slack** | Slack Bot (HTTP API + polling) | - |
| **WebSocket** | Real-time communication via SignalR | ✅ AES-256-GCM |
| **WebRTC** | P2P communication via DataChannel | ✅ DTLS-SRTP |

**Architecture:**
```
┌─────────────────────────────────────────────────────────────────────┐
│                       Clawleash (Main Application)                   │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │     InterfaceLoader + FileSystemWatcher (Hot Reload)            │ │
│  │  %LocalAppData%\Clawleash\Interfaces\ monitored                  │ │
│  │  New DLL → Auto-load → Register with ChatInterfaceManager       │ │
│  └──────────────────────────┬──────────────────────────────────────┘ │
│                             │                                        │
│  ┌──────────────────────────┴──────────────────────────────────────┐ │
│  │                   ChatInterfaceManager                           │ │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────┐ │ │
│  │  │   CLI    │ │ Discord  │ │  Slack   │ │ WebSocket│ │ WebRTC │ │ │
│  │  │(Built-in)│ │  (DLL)   │ │  (DLL)   │ │  (DLL)   │ │ (DLL)  │ │ │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └────────┘ │ │
│  └───────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

**Configuration Example (appsettings.json):**
```json
{
  "ChatInterface": {
    "EnableCli": true,
    "EnableHotReload": true,
    "InterfacesDirectory": null,
    "Discord": {
      "Enabled": true,
      "Token": "${DISCORD_BOT_TOKEN}",
      "CommandPrefix": "!"
    },
    "Slack": {
      "Enabled": true,
      "BotToken": "${SLACK_BOT_TOKEN}"
    },
    "WebSocket": {
      "Enabled": true,
      "ServerUrl": "wss://localhost:8080/chat",
      "EnableE2ee": true
    },
    "WebRtc": {
      "Enabled": true,
      "SignalingServerUrl": "wss://localhost:8080/signaling",
      "EnableE2ee": true
    }
  }
}
```

### E2EE (End-to-End Encryption)

Enable E2EE for WebSocket and WebRTC communications.

```
┌──────────────┐                      ┌──────────────┐
│   Client     │                      │    Server    │
│              │                      │              │
│  1. Exchange │ ◄─── X25519 ────────► │              │
│              │                      │              │
│  2. Encrypt  │                      │              │
│  Plaintext   │                      │              │
│     │        │                      │              │
│     ▼        │                      │              │
│  AES-256-GCM │                      │              │
│     │        │                      │              │
│     ▼        │                      │              │
│  Ciphertext  │ ──── wss:// ────────► │  3. Decrypt  │
│              │                      │  AES-256-GCM │
│              │                      │     │        │
│              │                      │     ▼        │
│              │                      │  Plaintext   │
└──────────────┘                      └──────────────┘
```

### Web Crawler (Firecrawl-style)

| Function | Description |
|----------|-------------|
| `ScrapeUrl` | Scrape a URL and get content in Markdown format |
| `CrawlWebsite` | Crawl entire websites with multi-page content extraction |
| `MapWebsite` | Generate sitemap (all URLs) from any website |
| `SearchWeb` | Search the web (DuckDuckGo, no API key required) |
| `BatchScrape` | Bulk scrape multiple URLs |

### File Operations

| Function | Description |
|----------|-------------|
| `CreateFile` / `ReadFile` | Create and read files |
| `ReplaceLine` / `ReplaceText` | Replace lines or text |
| `InsertLine` / `DeleteLine` | Insert or delete lines |
| `MoveFile` / `CopyFile` | Move or copy files |
| `CreateFolder` / `DeleteFolder` | Create or delete folders |
| `ShowTree` | Display directory structure as tree |

### Browser Automation

- **Basic Operations**: Navigate, click, type, form submit
- **Scroll**: Page scroll, scroll to bottom
- **Wait Operations**: Wait for element, timeout, page load
- **Keyboard**: Enter, Tab, Escape, arrow keys, etc.
- **Mouse**: Double-click, right-click, drag & drop
- **Storage**: Cookie, localStorage, sessionStorage

### AI-Powered Data Extraction

- `ExtractStructuredData`: AI-powered structured data extraction
- `ExtractProductInfo`: Auto-extract product information
- `SummarizePage`: Page content summarization

### Autonomous Agent

- **Goal Planning & Execution**: AI automatically breaks down and executes tasks
- **Self-Evaluation & Correction**: Evaluate results and try alternative approaches on failure
- **Human-in-the-Loop**: User approval required for dangerous operations

### Skill System

Define prompt templates as reusable "skills" and execute them.

| Function | Description |
|----------|-------------|
| `list_skills` | List available skills |
| `execute_skill` | Execute a specific skill |
| `show_skill` | Show skill details |
| `register_skill` | Register a new skill (YAML/JSON) |
| `remove_skill` | Remove a skill |

**Skill Definition Example (YAML):**
```yaml
name: summarize
description: Summarize text
version: "1.0.0"
tags: [text, summarization]

systemInstruction: |
  You are a professional summarization assistant.

parameters:
  - name: text
    type: string
    description: Text to summarize
    required: true
  - name: style
    type: string
    description: Summary style
    required: false
    default: concise
    enum: [concise, detailed, bullet-points]

prompt: |
  Summarize the following text in {{style}} style:
  {{text}}
```

**Skills Directory:** `%LocalAppData%\Clawleash\Skills\`

### MCP (Model Context Protocol) Client

Use tools from external MCP servers within Clawleash.

| Function | Description |
|----------|-------------|
| `list_tools` | List MCP server tools |
| `execute_tool` | Execute an MCP tool |

**Transport Support:**
- **stdio**: Local NPX packages, Docker containers
- **SSE**: Remote MCP servers (HTTP Server-Sent Events)

---

## Security

### Sandbox

| Platform | Implementation | Features |
|----------|----------------|----------|
| Windows | AppContainer | Capability-based access control |
| Linux | Bubblewrap | Namespace isolation |

### AppContainer Capabilities

Configure capabilities to grant to the AppContainer process.

```json
{
  "Sandbox": {
    "Type": "AppContainer",
    "AppContainerName": "Clawleash.Sandbox",
    "Capabilities": "InternetClient, PrivateNetworkClientServer"
  }
}
```

**Available Capabilities:**

| Capability | Description |
|------------|-------------|
| `InternetClient` | Outbound internet connections |
| `InternetClientServer` | Inbound and outbound internet connections |
| `PrivateNetworkClientServer` | Private network connections |
| `PicturesLibrary` | Pictures library access |
| `VideosLibrary` | Videos library access |
| `MusicLibrary` | Music library access |
| `DocumentsLibrary` | Documents library access |
| `EnterpriseAuthentication` | Enterprise authentication |
| `SharedUserCertificates` | Shared certificates |
| `RemovableStorage` | Removable storage access |
| `Appointments` | Appointments access |
| `Contacts` | Contacts access |

### Folder Policies

Configure detailed access control per directory. More specific paths take precedence, and child folders can override parent settings.

```json
{
  "Sandbox": {
    "FolderPolicies": [
      {
        "Path": "C:\\Projects",
        "Access": "ReadWrite",
        "Network": "Allow",
        "Execute": "Allow",
        "Name": "Project Folder"
      },
      {
        "Path": "C:\\Projects\\Secrets",
        "Access": "Deny",
        "Network": "Deny",
        "Name": "Sensitive Data (Access Denied)"
      },
      {
        "Path": "C:\\Projects\\Public",
        "Access": "ReadOnly",
        "Network": "Allow",
        "Name": "Public Area (Read-Only)"
      },
      {
        "Path": "C:\\Work",
        "Access": "ReadWrite",
        "Network": "Allow",
        "DeniedExtensions": ["exe", "bat", "ps1"],
        "MaxFileSizeMB": 50,
        "Name": "Work Folder"
      },
      {
        "Path": "C:\\Work\\Downloads",
        "Access": "ReadWrite",
        "Network": "Allow",
        "Execute": "Deny",
        "EnableAudit": true,
        "Name": "Downloads (No Execution, Audited)"
      }
    ]
  }
}
```

**Policy Properties:**

| Property | Values | Description |
|----------|--------|-------------|
| `Access` | `Deny` / `ReadOnly` / `ReadWrite` / `FullControl` | File system access |
| `Network` | `Inherit` / `Allow` / `Deny` | Network access |
| `Execute` | `Inherit` / `Allow` / `Deny` | Process execution |
| `AllowedExtensions` | `[".txt", ".json"]` | Allowed file extensions |
| `DeniedExtensions` | `[".exe", ".bat"]` | Denied file extensions |
| `MaxFileSizeMB` | `10` | Maximum file size |
| `EnableAudit` | `true` | Access logging |

**Inheritance Rules:**
```
C:\Projects          → ReadWrite, Network=Allow
  ├─ \Secrets        → Deny, Network=Deny        ← Overrides parent (disabled)
  ├─ \Public         → ReadOnly, Network=Allow  ← Changed to read-only
  └─ \Data
       └─ \Sensitive → Deny                      ← Can override at any depth
```

### PowerShell Constraints

- **ConstrainedLanguage**: Default safe mode
- **Command Whitelist**: Only allowed commands execute
- **Path Restrictions**: Only allowed paths accessible

### Approval System

```csharp
// For CLI (console approval)
services.AddCliApprovalHandler();

// For automation (rule-based)
services.AddSilentApprovalHandler(config);
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Clawleash (Main)                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Kernel    │  │ ToolLoader  │  │   ShellServer       │  │
│  │  (AI Agent) │  │ (ZIP/DLL)   │  │   (ZeroMQ Router)   │  │
│  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘  │
│         │                │                     │ IPC         │
│         ├────────────────┼─────────────────────┤             │
│         │  SkillLoader   │   McpClientManager  │             │
│         │  (YAML/JSON)   │   (stdio/SSE)       │             │
│         └────────────────┴─────────────────────┘             │
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              ChatInterfaceManager                        │ │
│  │  ┌─────┐ ┌─────────┐ ┌────────┐ ┌──────────┐ ┌───────┐  │ │
│  │  │ CLI │ │ Discord │ │ Slack  │ │ WebSocket│ │ WebRTC│  │ │
│  │  └─────┘ └─────────┘ └────────┘ └──────────┘ └───────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼ MessagePack over ZeroMQ
┌─────────────────────────────────────────────────────────────┐
│                    Clawleash.Shell (Sandboxed)               │
│  ┌─────────────┐  ┌─────────────────────────────────────┐   │
│  │  IpcClient  │  │     ConstrainedRunspaceHost         │   │
│  │  (Dealer)   │  │     (PowerShell SDK)                │   │
│  └─────────────┘  └─────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                    Clawleash.Server (Optional)               │
│  ┌─────────────────────┐  ┌─────────────────────────────┐   │
│  │     ChatHub         │  │     SignalingHub            │   │
│  │  (WebSocket/E2EE)   │  │  (WebRTC Signaling)         │   │
│  └─────────────────────┘  └─────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │     Svelte Client (Static Files)                        │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Project Structure

```
Clowleash/
├── Clawleash/                    # Main Application
│   ├── Execution/
│   │   └── ShellServer.cs        # IPC Server
│   ├── Tools/
│   │   ├── ToolLoader.cs         # Tool Loader
│   │   ├── ToolPackage.cs        # Package Management
│   │   ├── ToolProxyGenerator.cs # Proxy Generation (Reflection.Emit)
│   │   └── ShellToolExecutor.cs  # IPC Execution
│   ├── Skills/
│   │   └── SkillLoader.cs        # Skill Loader (YAML/JSON)
│   ├── Mcp/
│   │   ├── McpClientManager.cs   # MCP Client Management
│   │   ├── McpServerConfig.cs    # MCP Server Configuration
│   │   └── McpToolAdapter.cs     # Semantic Kernel Integration
│   ├── Services/
│   │   ├── IApprovalHandler.cs   # Approval System
│   │   ├── IInputHandler.cs      # Input System
│   │   ├── InterfaceLoader.cs    # Interface Dynamic Loading
│   │   ├── ChatInterfaceManager.cs # Multi-Interface Management
│   │   └── CliChatInterface.cs   # CLI Interface
│   ├── Sandbox/
│   │   ├── AppContainerProvider.cs  # Windows (Capability Support)
│   │   ├── BubblewrapProvider.cs    # Linux
│   │   ├── FolderPolicy.cs          # Folder Policy Definition
│   │   ├── FolderPolicyManager.cs   # Policy Management & Inheritance
│   │   ├── NativeMethods.cs         # P/Invoke Definitions
│   │   └── AclManager.cs            # ACL Management
│   ├── Security/
│   │   ├── UrlValidator.cs
│   │   ├── PathValidator.cs
│   │   └── CommandValidator.cs
│   └── Plugins/                 # Semantic Kernel Plugins
│       ├── WebCrawlerPlugin.cs
│       ├── BrowserActionsPlugin.cs
│       ├── FileOperationsPlugin.cs
│       ├── SkillPlugin.cs
│       └── ...
│
├── Clawleash.Shell/              # Sandbox Process
│   ├── IPC/IpcClient.cs          # IPC Client (DealerSocket)
│   └── Hosting/
│       └── ConstrainedRunspaceHost.cs  # Constrained PowerShell
│
├── Clawleash.Abstractions/       # Shared Interfaces
│   ├── Services/
│   │   ├── IChatInterface.cs     # Chat Interface
│   │   └── ChatMessageReceivedEventArgs.cs
│   └── Security/
│       └── IE2eeProvider.cs      # E2EE Provider
│
├── Clawleash.Interfaces.Discord/ # Discord Bot Interface
│   ├── DiscordChatInterface.cs
│   └── DiscordSettings.cs
│
├── Clawleash.Interfaces.Slack/   # Slack Bot Interface
│   ├── SlackChatInterface.cs
│   └── SlackSettings.cs
│
├── Clawleash.Interfaces.WebSocket/ # WebSocket Interface (E2EE)
│   ├── WebSocketChatInterface.cs
│   ├── Security/
│   │   └── AesGcmE2eeProvider.cs
│   └── WebSocketSettings.cs
│
├── Clawleash.Interfaces.WebRTC/  # WebRTC Interface (E2EE)
│   ├── WebRtcChatInterface.cs
│   ├── Security/
│   │   └── WebRtcE2eeProvider.cs
│   └── WebRtcSettings.cs
│
├── Clawleash.Server/             # SignalR Server (WebSocket/WebRTC)
│   ├── Hubs/
│   │   ├── ChatHub.cs            # Chat Hub (E2EE)
│   │   └── SignalingHub.cs       # WebRTC Signaling
│   ├── Security/
│   │   ├── KeyManager.cs         # Key Management
│   │   └── E2eeMiddleware.cs     # E2EE Middleware
│   └── Client/                   # Svelte Frontend
│
├── Clawleash.Contracts/          # Shared Types
│   └── Messages/
│       ├── ShellMessages.cs      # IPC Messages
│       └── Enums.cs              # Shared Enums
│
├── Clawleash.Tests/              # Unit Tests
│
└── sample-skills/                # Sample Skills
```

---

## Installation

```bash
# Clone repository
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash

# Restore dependencies
dotnet restore

# Install Playwright browsers
pwsh bin/Debug/net10.0/.playwright/package/cli.js install
```

---

## Configuration

`appsettings.json`:

```json
{
  "AI": {
    "ApiKey": "your-api-key",
    "ModelId": "gpt-4o",
    "Endpoint": "https://api.openai.com/v1"
  },
  "Sandbox": {
    "Type": "AppContainer",
    "AppContainerName": "Clawleash.Sandbox",
    "Capabilities": "InternetClient, PrivateNetworkClientServer",
    "FolderPolicies": [
      {
        "Path": "C:\\Projects",
        "Access": "ReadWrite",
        "Network": "Allow",
        "Execute": "Allow"
      },
      {
        "Path": "C:\\Projects\\Secrets",
        "Access": "Deny",
        "Network": "Deny"
      }
    ]
  },
  "ChatInterface": {
    "EnableCli": true,
    "EnableHotReload": true,
    "Discord": { "Enabled": false },
    "Slack": { "Enabled": false },
    "WebSocket": { "Enabled": false },
    "WebRtc": { "Enabled": false }
  },
  "Browser": {
    "Headless": true
  },
  "Mcp": {
    "Enabled": true,
    "Servers": []
  }
}
```

---

## Usage

### Main Application

```bash
dotnet run --project Clawleash
```

### SignalR Server (for WebSocket/WebRTC)

```bash
dotnet run --project Clawleash.Server
# Server starts at http://localhost:5000
# /chat - WebSocket hub
# /signaling - WebRTC signaling hub
```

### Adding Interface DLLs

```
%LocalAppData%\Clawleash\Interfaces\
├── Discord\
│   ├── Clawleash.Interfaces.Discord.dll
│   └── Discord.Net.dll
├── Slack\
│   ├── Clawleash.Interfaces.Slack.dll
│   └── (dependencies)
├── WebSocket\
│   └── Clawleash.Interfaces.WebSocket.dll
└── WebRTC\
    └── Clawleash.Interfaces.WebRTC.dll
```

Hot-reload enabled: New DLLs are automatically loaded when placed in the directory.

### Creating Custom Providers

You can create and add your own chat interfaces.

**Steps:**
1. Create a class library project referencing `Clawleash.Abstractions`
2. Implement `IChatInterface`
3. Build and place in `%LocalAppData%\Clawleash\Interfaces\`

See [Clawleash.Abstractions](Clawleash.Abstractions/README-en.md) for detailed development guide.

**Example Implementations:**
- [Discord](Clawleash.Interfaces.Discord/README-en.md) - Discord Bot
- [Slack](Clawleash.Interfaces.Slack/README-en.md) - Slack Bot
- [WebSocket](Clawleash.Interfaces.WebSocket/README-en.md) - WebSocket (E2EE)
- [WebRTC](Clawleash.Interfaces.WebRTC/README-en.md) - WebRTC (E2EE)

### Adding Skills

```
%LocalAppData%\Clawleash\Skills\
└── my-skill.skill.yaml       # YAML format
└── my-skill.skill.json       # or JSON format
```

---

## IPC Communication

Communication between Main and Shell processes uses ZeroMQ + MessagePack.

### Communication Specification

| Item | Specification |
|------|---------------|
| Library | NetMQ (ZeroMQ .NET implementation) |
| Pattern | Router/Dealer |
| Serialization | MessagePack (Union attribute for polymorphism) |
| Transport | TCP (localhost) |
| Direction | Main (Router/Server) ↔ Shell (Dealer/Client) |

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Main Process                              │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              RouterSocket (Server)                       │ │
│  │  - Bind to random port                                   │ │
│  │  - Multiple client connections supported                 │ │
│  │  - Client identification via Identity                    │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                            │
                    ZeroMQ (TCP)
                            │
┌─────────────────────────────────────────────────────────────┐
│                   Shell Process (Sandboxed)                  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              DealerSocket (Client)                       │ │
│  │  - Dynamically assigned Identity                         │ │
│  │  - Async request/response                                │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Connection Flow

```
Main                                Shell
  │                                   │
  │  1. RouterSocket.BindRandomPort  │
  │     (e.g., tcp://127.0.0.1:5555) │
  │                                   │
  │                     2. Process start --server "tcp://..."
  │                                   │
  │                     3. DealerSocket.Connect()
  │                                   │
  │  ◄─────── ShellReadyMessage ───── │
  │     (ProcessId, Runtime, OS)     │
  │                                   │
  │  ──── ShellInitializeRequest ────►│
  │     (AllowedCommands, Paths)     │
  │                                   │
  │  ◄─── ShellInitializeResponse ───│
  │     (Success, Version)           │
  │                                   │
  │         Ready                     │
```

### Message Types

#### Base Message (Common to All Messages)

```csharp
public abstract class ShellMessage
{
    public string MessageId { get; set; }      // Unique identifier
    public DateTime Timestamp { get; set; }    // UTC timestamp
}
```

#### 1. ShellReadyMessage (Shell → Main)

Ready notification sent on connection completion.

```csharp
public class ShellReadyMessage : ShellMessage
{
    public int ProcessId { get; set; }        // Shell process ID
    public string Runtime { get; set; }       // .NET runtime info
    public string OS { get; set; }            // OS information
}
```

#### 2. ShellInitializeRequest/Response (Main ↔ Shell)

Initialize the Shell execution environment.

**Request:**
```csharp
public class ShellInitializeRequest : ShellMessage
{
    public string[] AllowedCommands { get; set; }    // Permitted commands
    public string[] AllowedPaths { get; set; }       // Read/write allowed paths
    public string[] ReadOnlyPaths { get; set; }      // Read-only paths
    public ShellLanguageMode LanguageMode { get; set; } // ConstrainedLanguage
}
```

**Response:**
```csharp
public class ShellInitializeResponse : ShellMessage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Version { get; set; }
    public string Runtime { get; set; }
}
```

#### 3. ShellExecuteRequest/Response (Main ↔ Shell)

Execute PowerShell commands.

**Request:**
```csharp
public class ShellExecuteRequest : ShellMessage
{
    public string Command { get; set; }              // Command to execute
    public Dictionary<string, object?> Parameters { get; set; }
    public string? WorkingDirectory { get; set; }
    public int TimeoutMs { get; set; } = 30000;
    public ShellExecutionMode Mode { get; set; }
}
```

**Response:**
```csharp
public class ShellExecuteResponse : ShellMessage
{
    public string RequestId { get; set; }
    public bool Success { get; set; }
    public string Output { get; set; }              // Standard output
    public string? Error { get; set; }              // Error message
    public int ExitCode { get; set; }
    public Dictionary<string, object?> Metadata { get; set; }
}
```

#### 4. ToolInvokeRequest/Response (Main ↔ Shell)

Invoke methods from tool packages.

**Request:**
```csharp
public class ToolInvokeRequest : ShellMessage
{
    public string ToolName { get; set; }            // Tool name
    public string MethodName { get; set; }          // Method name
    public object?[] Arguments { get; set; }        // Arguments
}
```

**Response:**
```csharp
public class ToolInvokeResponse : ShellMessage
{
    public string RequestId { get; set; }
    public bool Success { get; set; }
    public object? Result { get; set; }             // Return value
    public string? Error { get; set; }
}
```

#### 5. ShellPingRequest/Response (Main ↔ Shell)

Health monitoring and latency measurement.

**Request:**
```csharp
public class ShellPingRequest : ShellMessage
{
    public string Payload { get; set; } = "ping";
}
```

**Response:**
```csharp
public class ShellPingResponse : ShellMessage
{
    public string Payload { get; set; } = "pong";
    public long ProcessingTimeMs { get; set; }      // Processing time
}
```

#### 6. ShellShutdownRequest/Response (Main ↔ Shell)

Shutdown the Shell process.

**Request:**
```csharp
public class ShellShutdownRequest : ShellMessage
{
    public bool Force { get; set; }                 // Force shutdown flag
}
```

**Response:**
```csharp
public class ShellShutdownResponse : ShellMessage
{
    public bool Success { get; set; }
}
```

### MessagePack Serialization

```csharp
// Union attribute for polymorphic deserialization
[MessagePackObject]
[Union(0, typeof(ShellExecuteRequest))]
[Union(1, typeof(ShellExecuteResponse))]
[Union(2, typeof(ShellInitializeRequest))]
// ...
public abstract class ShellMessage { ... }

// Serialize
var data = MessagePackSerializer.Serialize(request);

// Deserialize
var message = MessagePackSerializer.Deserialize<ShellMessage>(data);
```

### Error Handling

```csharp
try
{
    var response = await shellServer.ExecuteAsync(request);
    if (!response.Success)
    {
        // Command execution failed
        Console.WriteLine($"Error: {response.Error}");
    }
}
catch (TimeoutException)
{
    // No response from Shell
}
catch (InvalidOperationException)
{
    // Shell not connected
}
```

### Timeout Configuration

```csharp
var options = new ShellServerOptions
{
    StartTimeoutMs = 10000,           // Startup timeout
    CommunicationTimeoutMs = 30000,   // Communication timeout
    Verbose = true                    // Verbose logging
};
```

---

## Developing Extensions

### Adding MCP Servers

Add external MCP servers to use their tools within Clawleash.

**appsettings.json:**

```json
{
  "Mcp": {
    "Enabled": true,
    "Servers": [
      {
        "name": "filesystem",
        "transport": "stdio",
        "command": "npx",
        "args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/allowed/dir"],
        "enabled": true,
        "timeoutMs": 30000,
        "useSandbox": true
      },
      {
        "name": "github",
        "transport": "stdio",
        "command": "npx",
        "args": ["-y", "@modelcontextprotocol/server-github"],
        "environment": {
          "GITHUB_TOKEN": "${GITHUB_TOKEN}"
        },
        "enabled": true
      },
      {
        "name": "remote-server",
        "transport": "sse",
        "url": "https://api.example.com/mcp/sse",
        "headers": {
          "Authorization": "Bearer ${API_KEY}"
        },
        "enabled": true,
        "timeoutMs": 60000
      }
    ]
  }
}
```

**MCP Configuration Properties:**

| Property | Description | Required |
|----------|-------------|----------|
| `name` | Server name (unique identifier) | ✅ |
| `transport` | `stdio` or `sse` | ✅ |
| `command` | Command to execute (stdio) | stdio |
| `args` | Command arguments | No |
| `environment` | Environment variables | No |
| `url` | Server URL (SSE) | sse |
| `headers` | HTTP headers (SSE) | No |
| `enabled` | Enable/disable server | No |
| `timeoutMs` | Timeout in milliseconds | No |
| `useSandbox` | Run in sandbox | No |

### Adding Tool Packages (Native Function Calling)

Create custom tools using Semantic Kernel's `[KernelFunction]` attribute.

**Project Structure:**

```
MyToolPackage/
├── MyToolPackage.csproj
├── WeatherTools.cs
└── tool-manifest.json (optional)
```

**tool-manifest.json:**

```json
{
  "name": "WeatherTools",
  "version": "1.0.0",
  "description": "Weather information tools",
  "mainAssembly": "MyToolPackage.dll"
}
```

**WeatherTools.cs:**

```csharp
using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace MyToolPackage;

public class WeatherTools
{
    [KernelFunction("get_weather")]
    [Description("Get weather for specified city")]
    public async Task<string> GetWeatherAsync(
        [Description("City name")] string city,
        [Description("Unit (celsius/fahrenheit)")] string unit = "celsius")
    {
        // Weather retrieval logic
        return $"Weather in {city}: Sunny, Temperature: 25{unit}";
    }
}
```

**Deployment:**

```bash
# Create ZIP package
cd MyToolPackage/bin/Release/net10.0
zip -r ../../weather-tools.zip .

# Copy to packages directory
cp ../../weather-tools.zip "%LocalAppData%/Clawleash/Packages/"
```

**Tool Package Directory:** `%LocalAppData%\Clawleash\Packages\`

Hot-reload enabled: New ZIP files are automatically loaded when placed in the directory.

#### Native Tool Packages vs MCP

| Aspect | Native Tool Packages | MCP |
|--------|---------------------|-----|
| **Execution** | Direct execution in sandbox | External process |
| **Access Control** | AppContainer + Folder policies | Controlled by MCP server |
| **Network** | Capability-based control | Depends on MCP server |
| **Process Isolation** | Isolated within Shell process | Completely separate process |
| **Auditing** | Detailed logging available | MCP server dependent |
| **Deployment** | Simple ZIP deployment | MCP server setup required |

**Use Native Tool Packages when:**
- Internal tools or handling sensitive data
- Strict access control is required
- Audit logging is mandatory
- Network access should be restricted

**Use MCP when:**
- Using existing MCP servers
- Integrating with external services
- Using community-provided tools

### Sandbox Execution Architecture

Tools are executed in a sandboxed environment for security.

**Architecture:**

```
┌─────────────────────────────────────────────────────────────┐
│                    Clawleash (Main Process)                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Kernel    │  │ ToolLoader  │  │   ShellServer       │  │
│  │  (AI Agent) │  │ (ZIP/DLL)   │  │   (ZeroMQ Router)   │  │
│  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘  │
│         │                │                     │ IPC           │
└─────────┼────────────────┼─────────────────────┼─────────────┘
          │                │                     │
          │  ┌─────────────┴─────────────────────┘
          │  │
          ▼  ▼
┌─────────────────────────────────────────────────────────────┐
│                  Clawleash.Shell (Sandboxed Process)         │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │           AppContainer (Windows) / Bubblewrap (Linux)    │ │
│  │  - File system access control                            │ │
│  │  - Network access control                                │ │
│  │  - Process execution control                              │ │
│  │  - Folder policy enforcement                              │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │  AssemblyLoadContext (isCollectible: true)               │ │
│  │  - Tool DLLs loaded in isolated context                  │ │
│  │  - Can be unloaded when tool is removed                  │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

**Execution Flow:**

1. **Tool Invocation:**
   - Kernel → ToolProxy → ShellServer (IPC)
   - ShellServer → Shell (inside sandbox)
   - Shell → AssemblyLoadContext → Tool DLL
   - Results returned in reverse order

2. **Isolation Mechanism:**
   - Each tool package is loaded in a separate `AssemblyLoadContext`
   - Unloadable (`isCollectible: true`)
   - Memory released when tool is removed

3. **Security Boundaries:**
   - **Process Isolation**: Main ↔ Shell are separate processes
   - **OS-level Isolation**: AppContainer/Bubblewrap restricts resources
   - **Folder Policies**: Path-based access control

**AppContainer Capabilities (Windows):**

```json
{
  "Sandbox": {
    "Type": "AppContainer",
    "AppContainerName": "Clawleash.Sandbox",
    "Capabilities": "InternetClient, PrivateNetworkClientServer"
  }
}
```

| Capability | Allowed Operations |
|------------|-------------------|
| `InternetClient` | Outbound internet connections |
| `PrivateNetworkClientServer` | Private network connections |
| None | No network access |

**Folder Policy Control:**

```json
{
  "Sandbox": {
    "FolderPolicies": [
      {
        "Path": "C:\\Work",
        "Access": "ReadWrite",
        "Execute": "Deny",
        "DeniedExtensions": [".exe", ".bat", ".ps1"]
      }
    ]
  }
}
```

---

## Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Verbose test output
dotnet test --verbosity normal
```

---

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## License

MIT License - See [LICENSE](LICENSE)

---

<div align="center">

**Made with ❤️ by actbit**

English | [日本語](README.md) | [⬆ Back to Top](#clawleash)

</div>
