<div align="center">

# 🐾 Clawleash

**OpenClow風の自律AIエージェント - Firecrawl搭載Webスクレイピング**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue?style=flat-square)](https://github.com)

*Semantic Kernel × Playwright × Autonomous Agent Framework*

[**English**](README-en.md) | 日本語

</div>

---

## 🎯 概要

Clawleash（クラウリッシュ）は、**OpenClow風の自律的AIエージェント**です。Microsoft Semantic KernelとPlaywrightをベースに、Firecrawl/OpenCraw風の強力なWebスクレイピング機能と、人間のような自律的なブラウザ操作を実現します。

## ✨ 主な機能

### 🌐 Webクローラー機能（Firecrawl風）
| 機能 | 説明 |
|------|------|
| **ScrapeUrl** | URLをスクレイプしてMarkdown形式でコンテンツを取得 |
| **CrawlWebsite** | Webサイト全体をクロールして複数ページのコンテンツを取得 |
| **MapWebsite** | サイトマップ（全URL一覧）を高速に取得 |
| **SearchWeb** | Webを検索して結果を取得（DuckDuckGo使用） |
| **BatchScrape** | 複数のURLを一括スクレイプ |

### 📁 ファイル操作
| 機能 | 説明 |
|------|------|
| **CreateFile** | 新しいファイルを作成して内容を書き込み |
| **ReadFile** | ファイルの内容を読み込み（行範囲指定可能） |
| **ReplaceLine** | 特定の行を置換 |
| **ReplaceText** | テキストを一括置換 |
| **InsertLine** | 新しい行を挿入 |
| **DeleteLine** | 特定の行を削除 |
| **MoveFile** | ファイルを移動 |
| **CopyFile** | ファイルをコピー |
| **CreateFolder** | 新しいフォルダを作成 |
| **MoveFolder** | フォルダを移動 |
| **DeleteFolder** | フォルダを削除 |
| **ShowTree** | ディレクトリ構造をツリー形式で表示 |

### 🖱️ ブラウザ操作
- **基本操作**: ナビゲート、クリック、テキスト入力、フォーム送信
- **スクロール**: ページスクロール、最下部への移動
- **待機操作**: 要素表示待機、時間待機、ページ読み込み待機
- **キーボード**: Enter, Tab, Escape, 矢印キーなど
- **マウス操作**: ダブルクリック、右クリック、ドラッグ＆ドロップ

### 🤖 構造化データ抽出（AI搭載）
- **ExtractStructuredData**: AIを使った構造化データ抽出
- **ExtractProductInfo**: 商品情報の自動抽出
- **ExtractArticleInfo**: 記事・ニュース情報の抽出
- **SummarizePage**: ページ内容の要約

### 🧠 自律エージェント機能
- **目標の計画・実行**: 目標を設定して、AIが自動的にタスクを分解・実行
- **自己評価・修正**: 実行結果を評価して、失敗時は別のアプローチを試行
- **メモリ管理**: 短期記憶・長期記憶によるコンテキスト維持
- **Human-in-the-Loop**: 危険な操作にはユーザーの承認が必要

## 📦 インストール

```bash
# リポジトリをクローン
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash

# 依存関係を復元
dotnet restore

# Playwrightブラウザをインストール
pwsh bin/Debug/net10.0/.playwright/package/cli.js install
```

## ⚙️ 設定

`appsettings.json`を作成：

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

## 🚀 使用方法

```bash
dotnet run
```

### ファイル操作の例

```
👤 ユーザー: 現在のディレクトリのツリーを表示して

🤖 Clawleash:
C:\Projects\MyApp
├── 📁 src/
│   ├── 📁 components/
│   │   ├── 🔷 App.tsx
│   │   └── 🔷 Header.tsx
│   └── 📁 utils/
│       └── 💛 helpers.ts
├── 📁 tests/
│   └── 🔷 App.test.ts
├── 📋 package.json
└── 📝 README.md

5 ディレクトリ, 6 ファイル

👤 ユーザー: src/App.ts の最初の20行を読み込んで

🤖 Clawleash:
ファイル: src/App.ts
全行数: 150
表示: 1行目 〜 20行目
---
   1: import React from 'react';
   2: import { Header } from './components/Header';
   3: ...

👤 ユーザー: 15行目を「const MAX_COUNT = 100;」に書き換えて

🤖 Clawleash:
成功: 15行目を置換しました
  旧: const MAX_COUNT = 50;
  新: const MAX_COUNT = 100;

👤 ユーザー: logsフォルダを作成して

🤖 Clawleash:
成功: フォルダ 'logs' を作成しました
```

## 🏗️ アーキテクチャ

```
Clawleash/
├── 📁 Plugins/
│   ├── FileOperationsPlugin.cs      # ファイル操作
│   ├── RestrictedFileSystemPlugin.cs # 基本ファイルシステム
│   ├── RestrictedPowerShellPlugin.cs # PowerShell実行
│   ├── WebCrawlerPlugin.cs          # Webクローラー
│   ├── BrowserActionsPlugin.cs      # ブラウザアクション
│   ├── AdvancedBrowserPlugin.cs     # 高度なブラウザ操作
│   ├── StructuredDataExtractionPlugin.cs # データ抽出
│   └── AutonomousAgentPlugin.cs     # 自律エージェント
├── 📁 Services/
│   ├── BrowserManager.cs            # Playwright管理
│   ├── MemoryManager.cs             # メモリ管理
│   └── AutonomousAgentService.cs    # 自律実行サービス
└── 📁 Models/
    ├── BrowserState.cs
    ├── CrawlResult.cs
    └── AutonomousModels.cs
```

## 📖 APIリファレンス

詳細なAPIリファレンスは[英語版README](README-en.md#-api-reference)を参照してください。

## 🤝 コントリビュート

1. このリポジトリをフォーク
2. フィーチャーブランチを作成 (`git checkout -b feature/amazing-feature`)
3. 変更をコミット (`git commit -m 'Add amazing feature'`)
4. プルリクエストを作成

## 📄 ライセンス

このプロジェクトはMITライセンスの下で公開されています - 詳細は [LICENSE](LICENSE) を参照してください。

---

<div align="center">

**Made with ❤️ by actbit**

[English Version](README-en.md) | [⬆ トップに戻る](#-clawleash)

</div>
