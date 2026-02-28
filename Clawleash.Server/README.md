# Clawleash.Server

Clawleash の SignalR サーバーコンポーネント。WebSocket および WebRTC クライアントとのリアルタイム通信を提供し、E2EE（エンドツーエンド暗号化）に対応しています。

## 機能

- **SignalR Hub**: リアルタイム双方向通信
- **ChatHub**: WebSocket クライアント用チャットハブ（E2EE 対応）
- **SignalingHub**: WebRTC シグナリングサーバー
- **E2EE 鍵管理**: X25519 鍵交換・チャンネル鍵配布
- **Svelte クライアント配信**: 静的ファイルとして SPA を配信

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                    Clawleash.Server                          │
│  ┌─────────────────────┐  ┌─────────────────────────────┐   │
│  │     ChatHub         │  │     SignalingHub            │   │
│  │  (/chat)            │  │  (/signaling)               │   │
│  │  - E2EE 対応        │  │  - SDP/ICE 候補交換         │   │
│  │  - チャンネル管理    │  │  - ピア接続管理             │   │
│  └─────────────────────┘  └─────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │     Security                                             │ │
│  │  ┌───────────────────┐  ┌─────────────────────────────┐  │ │
│  │  │ KeyManager        │  │ E2eeMiddleware              │  │ │
│  │  │ - 鍵ペア生成       │  │ - チャンネル鍵管理          │  │ │
│  │  │ - セッション管理   │  │                             │  │ │
│  │  └───────────────────┘  └─────────────────────────────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │     Svelte Client (Static Files)                        │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## 使用方法

### 起動

```bash
cd Clawleash.Server
dotnet run

# または
dotnet run --project Clawleash.Server
```

### エンドポイント

| パス | 説明 |
|------|------|
| `/` | Svelte SPA クライアント |
| `/chat` | WebSocket ChatHub |
| `/signaling` | WebRTC シグナリングハブ |

### 設定

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedOrigins": "https://yourdomain.com"
}
```

## ChatHub API

### クライアント → サーバー

| メソッド | 説明 |
|----------|------|
| `StartKeyExchange()` | E2EE 鍵交換開始 |
| `CompleteKeyExchange(sessionId, publicKey)` | 鍵交換完了 |
| `SendMessage(content, channelId, senderName, encrypted, ciphertext)` | メッセージ送信 |
| `JoinChannel(channelId)` | チャンネル参加 |
| `LeaveChannel(channelId)` | チャンネル離脱 |

### サーバー → クライアント

| イベント | 説明 |
|----------|------|
| `MessageReceived` | メッセージ受信 |
| `ChannelKey` | チャンネル鍵配布 |
| `KeyExchangeCompleted` | 鍵交換完了通知 |

## SignalingHub API

### クライアント → サーバー

| メソッド | 説明 |
|----------|------|
| `Register(peerId, metadata)` | ピア登録 |
| `Offer(targetPeerId, sdp)` | SDP オファー送信 |
| `Answer(targetPeerId, sdp)` | SDP アンサー送信 |
| `IceCandidate(targetPeerId, candidate)` | ICE 候補送信 |

### サーバー → クライアント

| イベント | 説明 |
|----------|------|
| `PeerConnected` | ピー接続通知 |
| `PeerDisconnected` | ピー切断通知 |
| `Offer` | SDP オファー受信 |
| `Answer` | SDP アンサー受信 |
| `IceCandidate` | ICE 候補受信 |

## CORS 設定

### 開発環境

```csharp
policy.WithOrigins("http://localhost:5173", "http://localhost:4173")
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();
```

### 本番環境

`AllowedOrigins` 設定で許可するオリジンを指定：

```json
{
  "AllowedOrigins": "https://app.yourdomain.com"
}
```

## E2EE 鍵交換フロー

```
Client                          Server
  │                               │
  │ ─── StartKeyExchange ──────► │
  │                               │ 1. 鍵ペア生成
  │                               │    セッション ID 発行
  │ ◄── ServerPublicKey ─────── │
  │    SessionId                 │
  │                               │
  │ 2. 共有秘密生成               │
  │    チャンネル鍵生成           │
  │                               │
  │ ─── ClientPublicKey ───────► │
  │    SessionId                 │ 3. 共有秘密生成
  │                               │
  │ ◄── KeyExchangeCompleted ── │
  │                               │
  │ ◄── ChannelKey ───────────── │
  │    (暗号化済み)               │
```

## Svelte クライアント

`Client/` ディレクトリに Svelte アプリケーションを配置：

```
Client/
├── index.html
├── _framework/
│   └── blazor.webassembly.js
└── ...
```

ビルド時に `wwwroot/` にコピーされます。

## トラブルシューティング

### "CORS policy blocked"

1. 開発環境では `localhost:5173` が許可されています
2. 本番環境では `AllowedOrigins` を設定してください

### WebSocket 接続が切れる

1. プロキシサーバーで WebSocket が許可されているか確認
2. Keep-Alive 設定を確認

### E2EE が動作しない

1. クライアントとサーバーで `EnableE2ee` が `true` に設定されているか確認
2. 時刻同期が正しいか確認

## ビルド

```bash
cd Clawleash.Server
dotnet build
```

## 依存関係

- Microsoft.AspNetCore.SignalR
- Clawleash.Abstractions

## 関連プロジェクト

- [Clawleash.Interfaces.WebSocket](../Clawleash.Interfaces.WebSocket) - WebSocket クライアント
- [Clawleash.Interfaces.WebRTC](../Clawleash.Interfaces.WebRTC) - WebRTC クライアント

## ライセンス

MIT
