/**
 * Type declarations for Photino's JavaScript interop bridge.
 *
 * Photino injects `window.external.sendMessage` and `window.external.receiveMessage`
 * into the webview for bidirectional communication with the C# backend.
 */

interface External {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}
