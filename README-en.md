<div align="center">

# Clawleash

**Autonomous AI Agent with Sandbox Execution**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

*Semantic Kernel × PowerShell × Sandbox Architecture*

English | [**日本語**](README.md)

</div>

---

## Overview

Clawleash is an autonomous AI agent framework that runs in a secure sandbox environment.

### Key Features

- **Sandboxed Execution**: Run PowerShell/commands safely in isolated processes
- **Tool Package System**: Add tools via ZIP/DLL packages
- **Approval System**: User approval required for dangerous operations
- **Multi-Platform**: Windows (AppContainer) / Linux (Bubblewrap)

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
│   │   ├── ToolProxyGenerator.cs # Proxy Generation
│   │   └── ShellToolExecutor.cs  # IPC Execution
│   ├── Services/
│   │   ├── IApprovalHandler.cs   # Approval System
│   │   ├── IInputHandler.cs      # Input System
│   │   └── AutonomousAgentService.cs
│   ├── Sandbox/
│   │   ├── AppContainerProvider.cs  # Windows
│   │   └── BubblewrapProvider.cs    # Linux
│   └── Plugins/                 # Semantic Kernel Plugins
│
├── Clawleash.Shell/              # Sandbox Process
│   ├── IPC/IpcClient.cs          # IPC Client
│   └── Hosting/
│       └── ConstrainedRunspaceHost.cs  # Constrained PowerShell
│
└── Clawleash.Contracts/          # Shared Types
    └── Messages/
        ├── ShellMessages.cs      # IPC Messages
        └── Enums.cs              # Shared Enums
```

## Installation

```bash
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash
dotnet restore
```

## Configuration

`appsettings.json`:

```json
{
  "AI": {
    "ApiKey": "your-api-key",
    "ModelId": "gpt-4o",
    "Endpoint": "https://api.openai.com/v1"
  },
  "Shell": {
    "UseSandbox": true,
    "LanguageMode": "ConstrainedLanguage"
  }
}
```

## Usage

```bash
dotnet run --project Clawleash
```

### Tool Package System

```csharp
// Load all ZIPs from package directory
await toolLoader.LoadAllAsync(kernel);

// Enable hot-reload
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
// For CLI
services.AddCliApprovalHandler();

// For automation (rule-based)
services.AddSilentApprovalHandler(config);
```

## IPC Communication

- **Protocol**: ZeroMQ (Router/Dealer)
- **Serialization**: MessagePack
- **Direction**: Main (Server) ← Shell (Client)

## Development

```bash
# Build
dotnet build

# Test
dotnet test
```

## License

MIT License - See [LICENSE](LICENSE)
