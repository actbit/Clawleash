<div align="center">

# 🐾 Clawleash

**OpenClow-style Autonomous AI Agent with Firecrawl-powered Web Scraping**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue?style=flat-square)](https://github.com)

*Semantic Kernel × Playwright × Autonomous Agent Framework*

English | [**日本語**](README.md)

[Features](#-features) • [Installation](#-installation) • [Configuration](#-configuration) • [Usage](#-usage) • [Architecture](#-architecture)

</div>

---

## 📋 Table of Contents

- [Overview](#-overview)
- [Features](#-features)
  - [Web Crawler (Firecrawl-style)](#-web-crawler-firecrawl-style)
  - [Browser Automation](#-browser-automation)
  - [Advanced Browser Operations](#-advanced-browser-operations)
  - [AI-Powered Data Extraction](#-ai-powered-data-extraction)
  - [Autonomous Agent](#-autonomous-agent)
  - [Security Features](#-security-features)
- [Installation](#-installation)
- [Configuration](#-configuration)
- [Usage](#-usage)
- [Architecture](#-architecture)
- [API Reference](#-api-reference)
- [Contributing](#-contributing)
- [License](#-license)

---

## 🎯 Overview

**Clawleash** is an **OpenClow-style autonomous AI agent** that combines the power of:

- **Microsoft Semantic Kernel** - AI orchestration framework
- **Playwright** - Modern browser automation
- **Autonomous Agent Framework** - Self-directed task execution

It provides **Firecrawl/OpenCraw-style web scraping** capabilities with human-like autonomous browser operation, making it perfect for:

- 🔍 Web scraping and data extraction
- 🤖 Automated browser testing
- 📊 Competitive intelligence gathering
- 📰 Content monitoring and aggregation
- 🛒 E-commerce price tracking

---

## ✨ Features

### 🌐 Web Crawler (Firecrawl-style)

| Function | Description |
|----------|-------------|
| `ScrapeUrl` | Scrape a URL and get content in LLM-ready Markdown format |
| `CrawlWebsite` | Crawl entire websites with multi-page content extraction |
| `MapWebsite` | Generate sitemap (all URLs) from any website - extremely fast |
| `SearchWeb` | Search the web and optionally scrape content from results |
| `BatchScrape` | Bulk scrape multiple URLs simultaneously |
| `GetPageMarkdown` | Convert current page HTML to clean Markdown |

#### Scraping Capabilities
- ✅ LLM-ready formats (Markdown, structured data, HTML)
- ✅ Handles dynamic content (JS-rendered pages)
- ✅ Proxy support and anti-bot mechanisms
- ✅ Custom headers and cookies
- ✅ Screenshot capture
- ✅ Link extraction

### 🖱️ Browser Automation

#### Basic Operations
```csharp
NavigateTo(url)        // Navigate to URL
ClickElement(selector) // Click elements
TypeText(selector, text) // Type into input fields
SubmitForm(selector)   // Submit forms
ExecuteJavaScript(script) // Run JavaScript
```

#### Scroll Operations
```csharp
ScrollPage(pixels)     // Scroll by pixels
ScrollToBottom()       // Scroll to page bottom
ScrollToPosition(x, y) // Scroll to specific position
ScrollIntoView(selector) // Scroll element into view
```

#### Wait Operations
```csharp
WaitForSelector(selector) // Wait for element to appear
WaitForTimeout(ms)        // Wait for specific time
WaitForPageLoad()         // Wait for page load complete
```

#### Keyboard Operations
```csharp
PressKey(key)          // Press keys (Enter, Tab, Escape, etc.)
KeyboardType(text)     // Type text with keyboard
```

#### Navigation
```csharp
ReloadPage()           // Reload current page
GoBack()               // Go back in history
GoForward()            // Go forward in history
```

#### Mouse Operations
```csharp
HoverElement(selector) // Hover over element
DoubleClick(selector)  // Double-click element
RightClick(selector)   // Right-click (context menu)
DragAndDrop(src, dst)  // Drag and drop
```

### 🔧 Advanced Browser Operations

#### Storage Operations
```csharp
GetCookies()           // Get all cookies
GetLocalStorage(key)   // Get localStorage values
SetLocalStorage(key, value) // Set localStorage
GetSessionStorage(key) // Get sessionStorage
```

#### Form Operations
```csharp
SelectOption(selector, value) // Select dropdown option
CheckElement(selector, check) // Check/uncheck checkbox
FillTextArea(selector, text)  // Fill textarea
```

#### Text Operations
```csharp
SelectText(startSelector, endSelector) // Select text range
CopySelection()        // Copy selected text (Ctrl+C)
Paste()                // Paste from clipboard (Ctrl+V)
FindAndHighlightText(text) // Find and highlight text
```

#### iframe Operations
```csharp
GetIframeContent(iframeSelector) // Get iframe HTML
ClickInIframe(iframeSel, elementSel) // Click inside iframe
```

#### Data Extraction
```csharp
ExtractTableData(selector) // Extract table as array
GetScrollPosition()    // Get current scroll position
```

### 🤖 AI-Powered Data Extraction

#### Structured Data Extraction
```csharp
// Extract any data with natural language prompt
ExtractStructuredData("Extract product name, price, and availability")

// Extract with JSON schema
ExtractWithSchema(schemaJson)

// Extract specific types
ExtractProductInfo()   // E-commerce products
ExtractArticleInfo()   // News/blog articles
ExtractContactInfo()   // Contact information
```

#### Content Analysis
```csharp
SummarizePage()        // Generate page summary
AnalyzePageContent(question) // Ask questions about page
```

### 🧠 Autonomous Agent

#### Goal Execution
```csharp
// Plan and execute goal autonomously
ExecuteGoalAutonomously("Collect all product prices from example.com")

// Just plan without execution
PlanGoal("Scrape news articles from techcrunch.com")
```

#### Execution Control
```csharp
PauseExecution()       // Pause current execution
ResumeExecution()      // Resume paused execution
CancelExecution()      // Cancel execution
GetExecutionStatus()   // Get current status
```

#### Configuration
```csharp
UpdateSettings(
    maxSteps: 20,
    requireApprovalForDeletion: true
)
GetSettings()          // Get current settings
```

#### Self-Evaluation
```csharp
EvaluateLastExecution() // Evaluate and suggest improvements
```

### 🔒 Security Features

| Feature | Description |
|---------|-------------|
| **URL Filtering** | Only whitelisted URLs are accessible |
| **Path Restrictions** | File operations limited to allowed directories |
| **Command Restrictions** | Only whitelisted PowerShell commands |
| **Sandbox Support** | Docker, AppContainer, Bubblewrap isolation |
| **Human-in-the-Loop** | Approval required for dangerous operations |

---

## 📦 Installation

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [PowerShell 7+](https://github.com/PowerShell/PowerShell) (optional, for PowerShell plugin)

### Quick Start

```bash
# Clone the repository
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash/Clawleash

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Install Playwright browsers
pwsh bin/Debug/net10.0/.playwright/package/cli.js install

# Run the agent
dotnet run
```

### Docker (Optional)

```bash
# Build Docker image
docker build -t clawleash .

# Run in container
docker run -it clawleash
```

---

## ⚙️ Configuration

### appsettings.json

```json
{
  "AI": {
    "ApiKey": "your-api-key-here",
    "ModelId": "gpt-4o",
    "Endpoint": "https://api.openai.com/v1"
  },
  "Browser": {
    "Headless": true,
    "ScreenshotOnAction": false
  },
  "Security": {
    "AllowedUrls": [
      "https://example.com/*",
      "https://api.example.com/*"
    ],
    "AllowedPaths": [
      "C:\\Users\\YourName\\Documents",
      "/home/yourname/data"
    ],
    "AllowedCommands": [
      "Get-*",
      "Set-Location",
      "Write-Output",
      "ConvertTo-Json"
    ]
  },
  "Sandbox": {
    "Enabled": false,
    "Provider": "Docker"
  }
}
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `CLAWLEASH_API_KEY` | AI API key (overrides appsettings.json) |
| `CLAWLEASH_ENDPOINT` | AI API endpoint |
| `CLAWLEASH_MODEL` | Model ID to use |

### Autonomous Agent Settings

```json
{
  "AutonomousSettings": {
    "MaxSteps": 10,
    "MaxRetries": 3,
    "RequireApprovalForDangerousOperations": true,
    "RequireApprovalForFileDeletion": true,
    "RequireApprovalForFormSubmission": true,
    "MaxAutoScrolls": 5,
    "MaxAutoCrawlPages": 10,
    "StepDelayMs": 500,
    "ProgressReportInterval": 1
  }
}
```

---

## 🚀 Usage

### Interactive CLI Mode

```bash
dotnet run
```

```
================================
   Clawleash Agent v1.0
   OpenClow-style AI Agent
================================

チャットを開始します。終了するには 'exit' または 'quit' と入力してください

👤 You: Navigate to https://example.com and take a screenshot

🤖 Clawleash:
I'll navigate to the website and take a screenshot for you.

✅ Successfully navigated to https://example.com
📸 Screenshot saved (1920x1080, 245KB)

The page shows:
- Title: "Example Domain"
- Main heading: "Example Domain"
- Content: This domain is for use in illustrative examples...

👤 You: Extract all the links from this page

🤖 Clawleash:
Found 2 links:
1. https://www.iana.org/domains/example
2. https://www.iana.org/domains/reserved

```

### Autonomous Execution Example

```
👤 You: Collect product information from shop.example.com and save to products.json

🤖 Clawleash:
## Task Plan Created

**Goal**: Collect product information from shop.example.com
**Tasks**: 5

1. [10] Navigate to shop.example.com
2. [9] Get website sitemap
3. [8] Scrape product pages (estimated 15 pages)
4. [7] Extract product data
5. [6] ⚠️ Save to products.json (requires approval)

[Progress] Step 1/5: Navigating to shop.example.com...
[Progress] Step 2/5: Getting sitemap...
[Progress] Step 3/5: Scraping product pages (5/15)...

⚠️ Approval Required:
Task: Save to products.json
Do you approve? (y/n): y

[Progress] Step 5/5: Saving data...

✅ Completed: Collected 47 products and saved to products.json
```

### Programmatic Usage

```csharp
using Clawleash.Services;
using Clawleash.Plugins;
using Microsoft.SemanticKernel;

// Create kernel
var builder = Kernel.CreateBuilder();
builder.AddOpenAIChatCompletion("gpt-4o", "api-key");
var kernel = builder.Build();

// Add plugins
var browserManager = new BrowserManager(settings, urlValidator);
kernel.Plugins.AddFromObject(new WebCrawlerPlugin(browserManager));
kernel.Plugins.AddFromObject(new BrowserActionsPlugin(browserManager));

// Execute
var result = await kernel.InvokeAsync(
    "WebCrawler",
    "ScrapeUrl",
    new() { ["url"] = "https://example.com" }
);
Console.WriteLine(result);
```

---

## 🏗️ Architecture

```
Clawleash/
│
├── 📂 Plugins/                        # Semantic Kernel Plugins
│   ├── RestrictedFileSystemPlugin.cs   # File operations (with security)
│   ├── RestrictedPowerShellPlugin.cs   # PowerShell commands (with security)
│   ├── RestrictedBrowserPlugin.cs      # Basic browser operations
│   ├── WebCrawlerPlugin.cs             # Firecrawl-style web scraping
│   ├── BrowserActionsPlugin.cs         # Advanced browser actions
│   ├── AdvancedBrowserPlugin.cs        # Cookie, storage, forms, etc.
│   ├── StructuredDataExtractionPlugin.cs # AI-powered data extraction
│   └── AutonomousAgentPlugin.cs        # Self-directed execution
│
├── 📂 Services/                        # Core Services
│   ├── BrowserManager.cs               # Playwright browser management
│   ├── MemoryManager.cs                # Short & long-term memory
│   ├── AutonomousAgentService.cs       # Autonomous execution engine
│   ├── PowerShellExecutor.cs           # PowerShell command execution
│   └── ChatInterfaceManager.cs         # Multi-interface support
│
├── 📂 Models/                          # Data Models
│   ├── BrowserState.cs                 # Current browser state
│   ├── CrawlResult.cs                  # Crawl/scrape results
│   ├── AutonomousModels.cs             # Agent task & goal models
│   └── CommandResult.cs                # Command execution results
│
├── 📂 Security/                        # Security Layer
│   ├── UrlValidator.cs                 # URL whitelist/blacklist
│   ├── PathValidator.cs                # Path access control
│   └── CommandValidator.cs             # Command restrictions
│
├── 📂 Sandbox/                         # Isolation Providers
│   ├── ISandboxProvider.cs             # Interface
│   ├── DockerSandboxProvider.cs        # Docker isolation
│   ├── AppContainerProvider.cs         # Windows AppContainer
│   └── BubblewrapProvider.cs           # Linux Bubblewrap
│
└── 📂 Configuration/                   # Configuration
    └── ClawleashSettings.cs            # Settings model
```

### Plugin System

Clawleash uses **Microsoft Semantic Kernel** plugin system:

```csharp
[KernelFunction, Description("Function description")]
public async Task<string> FunctionName(
    [Description("Parameter description")] string param)
{
    // Implementation
}
```

All functions are automatically available to the AI agent with full type safety and documentation.

---

## 📖 API Reference

### WebCrawler Plugin

| Method | Parameters | Returns |
|--------|------------|---------|
| `ScrapeUrl` | url, includeScreenshot | Markdown content, links, metadata |
| `CrawlWebsite` | startUrl, maxPages, maxDepth | List of scraped pages |
| `MapWebsite` | url, searchQuery | List of URLs |
| `SearchWeb` | query, limit, scrapeContent | Search results with content |
| `BatchScrape` | urlsJson | List of scrape results |

### BrowserActions Plugin

| Method | Parameters | Description |
|--------|------------|-------------|
| `ScrollPage` | pixels | Scroll by amount |
| `WaitForElement` | selector, timeoutMs | Wait for element |
| `PressKey` | key | Press keyboard key |
| `ExecuteActions` | actionsJson | Execute multiple actions |

### AdvancedBrowser Plugin

| Method | Parameters | Description |
|--------|------------|-------------|
| `GetCookies` | - | Get all cookies |
| `GetLocalStorage` | key | Get storage value |
| `SelectOption` | selector, value | Select dropdown |
| `DragAndDrop` | source, target | Drag and drop |
| `ExtractTableData` | selector | Extract table |

### DataExtraction Plugin

| Method | Parameters | Description |
|--------|------------|-------------|
| `ExtractStructuredData` | prompt | AI-powered extraction |
| `ExtractWithSchema` | schemaJson | Schema-based extraction |
| `ExtractProductInfo` | - | Extract e-commerce data |
| `SummarizePage` | maxLength | Generate summary |

### AutonomousAgent Plugin

| Method | Parameters | Description |
|--------|------------|-------------|
| `ExecuteGoalAutonomously` | goalDescription, maxSteps | Execute goal |
| `PlanGoal` | goalDescription | Create plan only |
| `PauseExecution` | - | Pause execution |
| `UpdateSettings` | various | Update settings |

---

## 🤝 Contributing

We welcome contributions! Please follow these steps:

1. **Fork** the repository
2. **Create** a feature branch (`git checkout -b feature/amazing-feature`)
3. **Commit** your changes (`git commit -m 'Add amazing feature'`)
4. **Push** to the branch (`git push origin feature/amazing-feature`)
5. **Open** a Pull Request

### Development Setup

```bash
# Clone your fork
git clone https://github.com/yourusername/Clowleash.git

# Install development dependencies
dotnet restore

# Run tests
dotnet test

# Build release
dotnet build -c Release
```

### Code Style
- Follow [C# coding conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable names
- Add XML documentation for public APIs
- Write unit tests for new features

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

```
MIT License

Copyright (c) 2025 Clawleash

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

---

## 🙏 Acknowledgments

- [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel) - AI orchestration
- [Playwright](https://playwright.dev/) - Browser automation
- [Firecrawl](https://github.com/mendableai/firecrawl) - Inspiration for web scraping features

---

<div align="center">

**Made with ❤️ by actbit**

English | [日本語](README.md) | [⬆ Back to Top](#-clawleash)

</div>
