/**
 * TypeScript interfaces and types for frontend-backend interop messages.
 * These mirror the C# models in src/EditorApp/Models/ and conform to the
 * MessageEnvelope schema used by Photino's web message bridge.
 */

// ---------------------------------------------------------------------------
// Domain data models
// ---------------------------------------------------------------------------

/** Metadata about a loaded file. */
export interface FileMetadata {
  fileSizeBytes: number;
  lineCount: number;
  encoding: string;
  /** ISO 8601 date string */
  lastModified: string;
}

/** Full content and metadata of a loaded file. */
export interface FileContent {
  content: string;
  filePath: string;
  fileName: string;
  metadata: FileMetadata;
}

/** Error information sent from the backend. */
export interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}

/** Warning information sent from the backend (e.g. large file). */
export interface WarningInfo {
  warningCode: string;
  message: string;
  filePath: string;
  fileSizeBytes: number;
}

// ---------------------------------------------------------------------------
// Message envelope
// ---------------------------------------------------------------------------

/** Common envelope for all messages exchanged between backend and frontend. */
export interface MessageEnvelope {
  type: string;
  payload?: unknown;
  /** ISO 8601 date string */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Message type constants
// ---------------------------------------------------------------------------

/** Constants for message types used in the interop protocol. */
export const MessageTypes = {
  /** Frontend → Backend: request to open a file via native picker */
  OpenFileRequest: 'OpenFileRequest',
  /** Backend → Frontend: file loaded successfully */
  FileLoadedResponse: 'FileLoadedResponse',
  /** Backend → Frontend: an error occurred */
  ErrorResponse: 'ErrorResponse',
  /** Backend → Frontend: a warning (e.g. large file) */
  WarningResponse: 'WarningResponse',
} as const;

export type MessageType = (typeof MessageTypes)[keyof typeof MessageTypes];
