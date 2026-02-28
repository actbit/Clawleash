# Clawleash.Interfaces.Discord

Discord Bot チャットインターフェースの完全実装。Discord.NET を使用して Discord サーバーおよび DM からメッセージを受信し、AI エージェントと連携します。

## 機能

- **リアルタイムメッセージ受信**: Gateway Intents を使用したリアルタイムメッセージ監視
- **コマンドプレフィックス対応**: `!` などのプレフィックスでコマンドを識別
- **スレッド返信**: 元のメッセージに対する返信形式で応答
- **DM 対応**: ダイレクトメッセージでも動作（プレフィックス不要）
- **ストリーミング送信**: 長いメッセージの一括送信対応

## アーキテクチャ

```
┌──────────────────────────────────────────────────┐
│           DiscordChatInterface (C#)              │
│  ┌────────────────────────────────────────────┐  │
│  │  DiscordSocketClient (Discord.NET)         │  │
│  │  - Gateway Intents                         │  │
│  │  - Message Received Events                 │  │
│  └────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────┘
                       │
           ┌───────────────────────┐
           │   Discord Gateway     │
           │   (WebSocket)         │
           └───────────────────────┘
```

## 使用方法

### 設定

```csharp
var settings = new DiscordSettings
{
    Token = "YOUR_BOT_TOKEN",
    CommandPrefix = "!",
    UseThreads = false,
    UseEmbeds = true
};
```

### 基本的な使用

```csharp
var chatInterface = new DiscordChatInterface(settings, logger);

// イベントハンドラー
chatInterface.MessageReceived += (sender, args) =>
{
    Console.WriteLine($"Message from {args.SenderName}: {args.Content}");
    Console.WriteLine($"Guild: {args.Metadata["GuildName"]}");
    Console.WriteLine($"Channel: {args.Metadata["ChannelName"]}");
    Console.WriteLine($"Is DM: {args.Metadata["IsDirectMessage"]}");
};

// 開始
await chatInterface.StartAsync(cancellationToken);

// メッセージ送信（返信形式）
await chatInterface.SendMessageAsync("Hello!", replyToMessageId);

// 終了
await chatInterface.DisposeAsync();
```

## 設定オプション

| プロパティ | 説明 | デフォルト |
|-----------|------|-----------|
| `Token` | Discord Bot Token | (必須) |
| `CommandPrefix` | コマンドプレフィックス（DM では無視） | `!` |
| `UseThreads` | 返信をスレッドで行うかどうか | `false` |
| `UseEmbeds` | Embed メッセージを使用するかどうか | `true` |

## イベント

### MessageReceived

メッセージ受信時に発生するイベント。

```csharp
chatInterface.MessageReceived += (sender, args) =>
{
    // args.MessageId - メッセージ ID
    // args.SenderId - 送信者 Discord ID
    // args.SenderName - 送信者名（グローバル名優先）
    // args.Content - メッセージ内容（プレフィックス削除済み）
    // args.ChannelId - チャンネル ID
    // args.Timestamp - タイムスタンプ
    // args.Metadata["GuildId"] - サーバー ID
    // args.Metadata["GuildName"] - サーバー名
    // args.Metadata["ChannelName"] - チャンネル名
    // args.Metadata["IsDirectMessage"] - DM かどうか
    // args.Metadata["IsThread"] - スレッドかどうか
};
```

## Bot 設定

### 必要な権限

- **Read Messages**: メッセージの読み取り
- **Send Messages**: メッセージの送信
- **Read Message History**: メッセージ履歴の読み取り（返信用）
- **View Channels**: チャンネルの表示

### Gateway Intents

以下の Intents が必要です：

- `MessageContent` - メッセージ内容の読み取り
- `GuildMessages` - サーバーメッセージの受信
- `DirectMessages` - DM の受信

## トラブルシューティング

### "Privileged intent provided is not enabled"

Bot 設定で Message Content Intent を有効にしてください：
1. Discord Developer Portal を開く
2. 対象のアプリケーションを選択
3. Bot タブ → Privileged Gateway Intents
4. "Message Content Intent" を有効化

### "Discord token is not configured"

`appsettings.json` で Token が正しく設定されているか確認してください：

```json
{
  "ChatInterface": {
    "Discord": {
      "Enabled": true,
      "Token": "${DISCORD_BOT_TOKEN}"
    }
  }
}
```

### コマンドが反応しない

- コマンドプレフィックスが正しいか確認
- Bot がチャンネルにアクセス権を持っているか確認
- DM ではプレフィックスなしで送信

## ビルド

```bash
cd Clawleash.Interfaces.Discord
dotnet build
```

## 依存関係

- Discord.NET (最新安定版)
- Clawleash.Abstractions

## ライセンス

MIT
