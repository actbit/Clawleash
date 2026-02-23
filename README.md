<div align="center">

# Clawleash

**自律AIエージェント - サンドボックス実行環境搭載**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-34%20passed-brightgreen?style=flat-square)](Clawleash.Tests)

*Semantic Kernel × Playwright × PowerShell × MCP × Sandbox Architecture*

[**English**](README-en.md) | 日本語

</div>

---

## 概要

Clawleash（クラウリッシュ）は、**安全なサンドボックス環境で動作する自律AIエージェント**です。Microsoft Semantic KernelとPlaywrightをベースに、Firecrawl風のWebスクレイピング機能と自律的なブラウザ操作を実現します。

### 特徴

- **サンドボックス実行**: PowerShell/コマンドを分離プロセスで安全に実行
- **ツールパッケージシステム**: ZIP/DLLでツールを追加可能
- **スキルシステム**: YAML/JSONでプロンプトテンプレートを定義・再利用
- **MCPクライアント**: 外部MCPサーバーのツールを統合利用
- **承認システム**: 危険な操作にはユーザー承認が必要
- **マルチプラットフォーム**: Windows (AppContainer) / Linux (Bubblewrap)

---

## 主な機能

### Webクローラー（Firecrawl風）

| 機能 | 説明 |
|------|------|
| `ScrapeUrl` | URLをスクレイプしてMarkdown形式でコンテンツを取得 |
| `CrawlWebsite` | Webサイト全体をクロールして複数ページのコンテンツを取得 |
| `MapWebsite` | サイトマップ（全URL一覧）を高速に取得 |
| `SearchWeb` | Webを検索（DuckDuckGo使用・APIキー不要） |
| `BatchScrape` | 複数のURLを一括スクレイプ |

### ファイル操作

| 機能 | 説明 |
|------|------|
| `CreateFile` / `ReadFile` | ファイルの作成・読み込み |
| `ReplaceLine` / `ReplaceText` | 行・テキストの置換 |
| `InsertLine` / `DeleteLine` | 行の挿入・削除 |
| `MoveFile` / `CopyFile` | ファイルの移動・コピー |
| `CreateFolder` / `DeleteFolder` | フォルダの作成・削除 |
| `ShowTree` | ディレクトリ構造をツリー形式で表示 |

### ブラウザ操作

- **基本操作**: ナビゲート、クリック、テキスト入力、フォーム送信
- **スクロール**: ページスクロール、最下部への移動
- **待機操作**: 要素表示待機、時間待機、ページ読み込み待機
- **キーボード**: Enter, Tab, Escape, 矢印キーなど
- **マウス操作**: ダブルクリック、右クリック、ドラッグ＆ドロップ
- **ストレージ**: Cookie、localStorage、sessionStorage

### AI搭載データ抽出

- `ExtractStructuredData`: AIを使った構造化データ抽出
- `ExtractProductInfo`: 商品情報の自動抽出
- `SummarizePage`: ページ内容の要約

### 自律エージェント

- **目標の計画・実行**: 目標を設定して、AIが自動的にタスクを分解・実行
- **自己評価・修正**: 実行結果を評価して、失敗時は別のアプローチを試行
- **Human-in-the-Loop**: 危険な操作にはユーザーの承認が必要

### スキルシステム

プロンプトテンプレートを再利用可能な「スキル」として定義・実行できます。

| 機能 | 説明 |
|------|------|
| `list_skills` | 利用可能なスキル一覧を表示 |
| `execute_skill` | 指定したスキルを実行 |
| `show_skill` | スキルの詳細情報を表示 |
| `register_skill` | 新しいスキルを登録（YAML/JSON） |
| `remove_skill` | スキルを削除 |

**スキル定義例（YAML）:**
```yaml
name: summarize
description: テキストを要約します
version: "1.0.0"
tags: [text, summarization]

systemInstruction: |
  あなたは専門的な要約アシスタントです。

parameters:
  - name: text
    type: string
    description: 要約するテキスト
    required: true
  - name: style
    type: string
    description: 要約スタイル
    required: false
    default: 簡潔
    enum: [簡潔, 詳細, 箇条書き]

prompt: |
  以下のテキストを{{style}}に要約してください：
  {{text}}
```

**スキルディレクトリ:** `%LocalAppData%\Clawleash\Skills\`

### MCP (Model Context Protocol) クライアント

外部MCPサーバーのツールをClawleash内で利用できます。

| 機能 | 説明 |
|------|------|
| `list_tools` | MCPサーバーのツール一覧を表示 |
| `execute_tool` | MCPツールを実行 |

**トランスポート対応:**
- **stdio**: ローカルNPXパッケージ、Dockerコンテナ
- **SSE**: リモートMCPサーバー（今後対応）

**設定例 (appsettings.json):**
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

**セキュリティ:**
- MCPサーバーはサンドボックス内で実行可能（`UseSandbox: true`）
- Dockerコンテナを使用してファイルシステムを分離
- タイムアウト設定でレスポンス待機時間を制御

---

## アーキテクチャ

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

## プロジェクト構成

```
Clowleash/
├── Clawleash/                    # メインアプリケーション
│   ├── Execution/
│   │   └── ShellServer.cs        # IPCサーバー
│   ├── Tools/
│   │   ├── ToolLoader.cs         # ツールローダー
│   │   ├── ToolPackage.cs        # パッケージ管理
│   │   ├── ToolProxyGenerator.cs # プロキシ生成 (Reflection.Emit)
│   │   └── ShellToolExecutor.cs  # IPC経由実行
│   ├── Skills/
│   │   └── SkillLoader.cs        # スキルローダー (YAML/JSON)
│   ├── Mcp/
│   │   ├── McpClientManager.cs   # MCPクライアント管理
│   │   ├── McpServerConfig.cs    # MCPサーバー設定
│   │   └── McpToolAdapter.cs     # Semantic Kernel統合
│   ├── Models/
│   │   └── Skill.cs              # スキルモデル定義
│   ├── Services/
│   │   ├── IApprovalHandler.cs   # 承認システム
│   │   ├── IInputHandler.cs      # 入力システム
│   │   └── AutonomousAgentService.cs
│   ├── Sandbox/
│   │   ├── AppContainerProvider.cs  # Windows
│   │   └── BubblewrapProvider.cs    # Linux
│   ├── Security/
│   │   ├── UrlValidator.cs
│   │   ├── PathValidator.cs
│   │   └── CommandValidator.cs
│   └── Plugins/                 # Semantic Kernel プラグイン
│       ├── WebCrawlerPlugin.cs
│       ├── BrowserActionsPlugin.cs
│       ├── FileOperationsPlugin.cs
│       ├── SkillPlugin.cs        # スキル統合
│       └── ...
│
├── Clawleash.Shell/              # サンドボックスプロセス
│   ├── IPC/IpcClient.cs          # IPCクライアント (DealerSocket)
│   └── Hosting/
│       └── ConstrainedRunspaceHost.cs  # 制約付きPowerShell
│
├── Clawleash.Contracts/          # 共有型定義
│   └── Messages/
│       ├── ShellMessages.cs      # IPCメッセージ
│       └── Enums.cs              # 共有Enum
│
├── Clawleash.Tests/              # ユニットテスト
│   ├── Models/
│   │   └── SkillTests.cs         # スキルパラメータ置換テスト
│   ├── Skills/
│   │   └── SkillLoaderTests.cs   # YAML/JSONロードテスト
│   └── Mcp/
│       └── McpSettingsTests.cs   # MCP設定テスト
│
└── sample-skills/                # サンプルスキル
    ├── summarize.skill.yaml
    ├── translate.skill.yaml
    ├── code-review.skill.yaml
    └── explain.skill.yaml
```

---

## インストール

```bash
# リポジトリをクローン
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash

# 依存関係を復元
dotnet restore

# Playwrightブラウザをインストール
pwsh bin/Debug/net10.0/.playwright/package/cli.js install
```

---

## 設定

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

## 使用方法

```bash
dotnet run --project Clawleash
```

### ツールパッケージの追加

```csharp
// パッケージディレクトリのZIPを一括ロード
await toolLoader.LoadAllAsync(kernel);

// ホットリロード有効（新規ZIPを自動認識）
await toolLoader.LoadAllAsync(kernel, watchForChanges: true);
```

**パッケージ構成:**
```
%LocalAppData%\Clawleash\Packages\
└── MyTool.zip
    ├── tool-manifest.json  # オプション
    └── MyTool.dll          # [KernelFunction]メソッドを持つDLL
```

**tool-manifest.json:**
```json
{
  "name": "MyTool",
  "version": "1.0.0",
  "mainAssembly": "MyTool.dll",
  "description": "カスタムツール"
}
```

### スキルの追加

```
%LocalAppData%\Clawleash\Skills\
└── my-skill.skill.yaml       # YAML形式
└── my-skill.skill.json       # またはJSON形式
```

ホットリロード対応：新しいスキルファイルを配置すると自動的に読み込まれます。

### 使用例

```
👤 ユーザー: https://example.com をスクレイピングして

🤖 Clawleash:
スクレイピング完了:
- タイトル: Example Domain
- コンテンツ: This domain is for use in illustrative examples...
- リンク: 2件

👤 ユーザー: 現在のディレクトリのツリーを表示して

🤖 Clawleash:
C:\Projects\MyApp
├── 📁 src/
│   └── 🔷 App.tsx
├── 📋 package.json
└── 📝 README.md

3 ディレクトリ, 5 ファイル

👤 ユーザー: summarizeスキルでこの文章を要約して

🤖 Clawleash:
[execute_skill を自動呼び出し]
要約: ...
```

---

## セキュリティ

### サンドボックス

| プラットフォーム | 実装 |
|-----------------|------|
| Windows | AppContainer (InternetClient capability) |
| Linux | Bubblewrap |

### PowerShell制約

- **ConstrainedLanguage**: デフォルトの安全なモード
- **コマンドホワイトリスト**: 許可されたコマンドのみ実行
- **パス制限**: 許可されたパスのみアクセス可能

### MCPサーバーのセキュリティ

- **サンドボックス実行**: `UseSandbox: true` で分離プロセス実行
- **タイムアウト制御**: `TimeoutMs` でレスポンス待機時間を制限
- **無効化可能**: `Enabled: false` でMCP機能を無効化

### 承認システム

```csharp
// CLI用（コンソールで承認確認）
services.AddCliApprovalHandler();

// 自動化用（ルールベース）
services.AddSilentApprovalHandler(config);
```

---

## IPC通信

| 項目 | 仕様 |
|------|------|
| プロトコル | ZeroMQ (Router/Dealer) |
| シリアライズ | MessagePack |
| 方向 | Main (Server) ← Shell (Client) |

**メッセージ種別:**
- `ShellExecuteRequest/Response` - コマンド実行
- `ToolInvokeRequest/Response` - ツール呼び出し
- `ShellInitializeRequest/Response` - 初期化
- `ShellPingRequest/Response` - 死活監視

---

## 開発

```bash
# ビルド
dotnet build

# テスト実行
dotnet test

# テスト詳細表示
dotnet test --verbosity normal
```

### テストカバレッジ

| カテゴリ | テスト数 | 内容 |
|---------|---------|------|
| Models | 9 | Skillパラメータ置換、JsonElement処理 |
| Skills | 15 | YAML/JSONロード、ファイル監視、タグフィルタ |
| Mcp | 10 | 設定デシリアライズ、初期化、タイムアウト |

---

## コントリビュート

1. このリポジトリをフォーク
2. フィーチャーブランチを作成 (`git checkout -b feature/amazing-feature`)
3. 変更をコミット (`git commit -m 'Add amazing feature'`)
4. プルリクエストを作成

---

## ライセンス

MIT License - [LICENSE](LICENSE) を参照

---

<div align="center">

**Made with ❤️ by actbit**

[English Version](README-en.md) | [⬆ トップに戻る](#clawleash)

</div>
