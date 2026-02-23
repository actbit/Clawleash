// E2EE暗号化ユーティリティ

export class E2eeProvider {
  private privateKey: Uint8Array | null = null;
  private sessionKey: Uint8Array | null = null;
  private sessionId: string | null = null;
  private channelKeys: Map<string, Uint8Array> = new Map();

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

  /// <summary>
  /// 暗号化されたチャンネル鍵を設定
  /// </summary>
  async setChannelKey(channelId: string, encryptedKeyBase64: string): Promise<void> {
    if (!this.sessionKey) {
      throw new Error('Session key not established');
    }

    const encryptedData = this.base64ToArrayBuffer(encryptedKeyBase64);

    // nonce(12) + ciphertext + tag(16) フォーマット
    const nonce = new Uint8Array(encryptedData.slice(0, 12));
    const ciphertextWithTag = new Uint8Array(encryptedData.slice(12));
    const ciphertextLength = ciphertextWithTag.length - 16;
    const ciphertext = ciphertextWithTag.slice(0, ciphertextLength);
    const tag = ciphertextWithTag.slice(ciphertextLength);

    // セッション鍵でAES-GCM鍵をインポート
    const key = await crypto.subtle.importKey(
      'raw',
      this.sessionKey,
      { name: 'AES-GCM' },
      false,
      ['decrypt']
    );

    // ciphertextとtagを結合して復号化
    const ciphertextFull = new Uint8Array(ciphertext.length + tag.length);
    ciphertextFull.set(ciphertext, 0);
    ciphertextFull.set(tag, ciphertext.length);

    const channelKey = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: nonce },
      key,
      ciphertextFull
    );

    this.channelKeys.set(channelId, new Uint8Array(channelKey));
    console.log(`Channel key set for ${channelId}`);
  }

  /// <summary>
  /// 平文のチャンネル鍵を設定（E2EE無効時）
  /// </summary>
  setPlainChannelKey(channelId: string, plainKeyBase64: string): void {
    const channelKey = this.base64ToArrayBuffer(plainKeyBase64);
    this.channelKeys.set(channelId, new Uint8Array(channelKey));
    console.warn(`Plain channel key set for ${channelId} (E2EE disabled)`);
  }

  /// <summary>
  /// チャンネル鍵を使用して暗号化
  /// </summary>
  async encrypt(plaintext: string, channelId?: string): Promise<string> {
    const keyToUse = channelId && this.channelKeys.has(channelId)
      ? this.channelKeys.get(channelId)!
      : this.sessionKey;

    if (!keyToUse) {
      throw new Error('No key available for encryption');
    }

    const encoder = new TextEncoder();
    const plaintextBytes = encoder.encode(plaintext);

    // Nonce (12 bytes)
    const nonce = crypto.getRandomValues(new Uint8Array(12));

    // AES-GCM鍵をインポート
    const key = await crypto.subtle.importKey(
      'raw',
      keyToUse,
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

    // nonce + ciphertext (ciphertextにはtagが含まれる)
    const result = new Uint8Array(nonce.length + ciphertext.byteLength);
    result.set(nonce, 0);
    result.set(new Uint8Array(ciphertext), nonce.length);

    return this.arrayBufferToBase64(result);
  }

  /// <summary>
  /// チャンネル鍵を使用して復号化
  /// </summary>
  async decrypt(ciphertextBase64: string, channelId?: string): Promise<string> {
    const keyToUse = channelId && this.channelKeys.has(channelId)
      ? this.channelKeys.get(channelId)!
      : this.sessionKey;

    if (!keyToUse) {
      throw new Error('No key available for decryption');
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
      keyToUse,
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

  hasChannelKey(channelId: string): boolean {
    return this.channelKeys.has(channelId);
  }

  reset(): void {
    this.privateKey = null;
    this.sessionKey = null;
    this.sessionId = null;
    this.channelKeys.clear();
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
