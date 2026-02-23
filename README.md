<div align="center">

# Clawleash

**自律AIエージェントフレームワーク - サンドボックス実行環境搭載**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

*Semantic Kernel × PowerShell × Sandbox Architecture*

</div>

---

## 概要

Clawleash（クラウリッシュ）は、安全なサンドボックス環境で動作する自律AIエージェントフレームワークです。

### 特徴

- **サンドボックス実行**: PowerShell/コマンドを分離プロセスで安全に実行
- **ツールパッケージシステム**: ZIP/DLLでツールを追加可能
- **承認システム**: 危険な操作にはユーザー承認が必要
- **マルチプラットフォーム**: Windows (AppContainer) / Linux (Bubblewrap)

## アーキテクチャ

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

## プロジェクト構成

```
Clowleash/
├── Clawleash/                    # メインアプリケーション
│   ├── Execution/
│   │   └── ShellServer.cs        # IPCサーバー
│   ├── Tools/
│   │   ├── ToolLoader.cs         # ツールローダー
│   │   ├── ToolPackage.cs        # パッケージ管理
│   │   ├── ToolProxyGenerator.cs # プロキシ生成
│   │   └── ShellToolExecutor.cs  # IPC経由実行
│   ├── Services/
│   │   ├── IApprovalHandler.cs   # 承認システム
│   │   ├── IInputHandler.cs      # 入力システム
│   │   └── AutonomousAgentService.cs
│   ├── Sandbox/
│   │   ├── AppContainerProvider.cs  # Windows
│   │   └── BubblewrapProvider.cs    # Linux
│   └── Plugins/                 # Semantic Kernel プラグイン
│
├── Clawleash.Shell/              # サンドボックスプロセス
│   ├── IPC/IpcClient.cs          # IPCクライアント
│   └── Hosting/
│       └── ConstrainedRunspaceHost.cs  # 制約付きPowerShell
│
└── Clawleash.Contracts/          # 共有型定義
    └── Messages/
        ├── ShellMessages.cs      # IPCメッセージ
        └── Enums.cs              # 共有Enum
```

## インストール

```bash
git clone https://github.com/yourusername/Clowleash.git
cd Clawleash
dotnet restore
```

## 設定

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

## 使用方法

```bash
dotnet run --project Clawleash
```

### ツールパッケージの追加

```csharp
// パッケージディレクトリのZIPを一括ロード
await toolLoader.LoadAllAsync(kernel);

// ホットリロード有効
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

### 承認システム

```csharp
// CLI用
services.AddCliApprovalHandler();

// 自動化用（ルールベース）
services.AddSilentApprovalHandler(config);
```

## IPC通信

- **プロトコル**: ZeroMQ (Router/Dealer)
- **シリアライズ**: MessagePack
- **方向**: Main (Server) ← Shell (Client)

## 開発

```bash
# ビルド
dotnet build

# テスト
dotnet test
```

## ライセンス

MIT License - [LICENSE](LICENSE) を参照
