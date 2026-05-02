// InteropService.ts — compiled by tsc to wwwroot/js/InteropService.js
// No module imports — types are ambient declarations.

/**
 * Timeout in milliseconds for detecting an unresponsive backend.
 * If no response arrives within this window after sending a request,
 * the interop is considered failed (Requirement 7.3).
 */
const INTEROP_TIMEOUT_MS = 5_000;

/** Metadata about an opened file (from FileOpenedResponse). */
interface FileMeta {
  fileName: string;
  totalLines: number;
  fileSizeBytes: number;
  encoding: string;
  isPartial: boolean;
  isRefresh: boolean;
}

/** Payload from a LinesResponse message. */
interface LinesResponsePayload {
  startLine: number;
  lines: string[];
  totalLines: number;
}

/** Error information sent from the backend. */
interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}

/** Payload from a FileLoadProgressMessage. */
interface FileLoadProgressPayload {
  fileName: string;
  percent: number;
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
  RequestLinesMessage: 'RequestLinesMessage',
  FileOpenedResponse: 'FileOpenedResponse',
  LinesResponse: 'LinesResponse',
  ErrorResponse: 'ErrorResponse',
  FileLoadProgressMessage: 'FileLoadProgressMessage',
} as const;

/**
 * Service that manages communication between the React frontend and the
 * C# Photino backend via the web message interop bridge.
 */
interface InteropService {
  sendOpenFileRequest(): void;
  sendRequestLines(startLine: number, lineCount: number): void;
  onFileOpened(callback: (data: FileMeta) => void): void;
  onLinesResponse(callback: (data: LinesResponsePayload) => void): void;
  onFileLoadProgress(callback: (data: FileLoadProgressPayload) => void): void;
  onError(callback: (error: ErrorInfo) => void): void;
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
  const fileOpenedCallbacks: Array<(data: FileMeta) => void> = [];
  const linesResponseCallbacks: Array<(data: LinesResponsePayload) => void> = [];
  const fileLoadProgressCallbacks: Array<(data: FileLoadProgressPayload) => void> = [];
  const errorCallbacks: Array<(error: ErrorInfo) => void> = [];

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
      case MessageTypes.FileOpenedResponse:
        for (const cb of fileOpenedCallbacks) {
          cb(envelope.payload as FileMeta);
        }
        break;

      case MessageTypes.LinesResponse:
        for (const cb of linesResponseCallbacks) {
          cb(envelope.payload as LinesResponsePayload);
        }
        break;

      case MessageTypes.ErrorResponse:
        for (const cb of errorCallbacks) {
          cb(envelope.payload as ErrorInfo);
        }
        break;

      case MessageTypes.FileLoadProgressMessage:
        for (const cb of fileLoadProgressCallbacks) {
          cb(envelope.payload as FileLoadProgressPayload);
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

    sendRequestLines(startLine: number, lineCount: number): void {
      const envelope: MessageEnvelope = {
        type: MessageTypes.RequestLinesMessage,
        payload: { startLine, lineCount },
        timestamp: new Date().toISOString(),
      };

      try {
        (window as any).external.sendMessage(JSON.stringify(envelope));
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

    onFileOpened(callback: (data: FileMeta) => void): void {
      fileOpenedCallbacks.push(callback);
    },

    onLinesResponse(callback: (data: LinesResponsePayload) => void): void {
      linesResponseCallbacks.push(callback);
    },

    onError(callback: (error: ErrorInfo) => void): void {
      errorCallbacks.push(callback);
    },

    onFileLoadProgress(callback: (data: FileLoadProgressPayload) => void): void {
      fileLoadProgressCallbacks.push(callback);
    },

    dispose(): void {
      clearPendingTimeout();
      window.removeEventListener('message', handleDomMessage);
      fileOpenedCallbacks.length = 0;
      linesResponseCallbacks.length = 0;
      fileLoadProgressCallbacks.length = 0;
      errorCallbacks.length = 0;
    },
  };
}

// Expose on window for use by other scripts
(window as any).createInteropService = createInteropService;
