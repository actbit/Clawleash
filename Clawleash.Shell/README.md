# Clawleash.Shell

Clawleash のサンドボックス実行プロセス。ZeroMQ + MessagePack による IPC でメインアプリケーションと通信し、制約付き PowerShell 環境でコマンドを実行します。

## 概要

Clawleash.Shell は分離されたプロセスとして動作し、以下の役割を持ちます：

- **IPC クライアント**: ZeroMQ (DealerSocket) でメインアプリに接続
- **PowerShell 実行**: 制約付き PowerShell Runspace でコマンド実行
- **サンドボックス**: AppContainer (Windows) / Bubblewrap (Linux) で分離実行

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                      Clawleash (Main)                        │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              ShellServer (RouterSocket)                  │ │
│  └──────────────────────────┬──────────────────────────────┘ │
└─────────────────────────────┼───────────────────────────────┘
                              │
                              │ ZeroMQ + MessagePack
                              │
┌─────────────────────────────┼───────────────────────────────┐
│                    Clawleash.Shell (Sandboxed)               │
│  ┌──────────────────────────┴──────────────────────────────┐ │
│  │               IpcClient (DealerSocket)                   │ │
│  └──────────────────────────┬──────────────────────────────┘ │
│                             │                                │
│  ┌──────────────────────────┴──────────────────────────────┐ │
│  │          ConstrainedRunspaceHost                         │ │
│  │          (PowerShell SDK)                                │ │
│  │  ┌─────────────────────────────────────────────────────┐ │ │
│  │  │  ConstrainedLanguage Mode                           │ │ │
│  │  │  - Command Whitelist                                │ │ │
│  │  │  - Path Restrictions                                │ │ │
│  │  │  - Folder Policies                                  │ │ │
│  │  └─────────────────────────────────────────────────────┘ │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## 使用方法

### 起動

Clawleash.Shell は通常、メインアプリケーションから自動的に起動されます：

```bash
# 手動起動（デバッグ用）
Clawleash.Shell --server tcp://localhost:5555

# 詳細ログ有効
Clawleash.Shell --server tcp://localhost:5555 --verbose
```

### コマンドライン引数

| 引数 | 短縮 | 説明 |
|------|------|------|
| `--server <address>` | `-s` | ZeroMQ サーバーアドレス (必須) |
| `--verbose` | `-v` | 詳細ログ出力 |

## IPC プロトコル

### 通信仕様

| 項目 | 仕様 |
|------|------|
| プロトコル | ZeroMQ (Router/Dealer) |
| シリアライズ | MessagePack |
| 方向 | Main (Server) ← Shell (Client) |

### メッセージ種別

| メッセージ | 方向 | 説明 |
|-----------|------|------|
| `ShellInitializeRequest` | S → M | 初期化要求 |
| `ShellInitializeResponse` | M → S | 初期化応答 |
| `ShellExecuteRequest` | M → S | コマンド実行要求 |
| `ShellExecuteResponse` | S → M | 実行結果 |
| `ToolInvokeRequest` | M → S | ツール呼び出し要求 |
| `ToolInvokeResponse` | S → M | ツール実行結果 |
| `ShellPingRequest` | M → S | 死活監視 |
| `ShellPingResponse` | S → M | 応答 |

## PowerShell 制約

### ConstrainedLanguage モード

デフォルトで ConstrainedLanguage モードで動作：

- **許可**: 基本的なコマンド、パイプライン、変数
- **禁止**: .NET メソッド呼び出し、Add-Type、スクリプトブロック

### コマンドホワイトリスト

許可されたコマンドのみ実行可能：

```powershell
# 許可されるコマンド例
Get-Content, Set-Content, Get-ChildItem
New-Item, Remove-Item, Copy-Item, Move-Item
Write-Output, Write-Error
```

### パス制限

フォルダーポリシーに基づくアクセス制御：

```json
{
  "FolderPolicies": [
    {
      "Path": "C:\\Projects",
      "Access": "ReadWrite",
      "Network": "Allow",
      "Execute": "Allow"
    },
    {
      "Path": "C:\\Projects\\Secrets",
      "Access": "Deny"
    }
  ]
}
```

## サンドボックス

### Windows (AppContainer)

- ケーパビリティベースのアクセス制御
- ファイルシステム、ネットワーク、レジストリの分離
- 低い整合性レベルで実行

### Linux (Bubblewrap)

- 名前空間分離 (PID, Network, Mount, User)
- cgroups によるリソース制限
- seccomp フィルタ

## プロジェクト構成

```
Clawleash.Shell/
├── Program.cs                 # エントリーポイント
├── IPC/
│   └── IpcClient.cs          # ZeroMQ クライアント
├── Hosting/
│   └── ConstrainedRunspaceHost.cs  # PowerShell ホスト
└── Cmdlets/
    └── ...                   # カスタムコマンドレット
```

## トラブルシューティング

### "サーバーアドレスが指定されていません"

`--server` 引数を指定してください：

```bash
Clawleash.Shell --server tcp://localhost:5555
```

### "Main アプリへの接続に失敗しました"

1. メインアプリケーションが起動しているか確認
2. サーバーアドレスが正しいか確認
3. ファイアウォール設定を確認

### PowerShell コマンドが失敗する

1. コマンドがホワイトリストに含まれているか確認
2. パスがフォルダーポリシーで許可されているか確認
3. ConstrainedLanguage モードの制限を確認

## ビルド

```bash
cd Clawleash.Shell
dotnet build
```

## 依存関係

- NetMQ (ZeroMQ)
- MessagePack
- PowerShell SDK
- Clawleash.Contracts

## 関連プロジェクト

- [Clawleash](../Clawleash) - メインアプリケーション
- [Clawleash.Contracts](../Clawleash.Contracts) - IPC メッセージ定義

## ライセンス

MIT
