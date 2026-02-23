<div align="center">

# Clawleash

**Autonomous AI Agent with Sandbox Execution**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-34%20passed-brightgreen?style=flat-square)](Clawleash.Tests)

*Semantic Kernel × Playwright × PowerShell × MCP × Sandbox Architecture*

English | [**日本語**](README.md)

</div>

---

## Overview

Clawleash is an **autonomous AI agent** that runs in a secure sandbox environment. Built on Microsoft Semantic Kernel and Playwright, it provides Firecrawl-style web scraping and autonomous browser operation.

### Key Features

- **Sandboxed Execution**: Run PowerShell/commands safely in isolated processes
- **Tool Package System**: Add tools via ZIP/DLL packages
- **Skill System**: Define and reuse prompt templates in YAML/JSON
- **MCP Client**: Integrate tools from external MCP servers
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

**Skill Directory:** `%LocalAppData%\Clawleash\Skills\`

### MCP (Model Context Protocol) Client

Use tools from external MCP servers within Clawleash.

| Function | Description |
|----------|-------------|
| `list_tools` | List tools from MCP server |
| `execute_tool` | Execute an MCP tool |

**Transport Support:**
- **stdio**: Local NPX packages, Docker containers
- **SSE**: Remote MCP servers (coming soon)

**Configuration Example (appsettings.json):**
```json
{
  "Mcp": {
    "Enabled": true,
    "Servers": [
      {
        "Name": "github",
        "Transport": "stdio",
        "Command": "npx",
        "Args": ["-y", "@modelcontextprotocol/server-github"],
        "Environment": {
          "GITHUB_TOKEN": "${GITHUB_TOKEN}"
        },
        "UseSandbox": true
      },
      {
        "Name": "filesystem",
        "Transport": "stdio",
        "Command": "docker",
        "Args": ["run", "--rm", "-i", "-v", "${WORKSPACE}:/workspace:ro", "mcp/filesystem"],
        "UseSandbox": true
      }
    ]
  }
}
```

**Security:**
- MCP servers can run in sandbox (`UseSandbox: true`)
- Docker containers for filesystem isolation
- Timeout settings to control response wait time

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
│   ├── Skills/
│   │   └── SkillLoader.cs        # Skill Loader (YAML/JSON)
│   ├── Mcp/
│   │   ├── McpClientManager.cs   # MCP Client Manager
│   │   ├── McpServerConfig.cs    # MCP Server Config
│   │   └── McpToolAdapter.cs     # Semantic Kernel Integration
│   ├── Models/
│   │   └── Skill.cs              # Skill Model Definition
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
│       ├── WebCrawlerPlugin.cs
│       ├── BrowserActionsPlugin.cs
│       ├── FileOperationsPlugin.cs
│       ├── SkillPlugin.cs        # Skill Integration
│       └── ...
│
├── Clawleash.Shell/              # Sandbox Process
│   ├── IPC/IpcClient.cs          # IPC Client (DealerSocket)
│   └── Hosting/
│       └── ConstrainedRunspaceHost.cs  # Constrained PowerShell
│
├── Clawleash.Contracts/          # Shared Types
│   └── Messages/
│       ├── ShellMessages.cs      # IPC Messages
│       └── Enums.cs              # Shared Enums
│
├── Clawleash.Tests/              # Unit Tests
│   ├── Models/
│   │   └── SkillTests.cs         # Skill parameter tests
│   ├── Skills/
│   │   └── SkillLoaderTests.cs   # YAML/JSON load tests
│   └── Mcp/
│       └── McpSettingsTests.cs   # MCP settings tests
│
└── sample-skills/                # Sample Skills
    ├── summarize.skill.yaml
    ├── translate.skill.yaml
    ├── code-review.skill.yaml
    └── explain.skill.yaml
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
  },
  "Mcp": {
    "Enabled": true,
    "DefaultTimeoutMs": 30000,
    "Servers": []
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

### Adding Skills

```
%LocalAppData%\Clawleash\Skills\
└── my-skill.skill.yaml       # YAML format
└── my-skill.skill.json       # or JSON format
```

Hot-reload enabled: New skill files are automatically loaded when placed in the directory.

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

👤 You: Summarize this text using the summarize skill

🤖 Clawleash:
[Auto-calls execute_skill]
Summary: ...
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

### MCP Server Security

- **Sandboxed Execution**: `UseSandbox: true` for isolated process execution
- **Timeout Control**: `TimeoutMs` to limit response wait time
- **Disableable**: `Enabled: false` to disable MCP functionality

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

# Run tests
dotnet test

# Verbose test output
dotnet test --verbosity normal
```

### Test Coverage

| Category | Tests | Description |
|----------|-------|-------------|
| Models | 9 | Skill parameter replacement, JsonElement handling |
| Skills | 15 | YAML/JSON loading, file watching, tag filtering |
| Mcp | 10 | Settings deserialization, initialization, timeout |

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
