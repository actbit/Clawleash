<div align="center">

# 🐾 Clawleash

**OpenClow-style Autonomous AI Agent with Firecrawl-powered Web Scraping**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue?style=flat-square)](https://github.com)

*Semantic Kernel × Playwright × Autonomous Agent Framework*

[English](#english) | [日本語](#日本語)

</div>

---

## 🇯🇵 日本語

### 🎯 概要

Clawleash（クラウリッシュ）は、**OpenClow風の自律的AIエージェント**です。Microsoft Semantic KernelとPlaywrightをベースに、Firecrawl/OpenCraw風の強力なWebスクレイピング機能と、人間のような自律的なブラウザ操作を実現します。

### ✨ 主な機能

#### 🌐 Webクローラー機能（Firecrawl風）
| 機能 | 説明 |
|------|------|
| **ScrapeUrl** | URLをスクレイプしてMarkdown形式でコンテンツを取得 |
| **CrawlWebsite** | Webサイト全体をクロールして複数ページのコンテンツを取得 |
| **MapWebsite** | サイトマップ（全URL一覧）を高速に取得 |
| **SearchWeb** | Webを検索して結果を取得（DuckDuckGo使用） |
| **BatchScrape** | 複数のURLを一括スクレイプ |
| **GetPageMarkdown** | 現在のページをMarkdown形式で取得 |

#### 🖱️ ブラウザ操作
- **基本操作**: ナビゲート、クリック、テキスト入力、フォーム送信
- **スクロール**: ページスクロール、最下部への移動、特定位置へのスクロール
- **待機操作**: 要素表示待機、時間待機、ページ読み込み待機
- **キーボード**: Enter, Tab, Escape, 矢印キーなど
- **マウス操作**: ダブルクリック、右クリック、ドラッグ＆ドロップ、ホバー

#### 🔧 高度なブラウザ操作
- **ストレージ操作**: Cookie、LocalStorage、SessionStorageの取得・設定
- **フォーム操作**: セレクトボックス、チェックボックス、ラジオボタン
- **テキスト操作**: テキスト選択、コピー、ペースト、検索・ハイライト
- **iframe操作**: iframe内のコンテンツ取得、要素操作
- **テーブル抽出**: HTMLテーブルからデータを抽出

#### 🤖 構造化データ抽出（AI搭載）
- **ExtractStructuredData**: AIを使った構造化データ抽出
- **ExtractProductInfo**: 商品情報の自動抽出
- **ExtractArticleInfo**: 記事・ニュース情報の抽出
- **ExtractContactInfo**: 連絡先情報の抽出
- **SummarizePage**: ページ内容の要約
- **AnalyzePageContent**: ページ内容の分析・質問応答

#### 🧠 自律エージェント機能
- **目標の計画・実行**: 目標を設定して、AIが自動的にタスクを分解・実行
- **自己評価・修正**: 実行結果を評価して、失敗時は別のアプローチを試行
- **メモリ管理**: 短期記憶・長期記憶によるコンテキスト維持
- **Human-in-the-Loop**: 危険な操作にはユーザーの承認が必要

#### 🔒 セキュリティ機能
- **URLフィルタリング**: 許可されたURLのみアクセス可能
- **パス制限**: 許可されたディレクトリのみ操作可能
- **コマンド制限**: 許可されたPowerShellコマンドのみ実行
- **サンドボックス**: Docker、AppContainer、Bubblewrapに対応

### 📦 インストール

```bash
# リポジトリをクローン
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash

# 依存関係を復元
dotnet restore

# Playwrightブラウザをインストール
pwsh bin/Debug/net10.0/.playwright/package/cli.js install
```

### ⚙️ 設定

`appsettings.json`を作成：

```json
{
  "AI": {
    "ApiKey": "your-api-key",
    "ModelId": "gpt-4o",
    "Endpoint": "https://api.openai.com/v1"
  },
  "Browser": {
    "Headless": true,
    "ScreenshotOnAction": false
  },
  "Security": {
    "AllowedUrls": ["https://*"],
    "AllowedPaths": ["C:\\Users\\*"],
    "AllowedCommands": ["Get-*", "Set-Location", "Write-Output"]
  }
}
```

### 🚀 使用方法

```bash
# エージェントを起動
dotnet run
```

#### 対話例

```
👤 ユーザー: https://example.com の商品情報を収集して

🤖 Clawleash:
1. ページにアクセス中...
2. 商品情報を抽出中...

## 抽出結果
- 商品名: Example Product
- 価格: ¥1,980
- 説明: これはサンプル商品です

👤 ユーザー: この情報をCSVファイルに保存して

🤖 Clawleash:
⚠️ 承認が必要な操作です:
タスク: products.csv に書き込み
承認しますか？ (y/n): y

✅ ファイルを保存しました: products.csv
```

#### 自律実行の例

```
👤 ユーザー: ECサイトから全商品の情報を収集して、価格順にソートしてCSVに保存して

🤖 Clawleash:
## タスク計画
1. サイトマップを取得
2. 各商品ページをスクレイプ
3. 商品情報を抽出
4. 価格でソート
5. CSVファイルに保存

[進捗] ステップ 1/5: サイトマップを取得中...
[進捗] ステップ 2/5: 商品ページをスクレイプ中...
[進捗] ステップ 3/5: 商品情報を抽出中...

✅ 完了: 50件の商品情報を products.csv に保存しました
```

### 🏗️ アーキテクチャ

```
Clawleash/
├── 📁 Plugins/
│   ├── RestrictedFileSystemPlugin.cs   # ファイル操作
│   ├── RestrictedPowerShellPlugin.cs   # PowerShell実行
│   ├── RestrictedBrowserPlugin.cs      # 基本ブラウザ操作
│   ├── WebCrawlerPlugin.cs             # Webクローラー
│   ├── BrowserActionsPlugin.cs         # ブラウザアクション
│   ├── AdvancedBrowserPlugin.cs        # 高度なブラウザ操作
│   ├── StructuredDataExtractionPlugin.cs # データ抽出
│   └── AutonomousAgentPlugin.cs        # 自律エージェント
├── 📁 Services/
│   ├── BrowserManager.cs               # Playwright管理
│   ├── MemoryManager.cs                # メモリ管理
│   └── AutonomousAgentService.cs       # 自律実行サービス
├── 📁 Models/
│   ├── BrowserState.cs                 # ブラウザ状態
│   ├── CrawlResult.cs                  # クロール結果
│   └── AutonomousModels.cs             # 自律エージェントモデル
├── 📁 Security/
│   ├── UrlValidator.cs                 # URL検証
│   ├── PathValidator.cs                # パス検証
│   └── CommandValidator.cs             # コマンド検証
└── 📁 Sandbox/
    ├── DockerSandboxProvider.cs        # Docker サンドボックス
    └── AppContainerProvider.cs         # Windows AppContainer
```

### 🔧 設定オプション

#### 自律エージェント設定

```json
{
  "AutonomousSettings": {
    "MaxSteps": 10,
    "MaxRetries": 3,
    "RequireApprovalForDangerousOperations": true,
    "RequireApprovalForFileDeletion": true,
    "RequireApprovalForFormSubmission": true,
    "StepDelayMs": 500
  }
}
```

### 🤝 コントリビュート

1. このリポジトリをフォーク
2. フィーチャーブランチを作成 (`git checkout -b feature/amazing-feature`)
3. 変更をコミット (`git commit -m 'Add amazing feature'`)
4. ブランチにプッシュ (`git push origin feature/amazing-feature`)
5. プルリクエストを作成

### 📄 ライセンス

このプロジェクトはMITライセンスの下で公開されています - 詳細は [LICENSE](LICENSE) を参照してください。

---

## 🇺🇸 English

### 🎯 Overview

Clawleash is an **OpenClow-style autonomous AI agent** built on Microsoft Semantic Kernel and Playwright. It provides powerful Firecrawl/OpenCraw-style web scraping capabilities and human-like autonomous browser operation.

### ✨ Key Features

#### 🌐 Web Crawler (Firecrawl-style)
- **ScrapeUrl**: Scrape URLs and get content in Markdown format
- **CrawlWebsite**: Crawl entire websites with multi-page content extraction
- **MapWebsite**: Fast sitemap generation (all URL listing)
- **SearchWeb**: Web search with content scraping
- **BatchScrape**: Bulk scrape multiple URLs

#### 🖱️ Browser Operations
- Basic: Navigate, click, type, form submission
- Scroll: Page scroll, scroll to bottom, scroll to position
- Wait: Wait for selector, wait for timeout, wait for load
- Keyboard: Enter, Tab, Escape, arrow keys, etc.
- Mouse: Double-click, right-click, drag & drop, hover

#### 🤖 AI-Powered Data Extraction
- Structured data extraction with AI
- Product info extraction
- Article/news extraction
- Contact info extraction
- Page summarization
- Content analysis & Q&A

#### 🧠 Autonomous Agent
- Goal planning and execution
- Self-evaluation and correction
- Memory management (short-term & long-term)
- Human-in-the-Loop approval system

#### 🔒 Security
- URL filtering
- Path restrictions
- Command restrictions
- Sandbox support (Docker, AppContainer, Bubblewrap)

### 📦 Installation

```bash
# Clone repository
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash

# Restore dependencies
dotnet restore

# Install Playwright browsers
pwsh bin/Debug/net10.0/.playwright/package/cli.js install
```

### ⚙️ Configuration

Create `appsettings.json`:

```json
{
  "AI": {
    "ApiKey": "your-api-key",
    "ModelId": "gpt-4o",
    "Endpoint": "https://api.openai.com/v1"
  },
  "Browser": {
    "Headless": true
  }
}
```

### 🚀 Usage

```bash
dotnet run
```

### 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### 📄 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) for details.

---

<div align="center">

**Made with ❤️ by Clawleash Team**

[⬆ Back to Top](#-clawleash)

</div>
