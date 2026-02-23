<div align="center">

# Clawleash

**Autonomous AI Agent with Sandbox Execution**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

*Semantic Kernel × Playwright × PowerShell × Sandbox Architecture*

English | [**日本語**](README.md)

</div>

---

## Overview

Clawleash is an **autonomous AI agent** that runs in a secure sandbox environment. Built on Microsoft Semantic Kernel and Playwright, it provides Firecrawl-style web scraping and autonomous browser operation.

### Key Features

- **Sandboxed Execution**: Run PowerShell/commands safely in isolated processes
- **Tool Package System**: Add tools via ZIP/DLL packages
- **Approval System**: User approval required for dangerous operations
- **Multi-Platform**: Windows (AppContainer) / Linux (Bubblewrap)

---

## Features

### Web Crawler (Firecrawl-style)

| Function | Description |
|----------|-------------|
| `ScrapeUrl` | Scrape a URL and get content in Markdown format |
| `CrawlWebsite` | Crawl entire websites with multi-page content extraction |
| `MapWebsite` | Generate sitemap (all URLs) from any website |
| `SearchWeb` | Search the web and get results |
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
│         └────────────────┴─────────────────────┘             │
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
│   ├── Services/
│   │   ├── IApprovalHandler.cs   # Approval System
│   │   ├── IInputHandler.cs      # Input System
│   │   └── AutonomousAgentService.cs
│   ├── Sandbox/
│   │   ├── AppContainerProvider.cs  # Windows
│   │   └── BubblewrapProvider.cs    # Linux
│   ├── Security/
│   │   ├── UrlValidator.cs
│   │   ├── PathValidator.cs
│   │   └── CommandValidator.cs
│   └── Plugins/                 # Semantic Kernel Plugins
│
├── Clawleash.Shell/              # Sandbox Process
│   ├── IPC/IpcClient.cs          # IPC Client (DealerSocket)
│   └── Hosting/
│       └── ConstrainedRunspaceHost.cs  # Constrained PowerShell
│
└── Clawleash.Contracts/          # Shared Types
    └── Messages/
        ├── ShellMessages.cs      # IPC Messages
        └── Enums.cs              # Shared Enums
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
  "Browser": {
    "Headless": true
  },
  "Shell": {
    "UseSandbox": true,
    "LanguageMode": "ConstrainedLanguage"
  },
  "Security": {
    "AllowedUrls": ["https://example.com/*"],
    "AllowedPaths": ["C:\\Users\\YourName\\Documents"],
    "AllowedCommands": ["Get-*", "ConvertTo-Json"]
  }
}
```

---

## Usage

```bash
dotnet run --project Clawleash
```

### Tool Package System

```csharp
// Load all ZIPs from package directory
await toolLoader.LoadAllAsync(kernel);

// Enable hot-reload (auto-detect new ZIPs)
await toolLoader.LoadAllAsync(kernel, watchForChanges: true);
```

**Package Structure:**
```
%LocalAppData%\Clawleash\Packages\
└── MyTool.zip
    ├── tool-manifest.json  # Optional
    └── MyTool.dll          # DLL with [KernelFunction] methods
```

**tool-manifest.json:**
```json
{
  "name": "MyTool",
  "version": "1.0.0",
  "mainAssembly": "MyTool.dll",
  "description": "Custom tool"
}
```

### Example

```
👤 You: Scrape https://example.com

🤖 Clawleash:
Scraping complete:
- Title: Example Domain
- Content: This domain is for use in illustrative examples...
- Links: 2

👤 You: Show directory tree

🤖 Clawleash:
C:\Projects\MyApp
├── 📁 src/
│   └── 🔷 App.tsx
├── 📋 package.json
└── 📝 README.md

3 directories, 5 files
```

---

## Security

### Sandbox

| Platform | Implementation |
|----------|----------------|
| Windows | AppContainer (InternetClient capability) |
| Linux | Bubblewrap |

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

## IPC Communication

| Item | Specification |
|------|---------------|
| Protocol | ZeroMQ (Router/Dealer) |
| Serialization | MessagePack |
| Direction | Main (Server) ← Shell (Client) |

**Message Types:**
- `ShellExecuteRequest/Response` - Command execution
- `ToolInvokeRequest/Response` - Tool invocation
- `ShellInitializeRequest/Response` - Initialization
- `ShellPingRequest/Response` - Health check

---

## Development

```bash
# Build
dotnet build

# Test
dotnet test
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
