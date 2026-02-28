<div align="center">

# Clawleash

**自律AIエージェント - サンドボックス実行環境搭載**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-58%20passed-brightgreen?style=flat-square)](Clawleash.Tests)

*Semantic Kernel × Playwright × PowerShell × MCP × Sandbox Architecture × Multi-Interface*

[**English**](README-en.md) | 日本語

</div>

---

## 概要

Clawleash（クラウリッシュ）は、**安全なサンドボックス環境で動作する自律AIエージェント**です。Microsoft Semantic KernelとPlaywrightをベースに、Firecrawl風のWebスクレイピング機能と自律的なブラウザ操作を実現します。

### 特徴

- **マルチインターフェース**: CLI / Discord / Slack / WebSocket / WebRTC に対応
- **E2EE対応**: WebSocket・WebRTCでエンドツーエンド暗号化
- **サンドボックス実行**: AppContainer (Windows) / Bubblewrap (Linux) で安全に実行
- **フォルダーポリシー**: ディレクトリごとにアクセス権限・ネットワーク制御を設定
- **ツールパッケージシステム**: ZIP/DLLでツールを追加可能
- **スキルシステム**: YAML/JSONでプロンプトテンプレートを定義・再利用
- **MCPクライアント**: 外部MCPサーバーのツールを統合利用
- **承認システム**: 危険な操作にはユーザー承認が必要

---

## 主な機能

### マルチインターフェース

Clawleashは複数の入力インターフェースを同時にサポートします。

| インターフェース | 説明 | E2EE |
|----------------|------|------|
| **CLI** | 標準コンソール入力（ビルトイン） | - |
| **Discord** | Discord Bot経由でメッセージ受信 | - |
| **Slack** | Slack Bot (HTTP API + ポーリング) | - |
| **WebSocket** | SignalRによるリアルタイム通信 | ✅ AES-256-GCM |
| **WebRTC** | DataChannel経由のP2P通信 | ✅ DTLS-SRTP |

**アーキテクチャ:**
```
┌─────────────────────────────────────────────────────────────────────┐
│                       Clawleash (Main Application)                   │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │     InterfaceLoader + FileSystemWatcher (Hot Reload)            │ │
│  │  %LocalAppData%\Clawleash\Interfaces\ を監視                     │ │
│  │  新規DLL追加 → 自動ロード → ChatInterfaceManagerに登録           │ │
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

**設定例 (appsettings.json):**
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

### E2EE（エンドツーエンド暗号化）

WebSocket・WebRTC通信でE2EEを有効にできます。

```
┌──────────────┐                      ┌──────────────┐
│   Client     │                      │    Server    │
│              │                      │              │
│  1. 鍵交換    │ ◄─── X25519 ────────► │              │
│              │                      │              │
│  2. 暗号化    │                      │              │
│  Plaintext   │                      │              │
│     │        │                      │              │
│     ▼        │                      │              │
│  AES-256-GCM │                      │              │
│     │        │                      │              │
│     ▼        │                      │              │
│  Ciphertext  │ ──── wss:// ────────► │  3. 復号化    │
│              │                      │  AES-256-GCM │
│              │                      │     │        │
│              │                      │     ▼        │
│              │                      │  Plaintext   │
└──────────────┘                      └──────────────┘
```

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
- **SSE**: リモートMCPサーバー（HTTP Server-Sent Events）

---

## セキュリティ

### サンドボックス

| プラットフォーム | 実装 | 機能 |
|-----------------|------|------|
| Windows | AppContainer | ケーパビリティベースのアクセス制御 |
| Linux | Bubblewrap | 名前空間分離 |

### AppContainerケーパビリティ

Windows AppContainerでプロセスに付与するケーパビリティを設定できます。

```json
{
  "Sandbox": {
    "Type": "AppContainer",
    "AppContainerName": "Clawleash.Sandbox",
    "Capabilities": "InternetClient, PrivateNetworkClientServer"
  }
}
```

**利用可能なケーパビリティ:**

| ケーパビリティ | 説明 |
|--------------|------|
| `InternetClient` | インターネットへの送信接続 |
| `InternetClientServer` | インターネットへの着信・送信接続 |
| `PrivateNetworkClientServer` | プライベートネットワークへの接続 |
| `PicturesLibrary` | 画像ライブラリへのアクセス |
| `VideosLibrary` | ビデオライブラリへのアクセス |
| `MusicLibrary` | ミュージックライブラリへのアクセス |
| `DocumentsLibrary` | ドキュメントライブラリへのアクセス |
| `EnterpriseAuthentication` | 企業認証 |
| `SharedUserCertificates` | 共有証明書 |
| `RemovableStorage` | リムーバブルストレージ |
| `Appointments` | 予定 |
| `Contacts` | 連絡先 |

### フォルダーポリシー

ディレクトリごとに詳細なアクセス制御を設定できます。より具体的なパスが優先され、子フォルダーで親の設定を上書き可能です。

```json
{
  "Sandbox": {
    "FolderPolicies": [
      {
        "Path": "C:\\Projects",
        "Access": "ReadWrite",
        "Network": "Allow",
        "Execute": "Allow",
        "Name": "プロジェクトフォルダー"
      },
      {
        "Path": "C:\\Projects\\Secrets",
        "Access": "Deny",
        "Network": "Deny",
        "Name": "機密情報（アクセス禁止）"
      },
      {
        "Path": "C:\\Projects\\Public",
        "Access": "ReadOnly",
        "Network": "Allow",
        "Name": "公開エリア（読み取り専用）"
      },
      {
        "Path": "C:\\Work",
        "Access": "ReadWrite",
        "Network": "Allow",
        "DeniedExtensions": ["exe", "bat", "ps1"],
        "MaxFileSizeMB": 50,
        "Name": "作業フォルダー"
      },
      {
        "Path": "C:\\Work\\Downloads",
        "Access": "ReadWrite",
        "Network": "Allow",
        "Execute": "Deny",
        "EnableAudit": true,
        "Name": "ダウンロード（実行禁止・監査あり）"
      }
    ]
  }
}
```

**ポリシープロパティ:**

| プロパティ | 値 | 説明 |
|-----------|-----|------|
| `Access` | `Deny` / `ReadOnly` / `ReadWrite` / `FullControl` | ファイルシステムアクセス |
| `Network` | `Inherit` / `Allow` / `Deny` | ネットワークアクセス |
| `Execute` | `Inherit` / `Allow` / `Deny` | プロセス実行権限 |
| `AllowedExtensions` | `[".txt", ".json"]` | 許可する拡張子 |
| `DeniedExtensions` | `[".exe", ".bat"]` | 禁止する拡張子 |
| `MaxFileSizeMB` | `10` | 最大ファイルサイズ |
| `EnableAudit` | `true` | アクセスログ記録 |

**継承ルール:**
```
C:\Projects          → ReadWrite, Network=Allow
  ├─ \Secrets        → Deny, Network=Deny        ← 親を上書き（無効化）
  ├─ \Public         → ReadOnly, Network=Allow  ← 読み取り専用に変更
  └─ \Data
       └─ \Sensitive → Deny                      ← 深い階層でも上書き可能
```

### PowerShell制約

- **ConstrainedLanguage**: デフォルトの安全なモード
- **コマンドホワイトリスト**: 許可されたコマンドのみ実行
- **パス制限**: 許可されたパスのみアクセス可能

### 承認システム

```csharp
// CLI用（コンソールで承認確認）
services.AddCliApprovalHandler();

// 自動化用（ルールベース）
services.AddSilentApprovalHandler(config);
```

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
│   ├── Services/
│   │   ├── IApprovalHandler.cs   # 承認システム
│   │   ├── IInputHandler.cs      # 入力システム
│   │   ├── InterfaceLoader.cs    # インターフェース動的ロード
│   │   ├── ChatInterfaceManager.cs # マルチインターフェース管理
│   │   └── CliChatInterface.cs   # CLIインターフェース
│   ├── Sandbox/
│   │   ├── AppContainerProvider.cs  # Windows (ケーパビリティ対応)
│   │   ├── BubblewrapProvider.cs    # Linux
│   │   ├── FolderPolicy.cs          # フォルダーポリシー定義
│   │   ├── FolderPolicyManager.cs   # ポリシー管理・継承処理
│   │   ├── NativeMethods.cs         # P/Invoke定義
│   │   └── AclManager.cs            # ACL管理
│   ├── Security/
│   │   ├── UrlValidator.cs
│   │   ├── PathValidator.cs
│   │   └── CommandValidator.cs
│   └── Plugins/                 # Semantic Kernel プラグイン
│       ├── WebCrawlerPlugin.cs
│       ├── BrowserActionsPlugin.cs
│       ├── FileOperationsPlugin.cs
│       ├── SkillPlugin.cs
│       └── ...
│
├── Clawleash.Shell/              # サンドボックスプロセス
│   ├── IPC/IpcClient.cs          # IPCクライアント (DealerSocket)
│   └── Hosting/
│       └── ConstrainedRunspaceHost.cs  # 制約付きPowerShell
│
├── Clawleash.Abstractions/       # 共有インターフェース
│   ├── Services/
│   │   ├── IChatInterface.cs     # チャットインターフェース
│   │   └── ChatMessageReceivedEventArgs.cs
│   └── Security/
│       └── IE2eeProvider.cs      # E2EEプロバイダー
│
├── Clawleash.Interfaces.Discord/ # Discord Bot インターフェース
│   ├── DiscordChatInterface.cs
│   └── DiscordSettings.cs
│
├── Clawleash.Interfaces.Slack/   # Slack Bot インターフェース
│   ├── SlackChatInterface.cs
│   └── SlackSettings.cs
│
├── Clawleash.Interfaces.WebSocket/ # WebSocket インターフェース (E2EE)
│   ├── WebSocketChatInterface.cs
│   ├── Security/
│   │   └── AesGcmE2eeProvider.cs
│   └── WebSocketSettings.cs
│
├── Clawleash.Interfaces.WebRTC/  # WebRTC インターフェース (E2EE)
│   ├── WebRtcChatInterface.cs
│   ├── Security/
│   │   └── WebRtcE2eeProvider.cs
│   └── WebRtcSettings.cs
│
├── Clawleash.Server/             # SignalRサーバー (WebSocket/WebRTC)
│   ├── Hubs/
│   │   ├── ChatHub.cs            # チャットハブ (E2EE対応)
│   │   └── SignalingHub.cs       # WebRTCシグナリング
│   ├── Security/
│   │   ├── KeyManager.cs         # 鍵管理
│   │   └── E2eeMiddleware.cs     # E2EEミドルウェア
│   └── Client/                   # Svelteフロントエンド
│
├── Clawleash.Contracts/          # 共有型定義
│   └── Messages/
│       ├── ShellMessages.cs      # IPCメッセージ
│       └── Enums.cs              # 共有Enum
│
├── Clawleash.Tests/              # ユニットテスト
│
└── sample-skills/                # サンプルスキル
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

## 使用方法

### メインアプリケーション

```bash
dotnet run --project Clawleash
```

### SignalRサーバー（WebSocket/WebRTC用）

```bash
dotnet run --project Clawleash.Server
# http://localhost:5000 でサーバー起動
# /chat - WebSocketハブ
# /signaling - WebRTCシグナリングハブ
```

### インターフェースDLLの追加

```
%LocalAppData%\Clawleash\Interfaces\
├── Discord\
│   ├── Clawleash.Interfaces.Discord.dll
│   └── Discord.Net.dll
├── Slack\
│   ├── Clawleash.Interfaces.Slack.dll
│   └── (依存DLL)
├── WebSocket\
│   └── Clawleash.Interfaces.WebSocket.dll
└── WebRTC\
    └── Clawleash.Interfaces.WebRTC.dll
```

ホットリロード対応：新しいDLLを配置すると自動的に読み込まれます。

### カスタムプロバイダーの作成

独自のチャットインターフェースを作成して追加できます。

**手順:**
1. `Clawleash.Abstractions` を参照したクラスライブラリプロジェクトを作成
2. `IChatInterface` を実装
3. ビルドして `%LocalAppData%\Clawleash\Interfaces\` に配置

詳細な開発ガイドは [Clawleash.Abstractions/README.md](Clawleash.Abstractions/README.md) を参照してください。

**実装例:**
- [Discord](Clawleash.Interfaces.Discord) - Discord Bot
- [Slack](Clawleash.Interfaces.Slack) - Slack Bot
- [WebSocket](Clawleash.Interfaces.WebSocket) - WebSocket (E2EE)
- [WebRTC](Clawleash.Interfaces.WebRTC) - WebRTC (E2EE)

### スキルの追加

```
%LocalAppData%\Clawleash\Skills\
└── my-skill.skill.yaml       # YAML形式
└── my-skill.skill.json       # またはJSON形式
```

---

## IPC通信

Main プロセスと Shell プロセス間の通信には ZeroMQ + MessagePack を使用します。

### 通信仕様

| 項目 | 仕様 |
|------|------|
| ライブラリ | NetMQ (ZeroMQ .NET実装) |
| パターン | Router/Dealer |
| シリアライズ | MessagePack (Union属性によるポリモーフィズム) |
| トランスポート | TCP (localhost) |
| 方向 | Main (Router/Server) ↔ Shell (Dealer/Client) |

### アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                    Main プロセス                              │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              RouterSocket (Server)                       │ │
│  │  - ランダムポートでバインド                               │ │
│  │  - 複数クライアント接続可能                               │ │
│  │  - Identity でクライアント識別                            │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                            │
                    ZeroMQ (TCP)
                            │
┌─────────────────────────────────────────────────────────────┐
│                   Shell プロセス (Sandboxed)                  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              DealerSocket (Client)                       │ │
│  │  - 動的に割り当てられたIdentity                          │ │
│  │  - 非同期リクエスト/レスポンス                            │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 接続フロー

```
Main                                Shell
  │                                   │
  │  1. RouterSocket.BindRandomPort  │
  │     (例: tcp://127.0.0.1:5555)   │
  │                                   │
  │                     2. プロセス起動 --server "tcp://..."
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
  │         準備完了                   │
```

### メッセージ一覧

#### 基本メッセージ（全メッセージ共通）

```csharp
public abstract class ShellMessage
{
    public string MessageId { get; set; }      // 一意識別子
    public DateTime Timestamp { get; set; }    // UTC タイムスタンプ
}
```

#### 1. ShellReadyMessage (Shell → Main)

接続完了時に送信される準備完了通知。

```csharp
public class ShellReadyMessage : ShellMessage
{
    public int ProcessId { get; set; }        // Shell プロセス ID
    public string Runtime { get; set; }       // .NET ランタイム情報
    public string OS { get; set; }            // OS 情報
}
```

#### 2. ShellInitializeRequest/Response (Main ↔ Shell)

Shell 実行環境の初期化。

**Request:**
```csharp
public class ShellInitializeRequest : ShellMessage
{
    public string[] AllowedCommands { get; set; }    // 許可コマンド
    public string[] AllowedPaths { get; set; }       // 読み書き許可パス
    public string[] ReadOnlyPaths { get; set; }      // 読み取り専用パス
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

PowerShell コマンドの実行。

**Request:**
```csharp
public class ShellExecuteRequest : ShellMessage
{
    public string Command { get; set; }              // 実行コマンド
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
    public string Output { get; set; }              // 標準出力
    public string? Error { get; set; }              // エラーメッセージ
    public int ExitCode { get; set; }
    public Dictionary<string, object?> Metadata { get; set; }
}
```

#### 4. ToolInvokeRequest/Response (Main ↔ Shell)

ツールパッケージのメソッド呼び出し。

**Request:**
```csharp
public class ToolInvokeRequest : ShellMessage
{
    public string ToolName { get; set; }            // ツール名
    public string MethodName { get; set; }          // メソッド名
    public object?[] Arguments { get; set; }        // 引数
}
```

**Response:**
```csharp
public class ToolInvokeResponse : ShellMessage
{
    public string RequestId { get; set; }
    public bool Success { get; set; }
    public object? Result { get; set; }             // 戻り値
    public string? Error { get; set; }
}
```

#### 5. ShellPingRequest/Response (Main ↔ Shell)

死活監視・レイテンシ測定。

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
    public long ProcessingTimeMs { get; set; }      // 処理時間
}
```

#### 6. ShellShutdownRequest/Response (Main ↔ Shell)

Shell プロセスのシャットダウン。

**Request:**
```csharp
public class ShellShutdownRequest : ShellMessage
{
    public bool Force { get; set; }                 // 強制終了フラグ
}
```

**Response:**
```csharp
public class ShellShutdownResponse : ShellMessage
{
    public bool Success { get; set; }
}
```

### MessagePack シリアライズ

```csharp
// Union 属性でポリモーフィック デシリアライズ
[MessagePackObject]
[Union(0, typeof(ShellExecuteRequest))]
[Union(1, typeof(ShellExecuteResponse))]
[Union(2, typeof(ShellInitializeRequest))]
// ...
public abstract class ShellMessage { ... }

// シリアライズ
var data = MessagePackSerializer.Serialize(request);

// デシリアライズ
var message = MessagePackSerializer.Deserialize<ShellMessage>(data);
```

### エラーハンドリング

```csharp
try
{
    var response = await shellServer.ExecuteAsync(request);
    if (!response.Success)
    {
        // コマンド実行失敗
        Console.WriteLine($"Error: {response.Error}");
    }
}
catch (TimeoutException)
{
    // Shell からの応答なし
}
catch (InvalidOperationException)
{
    // Shell に接続されていない
}
```

### タイムアウト設定

```csharp
var options = new ShellServerOptions
{
    StartTimeoutMs = 10000,           // 起動タイムアウト
    CommunicationTimeoutMs = 30000,   // 通信タイムアウト
    Verbose = true                    // 詳細ログ
};
```

---

## 拡張機能の開発

### MCPサーバーの追加

外部MCPサーバーを追加して、そのツールをClawleash内で使用できます。

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

**MCP設定プロパティ:**

| プロパティ | 説明 | 必須 |
|-----------|------|------|
| `name` | サーバー名（一意識別子） | ✅ |
| `transport` | `stdio` または `sse` | ✅ |
| `command` | 実行コマンド（stdio用） | stdio時 |
| `args` | コマンド引数 | |
| `environment` | 環境変数 | |
| `url` | サーバーURL（SSE用） | SSE時 |
| `headers` | HTTPヘッダー（SSE用） | |
| `enabled` | 有効/無効 | |
| `timeoutMs` | タイムアウト（ミリ秒） | |
| `useSandbox` | サンドボックスで実行 | |

**使用可能なツールの確認:**

```
ユーザー: MCPサーバーのツール一覧を表示して
AI: list_tools ツールを実行してツール一覧を表示
```

### ツールパッケージの作成

ネイティブなFunction CallingライブラリをZIPパッケージとして追加できます。

**1. プロジェクト作成**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.SemanticKernel.Core" Version="1.*" />
  </ItemGroup>
</Project>
```

**2. ツールクラスの実装**

```csharp
using Microsoft.SemanticKernel;

namespace MyTools;

public class WeatherTools
{
    [KernelFunction("get_weather")]
    [Description("指定した都市の天気を取得します")]
    public async Task<string> GetWeatherAsync(
        [Description("都市名")] string city,
        [Description("単位（celsius/fahrenheit）")] string unit = "celsius")
    {
        // 天気取得ロジック
        return $"{city}の天気: 晴れ, 気温: 25{unit}";
    }

    [KernelFunction("get_forecast")]
    [Description("天気予報を取得します")]
    public async Task<string> GetForecastAsync(
        [Description("都市名")] string city,
        [Description("予報日数（1-7）")] int days = 3)
    {
        return $"{city}の{days}日間予報: 晴れ→曇り→雨";
    }
}
```

**3. マニフェスト作成（tool-manifest.json）**

```json
{
  "name": "WeatherTools",
  "version": "1.0.0",
  "description": "天気情報ツール",
  "mainAssembly": "MyTools.dll",
  "dependencies": []
}
```

**4. パッケージ作成**

```bash
# ZIPパッケージを作成
zip -r WeatherTools.zip MyTools.dll tool-manifest.json

# 配置
cp WeatherTools.zip "%LocalAppData%\Clawleash\Packages\"
```

**パッケージ構成:**

```
WeatherTools.zip
├── tool-manifest.json     # マニフェスト
├── MyTools.dll            # メインアセンブリ
└── (依存DLL)              # 必要に応じて
```

**ホットリロード:** PackagesディレクトリにZIPを配置すると自動的にロードされます。

#### ネイティブツールパッケージ vs MCP

| 項目 | ネイティブツールパッケージ | MCP |
|------|--------------------------|-----|
| **実行環境** | サンドボックス内で直接実行 | 外部プロセス |
| **アクセス制御** | AppContainer + フォルダーポリシー | MCPサーバー側で制御 |
| **ネットワーク** | ケーパビリティで制御 | MCPサーバー次第 |
| **プロセス分離** | Shellプロセス内で分離 | 完全に別プロセス |
| **監査** | 詳細ログ可能 | MCPサーバー依存 |
| **デプロイ** | ZIPで簡単配置 | MCPサーバー構築必要 |

**ネイティブツールパッケージが推奨されるケース:**
- 社内ツール・機密データを扱うツール
- 厳密なアクセス制御が必要な場合
- 監査ログが必須な環境
- ネットワークアクセスを制限したい場合

**MCPが推奨されるケース:**
- 既存のMCPサーバーを利用したい場合
- 外部サービスとの連携
- コミュニティ提供のツールを使用したい場合

### サンドボックス実行の仕組み

ツールパッケージとシェルコマンドは、サンドボックス環境内で安全に実行されます。

**アーキテクチャ:**

```
┌─────────────────────────────────────────────────────────────┐
│                    Clawleash (Main Process)                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │   Kernel    │  │ ToolLoader  │  │   ShellServer       │  │
│  │  (AI Agent) │  │ (ZIP/DLL)   │  │   (ZeroMQ Router)   │  │
│  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘  │
│         │                │                     │             │
│         │  ToolProxy     │                     │ IPC         │
│         │  (経由で呼出)   │                     │             │
└─────────┼────────────────┼─────────────────────┼─────────────┘
          │                │                     │
          ▼                ▼                     ▼
┌─────────────────────────────────────────────────────────────┐
│              Clawleash.Shell (Sandboxed Process)             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │           AssemblyLoadContext (分離ロード)               │ │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────┐  │ │
│  │  │  Tool DLL   │  │  Tool DLL   │  │  PowerShell     │  │ │
│  │  │  (分離済み)  │  │  (分離済み)  │  │  Constrained    │  │ │
│  │  └─────────────┘  └─────────────┘  └─────────────────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │           AppContainer (Windows) / Bubblewrap (Linux)   │ │
│  │  - ファイルシステムアクセス制御                           │ │
│  │  - ネットワークアクセス制御                               │ │
│  │  - プロセス実行制御                                       │ │
│  │  - フォルダーポリシー適用                                 │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

**実行フロー:**

1. **ツール呼び出し時:**
   - Kernel → ToolProxy → ShellServer (IPC)
   - ShellServer → Shell (サンドボックス内)
   - Shell → AssemblyLoadContext → Tool DLL
   - 結果を逆順で返却

2. **分離の仕組み:**
   - 各ツールパッケージは独立した`AssemblyLoadContext`でロード
   - アンロード可能（`isCollectible: true`）
   - ツール削除時にメモリ解放

3. **セキュリティ境界:**
   - **プロセス分離**: Main ↔ Shell は別プロセス
   - **OSレベル分離**: AppContainer/Bubblewrap でリソース制限
   - **フォルダーポリシー**: パスごとのアクセス制御

**AppContainerケーパビリティ（Windows）:**

```json
{
  "Sandbox": {
    "Type": "AppContainer",
    "AppContainerName": "Clawleash.Sandbox",
    "Capabilities": "InternetClient, PrivateNetworkClientServer"
  }
}
```

| ケーパビリティ | 許可される操作 |
|--------------|---------------|
| `InternetClient` | インターネットへの送信接続 |
| `PrivateNetworkClientServer` | プライベートネットワークへの接続 |
| なし | ネットワークアクセス禁止 |

**フォルダーポリシーによる制御:**

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

## 開発

```bash
# ビルド
dotnet build

# テスト実行
dotnet test

# テスト詳細表示
dotnet test --verbosity normal
```

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
