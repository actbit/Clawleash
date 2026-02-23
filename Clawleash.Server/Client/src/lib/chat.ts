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

  public onMessageReceived?: (message: Message) => void;
  public onConnectionStateChanged?: (state: ConnectionState) => void;
  public onKeyExchangeCompleted?: () => void;

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

      // E2EE復号化
      if (message.encrypted && this.e2ee.isEncrypted && message.ciphertext) {
        try {
          content = await this.e2ee.decrypt(message.ciphertext);
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
    await this.connection.invoke('JoinChannel', channelId);
  }

  async leaveChannel(channelId: string): Promise<void> {
    if (!this.connection) throw new Error('Not connected');
    await this.connection.invoke('LeaveChannel', channelId);
  }

  async sendMessage(content: string, channelId: string, senderName: string = 'Anonymous'): Promise<void> {
    if (!this.connection) throw new Error('Not connected');

    if (this.enableE2ee && this.e2ee.isEncrypted) {
      const ciphertext = await this.e2ee.encrypt(content);
      await this.connection.invoke('SendMessage', {
        content: '',
        channelId,
        senderName,
        encrypted: true,
        ciphertext
      });
    } else {
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
