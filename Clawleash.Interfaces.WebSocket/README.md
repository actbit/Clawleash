# Clawleash.Interfaces.WebSocket

WebSocket チャットインターフェースの完全実装。SignalR クライアントを使用してサーバーとリアルタイム通信を行い、E2EE（エンドツーエンド暗号化）に対応しています。

## 機能

- **SignalR 通信**: ASP.NET Core SignalR によるリアルタイム双方向通信
- **E2EE 対応**: X25519 鍵交換 + AES-256-GCM によるエンドツーエンド暗号化
- **チャンネル鍵**: チャンネルごとの暗号化鍵管理
- **自動再接続**: 指数バックオフによる自動再接続
- **ストリーミング送信**: 長いメッセージの一括送信対応

## アーキテクチャ

```
┌──────────────────────────────────────────────────┐
│         WebSocketChatInterface (C#)              │
│  ┌────────────────────────────────────────────┐  │
│  │  HubConnection (SignalR Client)            │  │
│  │  - Automatic Reconnect                     │  │
│  │  - Message/Channel Key Handling            │  │
│  └────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────┐  │
│  │  AesGcmE2eeProvider                        │  │
│  │  - X25519 Key Exchange                     │  │
│  │  - AES-256-GCM Encryption/Decryption       │  │
│  └────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────┘
                       │
           ┌───────────────────────┐
           │   Clawleash.Server    │
           │   ChatHub (/chat)     │
           └───────────────────────┘
```

## E2EE 暗号化フロー

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

## 使用方法

### 設定

```csharp
var settings = new WebSocketSettings
{
    ServerUrl = "wss://localhost:8080/chat",
    EnableE2ee = true,
    ReconnectIntervalMs = 5000,
    MaxReconnectAttempts = 10,
    HeartbeatIntervalMs = 30000,
    ConnectionTimeoutMs = 10000
};
```

### 基本的な使用

```csharp
var chatInterface = new WebSocketChatInterface(settings, logger);

// イベントハンドラー
chatInterface.MessageReceived += (sender, args) =>
{
    Console.WriteLine($"Message from {args.SenderName}: {args.Content}");
    Console.WriteLine($"Encrypted: {args.Metadata["encrypted"]}");
};

// 開始（E2EE 有効時は鍵交換も実行）
await chatInterface.StartAsync(cancellationToken);

// チャンネルに参加
await chatInterface.JoinChannelAsync("general");

// メッセージ送信
await chatInterface.SendMessageAsync("Hello!", replyToMessageId);

// チャンネルから離脱
await chatInterface.LeaveChannelAsync("general");

// 終了
await chatInterface.DisposeAsync();
```

## 設定オプション

| プロパティ | 説明 | デフォルト |
|-----------|------|-----------|
| `ServerUrl` | SignalR サーバー URL | `ws://localhost:8080/chat` |
| `EnableE2ee` | E2EE 有効化（親設定から継承） | `false` |
| `ReconnectIntervalMs` | 再接続間隔 | `5000` |
| `MaxReconnectAttempts` | 最大再接続試行回数 | `10` |
| `HeartbeatIntervalMs` | ハートビート間隔 | `30000` |
| `ConnectionTimeoutMs` | 接続タイムアウト | `10000` |

## イベント

### MessageReceived

メッセージ受信時に発生するイベント。

```csharp
chatInterface.MessageReceived += (sender, args) =>
{
    // args.MessageId - メッセージ ID
    // args.SenderId - 送信者 ID
    // args.SenderName - 送信者名
    // args.Content - メッセージ内容（復号化済み）
    // args.ChannelId - チャンネル ID
    // args.Timestamp - タイムスタンプ
    // args.Metadata["encrypted"] - 暗号化されていたか
};
```

## 再接続ポリシー

指数バックオフを使用した自動再接続：

```
試行 1: 5 秒後
試行 2: 10 秒後
試行 3: 20 秒後
...
最大 60 秒間隔
```

最大試行回数に達すると再接続を停止します。

## トラブルシューティング

### "WebSocket server URL is not configured"

`appsettings.json` で URL が設定されているか確認：

```json
{
  "ChatInterface": {
    "WebSocket": {
      "Enabled": true,
      "ServerUrl": "wss://localhost:8080/chat",
      "EnableE2ee": true
    }
  }
}
```

### "Failed to connect to SignalR hub"

1. Clawleash.Server が起動しているか確認
2. URL が正しいか確認（`ws://` または `wss://`）
3. ファイアウォール設定を確認

### "Key exchange failed"

1. サーバー側でも E2EE が有効か確認
2. サーバーの時刻が正しいか確認

### メッセージが暗号化されない

- `EnableE2ee` が `true` に設定されているか確認
- チャンネル鍵が設定されているか確認（`HasChannelKey`）

## ビルド

```bash
cd Clawleash.Interfaces.WebSocket
dotnet build
```

## 依存関係

- Microsoft.AspNetCore.SignalR.Client
- Clawleash.Abstractions

## 関連プロジェクト

- [Clawleash.Server](../Clawleash.Server) - SignalR サーバー
- [Clawleash.Interfaces.WebRTC](../Clawleash.Interfaces.WebRTC) - WebRTC インターフェース

## ライセンス

MIT
