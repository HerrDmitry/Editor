// InteropService.ts — compiled by tsc to wwwroot/js/InteropService.js
// No module imports — types are ambient declarations.

/**
 * Timeout in milliseconds for detecting an unresponsive backend.
 * If no response arrives within this window after sending a request,
 * the interop is considered failed (Requirement 7.3).
 */
const INTEROP_TIMEOUT_MS = 5_000;

/** Metadata about a loaded file. */
interface FileMetadata {
  fileSizeBytes: number;
  lineCount: number;
  encoding: string;
  lastModified: string;
}

/** Full content and metadata of a loaded file. */
interface FileContent {
  content: string;
  filePath: string;
  fileName: string;
  metadata: FileMetadata;
}

/** Error information sent from the backend. */
interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}

/** Warning information sent from the backend (e.g. large file). */
interface WarningInfo {
  warningCode: string;
  message: string;
  filePath: string;
  fileSizeBytes: number;
}

/** Common envelope for all messages exchanged between backend and frontend. */
interface MessageEnvelope {
  type: string;
  payload?: any;
  timestamp: string;
}

/** Constants for message types used in the interop protocol. */
const MessageTypes = {
  OpenFileRequest: 'OpenFileRequest',
  FileLoadedResponse: 'FileLoadedResponse',
  ErrorResponse: 'ErrorResponse',
  WarningResponse: 'WarningResponse',
} as const;

/**
 * Service that manages communication between the React frontend and the
 * C# Photino backend via the web message interop bridge.
 */
interface InteropService {
  sendOpenFileRequest(): void;
  onFileLoaded(callback: (data: FileContent) => void): void;
  onError(callback: (error: ErrorInfo) => void): void;
  onWarning(callback: (warning: WarningInfo) => void): void;
  dispose(): void;
}

/**
 * Create a concrete InteropService instance.
 *
 * The factory wires up a single receiveMessage callback and dispatches
 * incoming messages to the appropriate registered callbacks based on
 * the type field of the MessageEnvelope.
 */
function createInteropService(): InteropService {
  const fileLoadedCallbacks: Array<(data: FileContent) => void> = [];
  const errorCallbacks: Array<(error: ErrorInfo) => void> = [];
  const warningCallbacks: Array<(warning: WarningInfo) => void> = [];

  let timeoutId: ReturnType<typeof setTimeout> | null = null;

  // ------------------------------------------------------------------
  // Incoming message handler
  // ------------------------------------------------------------------

  function handleMessage(rawData: string): void {
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
  if ((window as any).external && 'receiveMessage' in (window as any).external) {
    (window as any).external.receiveMessage(handleMessage);
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
        (window as any).external.sendMessage(JSON.stringify(envelope));
        startTimeout();
      } catch {
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

// Expose on window for use by other scripts
(window as any).createInteropService = createInteropService;
