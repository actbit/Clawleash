// E2EE暗号化ユーティリティ
// ECDH-P256鍵交換 + AES-256-GCM暗号化

export class E2eeProvider {
  private keyPair: CryptoKeyPair | null = null;
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
    this.keyPair = await crypto.subtle.generateKey(
      { name: 'ECDH', namedCurve: 'P-256' },
      true,
      ['deriveBits']
    );
  }

  async getPublicKey(): Promise<Uint8Array> {
    if (!this.keyPair) {
      throw new Error('Not initialized');
    }

    const exported = await crypto.subtle.exportKey('spki', this.keyPair.publicKey);
    return new Uint8Array(exported);
  }

  async startKeyExchange(): Promise<{ sessionId: string; publicKey: string }> {
    if (!this.keyPair) {
      await this.initialize();
    }

    this.sessionId = crypto.randomUUID();

    return {
      sessionId: this.sessionId,
      publicKey: this.arrayBufferToBase64(await this.getPublicKey())
    };
  }

  async completeKeyExchange(peerPublicKeyBase64: string): Promise<void> {
    if (!this.keyPair) {
      throw new Error('Not initialized');
    }

    const peerPublicKeyBuffer = this.base64ToArrayBuffer(peerPublicKeyBase64);
    const peerPublicKey = await crypto.subtle.importKey(
      'spki',
      peerPublicKeyBuffer,
      { name: 'ECDH', namedCurve: 'P-256' },
      false,
      []
    );

    // ECDH共有秘密を導出
    const sharedBits = await crypto.subtle.deriveBits(
      { name: 'ECDH', public: peerPublicKey },
      this.keyPair.privateKey,
      256
    );

    // SHA-256でハッシュ（サーバーのDeriveKeyFromHashと同じ処理）
    this.sessionKey = new Uint8Array(await crypto.subtle.digest('SHA-256', sharedBits));
  }

  async setChannelKey(channelId: string, encryptedKeyBase64: string): Promise<void> {
    if (!this.sessionKey) {
      throw new Error('Session key not established');
    }

    const encryptedData = this.base64ToArrayBuffer(encryptedKeyBase64);

    const nonce = new Uint8Array(encryptedData.slice(0, 12));
    const ciphertextWithTag = new Uint8Array(encryptedData.slice(12));
    const ciphertextLength = ciphertextWithTag.length - 16;
    const ciphertext = ciphertextWithTag.slice(0, ciphertextLength);
    const tag = ciphertextWithTag.slice(ciphertextLength);

    const key = await crypto.subtle.importKey(
      'raw',
      this.sessionKey,
      { name: 'AES-GCM' },
      false,
      ['decrypt']
    );

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

  setPlainChannelKey(channelId: string, plainKeyBase64: string): void {
    const channelKey = this.base64ToArrayBuffer(plainKeyBase64);
    this.channelKeys.set(channelId, new Uint8Array(channelKey));
    console.warn(`Plain channel key set for ${channelId} (E2EE disabled)`);
  }

  async encrypt(plaintext: string, channelId?: string): Promise<string> {
    const keyToUse = channelId && this.channelKeys.has(channelId)
      ? this.channelKeys.get(channelId)!
      : this.sessionKey;

    if (!keyToUse) {
      throw new Error('No key available for encryption');
    }

    const encoder = new TextEncoder();
    const plaintextBytes = encoder.encode(plaintext);

    const nonce = crypto.getRandomValues(new Uint8Array(12));

    const key = await crypto.subtle.importKey(
      'raw',
      keyToUse,
      { name: 'AES-GCM' },
      false,
      ['encrypt']
    );

    const ciphertext = await crypto.subtle.encrypt(
      { name: 'AES-GCM', iv: nonce },
      key,
      plaintextBytes
    );

    // nonce(12) + ciphertext + tag
    const result = new Uint8Array(nonce.length + ciphertext.byteLength);
    result.set(nonce, 0);
    result.set(new Uint8Array(ciphertext), nonce.length);

    return this.arrayBufferToBase64(result);
  }

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

    const nonce = data.slice(0, 12);
    const ciphertext = data.slice(12);

    const key = await crypto.subtle.importKey(
      'raw',
      keyToUse,
      { name: 'AES-GCM' },
      false,
      ['decrypt']
    );

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
    this.keyPair = null;
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
