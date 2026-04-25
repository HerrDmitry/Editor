import type { FileContent, ErrorInfo, WarningInfo, MessageEnvelope } from '../types/messages';
import { MessageTypes } from '../types/messages';

/**
 * Timeout in milliseconds for detecting an unresponsive backend.
 * If no response arrives within this window after sending a request,
 * the interop is considered failed (Requirement 7.3).
 */
const INTEROP_TIMEOUT_MS = 5_000;

/**
 * Service that manages communication between the React frontend and the
 * C# Photino backend via the web message interop bridge.
 *
 * Outgoing messages use `window.external.sendMessage(json)`.
 * Incoming messages arrive as `MessageEvent` on the window (triggered by
 * Photino's `SendWebMessage`).
 */
export interface InteropService {
  /** Send a request to the backend to open the native file picker. */
  sendOpenFileRequest(): void;

  /** Register a callback for successful file-loaded responses. */
  onFileLoaded(callback: (data: FileContent) => void): void;

  /** Register a callback for error responses from the backend. */
  onError(callback: (error: ErrorInfo) => void): void;

  /** Register a callback for warning responses from the backend. */
  onWarning(callback: (warning: WarningInfo) => void): void;

  /** Remove all registered listeners. Call on unmount / cleanup. */
  dispose(): void;
}

/**
 * Create a concrete {@link InteropService} instance.
 *
 * The factory wires up a single `message` event listener on the window and
 * dispatches incoming messages to the appropriate registered callbacks based
 * on the `type` field of the {@link MessageEnvelope}.
 */
export function createInteropService(): InteropService {
  // Callback registries — each type can have multiple subscribers.
  const fileLoadedCallbacks: Array<(data: FileContent) => void> = [];
  const errorCallbacks: Array<(error: ErrorInfo) => void> = [];
  const warningCallbacks: Array<(warning: WarningInfo) => void> = [];

  let timeoutId: ReturnType<typeof setTimeout> | null = null;

  // ------------------------------------------------------------------
  // Incoming message handler
  // ------------------------------------------------------------------

  function handleMessage(rawData: string): void {
    // Photino delivers messages as strings via window.external.receiveMessage.
    let envelope: MessageEnvelope;
    try {
      envelope = JSON.parse(rawData) as MessageEnvelope;
    } catch {
      // Ignore messages that aren't valid JSON envelopes.
      return;
    }

    // Any valid response from the backend clears the pending timeout.
    clearPendingTimeout();

    switch (envelope.type) {
      case MessageTypes.FileLoadedResponse:
        for (const cb of fileLoadedCallbacks) {
          cb(envelope.payload as FileContent);
        }
        break;

      case MessageTypes.ErrorResponse:
        for (const cb of errorCallbacks) {
          cb(envelope.payload as ErrorInfo);
        }
        break;

      case MessageTypes.WarningResponse:
        for (const cb of warningCallbacks) {
          cb(envelope.payload as WarningInfo);
        }
        break;

      default:
        break;
    }
  }

  // Register via Photino's receiveMessage callback.
  // Photino injects window.external.receiveMessage for C# → JS messages.
  if (window.external && 'receiveMessage' in window.external) {
    window.external.receiveMessage(handleMessage);
  }

  // Also listen on the standard DOM message event as a fallback.
  function handleDomMessage(event: MessageEvent): void {
    const data = typeof event.data === 'string' ? event.data : JSON.stringify(event.data);
    handleMessage(data);
  }
  window.addEventListener('message', handleDomMessage);

  // ------------------------------------------------------------------
  // Timeout helpers
  // ------------------------------------------------------------------

  function clearPendingTimeout(): void {
    if (timeoutId !== null) {
      clearTimeout(timeoutId);
      timeoutId = null;
    }
  }

  function startTimeout(): void {
    clearPendingTimeout();
    timeoutId = setTimeout(() => {
      timeoutId = null;
      const interopError: ErrorInfo = {
        errorCode: 'INTEROP_FAILURE',
        message: 'The application is not responding. Please restart.',
      };
      for (const cb of errorCallbacks) {
        cb(interopError);
      }
    }, INTEROP_TIMEOUT_MS);
  }

  // ------------------------------------------------------------------
  // Public API
  // ------------------------------------------------------------------

  return {
    sendOpenFileRequest(): void {
      const envelope: MessageEnvelope = {
        type: MessageTypes.OpenFileRequest,
        timestamp: new Date().toISOString(),
      };

      try {
        window.external.sendMessage(JSON.stringify(envelope));
        startTimeout();
      } catch {
        // If sendMessage itself throws (e.g. running outside Photino),
        // surface it as an interop failure immediately.
        const interopError: ErrorInfo = {
          errorCode: 'INTEROP_FAILURE',
          message: 'The application is not responding. Please restart.',
        };
        for (const cb of errorCallbacks) {
          cb(interopError);
        }
      }
    },

    onFileLoaded(callback: (data: FileContent) => void): void {
      fileLoadedCallbacks.push(callback);
    },

    onError(callback: (error: ErrorInfo) => void): void {
      errorCallbacks.push(callback);
    },

    onWarning(callback: (warning: WarningInfo) => void): void {
      warningCallbacks.push(callback);
    },

    dispose(): void {
      clearPendingTimeout();
      window.removeEventListener('message', handleDomMessage);
      fileLoadedCallbacks.length = 0;
      errorCallbacks.length = 0;
      warningCallbacks.length = 0;
    },
  };
}
