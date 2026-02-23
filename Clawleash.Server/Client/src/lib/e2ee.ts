// E2EE暗号化ユーティリティ

export class E2eeProvider {
  private privateKey: Uint8Array | null = null;
  private sessionKey: Uint8Array | null = null;
  private sessionId: string | null = null;

  get isEncrypted(): boolean {
    return this.sessionKey !== null;
  }

  get currentSessionId(): string | null {
    return this.sessionId;
  }

  async initialize(): Promise<void> {
    this.privateKey = crypto.getRandomValues(new Uint8Array(32));
  }

  async getPublicKey(): Promise<Uint8Array> {
    if (!this.privateKey) {
      throw new Error('Not initialized');
    }

    // SHA-256ベースの公開鍵導出
    const hash = await crypto.subtle.digest('SHA-256', this.privateKey);
    const publicKey = new Uint8Array(hash);

    // Curve25519 adjustments
    publicKey[0] &= 248;
    publicKey[31] &= 127;
    publicKey[31] |= 64;

    return publicKey;
  }

  async startKeyExchange(): Promise<{ sessionId: string; publicKey: string }> {
    if (!this.privateKey) {
      await this.initialize();
    }

    this.sessionId = crypto.randomUUID();

    return {
      sessionId: this.sessionId,
      publicKey: this.arrayBufferToBase64(await this.getPublicKey())
    };
  }

  async completeKeyExchange(peerPublicKeyBase64: string): Promise<void> {
    if (!this.privateKey) {
      throw new Error('Not initialized');
    }

    const peerPublicKey = this.base64ToArrayBuffer(peerPublicKeyBase64);

    // 共有秘密を導出
    const combined = new Uint8Array(64);
    combined.set(this.privateKey, 0);
    combined.set(new Uint8Array(peerPublicKey), 32);

    const hash = await crypto.subtle.digest('SHA-256', combined);
    this.sessionKey = new Uint8Array(hash);
  }

  async encrypt(plaintext: string): Promise<string> {
    if (!this.sessionKey) {
      throw new Error('Session key not established');
    }

    const encoder = new TextEncoder();
    const plaintextBytes = encoder.encode(plaintext);

    // Nonce (12 bytes)
    const nonce = crypto.getRandomValues(new Uint8Array(12));

    // AES-GCM鍵をインポート
    const key = await crypto.subtle.importKey(
      'raw',
      this.sessionKey,
      { name: 'AES-GCM' },
      false,
      ['encrypt']
    );

    // 暗号化
    const ciphertext = await crypto.subtle.encrypt(
      { name: 'AES-GCM', iv: nonce },
      key,
      plaintextBytes
    );

    // nonce + ciphertext
    const result = new Uint8Array(nonce.length + ciphertext.byteLength);
    result.set(nonce, 0);
    result.set(new Uint8Array(ciphertext), nonce.length);

    return this.arrayBufferToBase64(result);
  }

  async decrypt(ciphertextBase64: string): Promise<string> {
    if (!this.sessionKey) {
      throw new Error('Session key not established');
    }

    const data = this.base64ToArrayBuffer(ciphertextBase64);

    if (data.byteLength < 28) {
      throw new Error('Ciphertext too short');
    }

    // Extract nonce and ciphertext
    const nonce = data.slice(0, 12);
    const ciphertext = data.slice(12);

    // AES-GCM鍵をインポート
    const key = await crypto.subtle.importKey(
      'raw',
      this.sessionKey,
      { name: 'AES-GCM' },
      false,
      ['decrypt']
    );

    // 復号化
    const plaintext = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: nonce },
      key,
      ciphertext
    );

    const decoder = new TextDecoder();
    return decoder.decode(plaintext);
  }

  reset(): void {
    this.privateKey = null;
    this.sessionKey = null;
    this.sessionId = null;
  }

  private arrayBufferToBase64(buffer: ArrayBuffer | Uint8Array): string {
    const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
  }

  private base64ToArrayBuffer(base64: string): ArrayBuffer {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
  }
}
