import * as signalR from '@microsoft/signalr';
import { E2eeProvider } from './e2ee';

export interface Message {
  messageId: string;
  senderId: string;
  senderName: string;
  content: string;
  channelId: string;
  encrypted: boolean;
  ciphertext?: string;
  timestamp: Date;
}

export interface ConnectionState {
  status: 'disconnected' | 'connecting' | 'connected' | 'reconnecting';
  error?: string;
}

export class ChatService {
  private connection: signalR.HubConnection | null = null;
  private e2ee: E2eeProvider;
  private enableE2ee: boolean;
  private currentChannel: string | null = null;

  public onMessageReceived?: (message: Message) => void;
  public onConnectionStateChanged?: (state: ConnectionState) => void;
  public onKeyExchangeCompleted?: () => void;
  public onChannelJoined?: (channelId: string) => void;

  constructor(enableE2ee: boolean = true) {
    this.e2ee = new E2eeProvider();
    this.enableE2ee = enableE2ee;
  }

  async connect(serverUrl: string): Promise<void> {
    this.notifyState('connecting');

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${serverUrl}/chat`, {
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.elapsedMilliseconds < 60000) {
            return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
          }
          return null;
        }
      })
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.connection.on('MessageReceived', async (message: any) => {
      let content = message.content;

      // E2EE復号化（チャンネル鍵を使用）
      if (message.encrypted && message.ciphertext) {
        try {
          content = await this.e2ee.decrypt(message.ciphertext, message.channelId);
        } catch (e) {
          console.error('Failed to decrypt message:', e);
          content = '[Encrypted - Decryption Failed]';
        }
      }

      this.onMessageReceived?.({
        ...message,
        content,
        timestamp: new Date(message.timestamp)
      });
    });

    this.connection.on('KeyExchangeCompleted', () => {
      this.onKeyExchangeCompleted?.();
    });

    this.connection.on('ChannelKey', async (data: any) => {
      try {
        if (data.encryptedKey) {
          // E2EE有効: 暗号化されたチャンネル鍵を復号化
          await this.e2ee.setChannelKey(data.channelId, data.encryptedKey);
        } else if (data.plainKey) {
          // E2EE無効: 平文のチャンネル鍵を設定
          this.e2ee.setPlainChannelKey(data.channelId, data.plainKey);
        }

        console.log(`Channel key received for ${data.channelId}`);
        this.onChannelJoined?.(data.channelId);
      } catch (e) {
        console.error('Failed to set channel key:', e);
      }
    });

    this.connection.on('Pong', (timestamp: Date) => {
      console.log('Pong received:', timestamp);
    });

    this.connection.onclose(() => {
      this.notifyState('disconnected');
    });

    this.connection.onreconnecting(() => {
      this.notifyState('reconnecting');
    });

    this.connection.onreconnected(async () => {
      this.notifyState('connected');

      // E2EE鍵交換を再実行
      if (this.enableE2ee) {
        await this.performKeyExchange();
      }

      // 現在のチャンネルに再参加
      if (this.currentChannel) {
        await this.joinChannel(this.currentChannel);
      }
    });

    try {
      await this.connection.start();
      this.notifyState('connected');

      // E2EE鍵交換
      if (this.enableE2ee) {
        await this.performKeyExchange();
      }
    } catch (error) {
      this.notifyState('disconnected', String(error));
      throw error;
    }
  }

  private async performKeyExchange(): Promise<void> {
    if (!this.connection) return;

    await this.e2ee.initialize();

    const startResponse = await this.connection.invoke('StartKeyExchange');
    await this.e2ee.completeKeyExchange(startResponse.serverPublicKey);

    const clientPublicKey = await this.e2ee.getPublicKey();
    await this.connection.invoke('CompleteKeyExchange',
      startResponse.sessionId,
      this.arrayBufferToBase64(clientPublicKey)
    );
  }

  async joinChannel(channelId: string): Promise<void> {
    if (!this.connection) throw new Error('Not connected');

    this.currentChannel = channelId;
    await this.connection.invoke('JoinChannel', channelId);
  }

  async leaveChannel(channelId: string): Promise<void> {
    if (!this.connection) throw new Error('Not connected');

    if (this.currentChannel === channelId) {
      this.currentChannel = null;
    }

    await this.connection.invoke('LeaveChannel', channelId);
  }

  async sendMessage(content: string, channelId: string, senderName: string = 'Anonymous'): Promise<void> {
    if (!this.connection) throw new Error('Not connected');

    const canEncrypt = this.enableE2ee && this.e2ee.hasChannelKey(channelId);

    if (canEncrypt) {
      // チャンネル鍵で暗号化
      const ciphertext = await this.e2ee.encrypt(content, channelId);
      await this.connection.invoke('SendMessage', {
        content: '',
        channelId,
        senderName,
        encrypted: true,
        ciphertext
      });
    } else {
      // 平文で送信
      await this.connection.invoke('SendMessage', {
        content,
        channelId,
        senderName,
        encrypted: false
      });
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
    this.e2ee.reset();
    this.currentChannel = null;
    this.notifyState('disconnected');
  }

  get isConnected(): boolean {
    return this.connection?.state === signalR.HubConnectionState.Connected;
  }

  get isE2eeEnabled(): boolean {
    return this.enableE2ee && this.e2ee.isEncrypted;
  }

  private notifyState(status: ConnectionState['status'], error?: string): void {
    this.onConnectionStateChanged?.({ status, error });
  }

  private arrayBufferToBase64(buffer: ArrayBuffer | Uint8Array): string {
    const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
  }
}
