import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import App from './App';
import type { WarningInfo, ErrorInfo, FileContent } from './types/messages';

// Mock window.external.sendMessage so InteropService doesn't throw
beforeEach(() => {
  (window as unknown as Record<string, unknown>).external = {
    sendMessage: vi.fn(),
  };
});

/**
 * Helper: simulate a backend message arriving via the Photino bridge.
 */
function simulateBackendMessage(type: string, payload: unknown) {
  const envelope = {
    type,
    payload,
    timestamp: new Date().toISOString(),
  };
  window.dispatchEvent(
    new MessageEvent('message', { data: JSON.stringify(envelope) }),
  );
}

describe('App', () => {
  it('renders the application shell with title bar', () => {
    render(<App />);
    expect(screen.getByText('Editor')).toBeInTheDocument();
  });

  it('shows empty prompt when no file is open', () => {
    render(<App />);
    expect(screen.getByText('Press Ctrl+O to open a file')).toBeInTheDocument();
  });

  it('displays warning banner when WarningResponse is received', () => {
    render(<App />);

    const warning: WarningInfo = {
      warningCode: 'LARGE_FILE',
      message: 'This file is 15.0 MB. Loading may take a moment.',
      filePath: '/path/to/large.txt',
      fileSizeBytes: 15 * 1024 * 1024,
    };

    act(() => {
      simulateBackendMessage('WarningResponse', warning);
    });

    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText(warning.message)).toBeInTheDocument();
  });

  it('dismisses warning banner when dismiss button is clicked', () => {
    render(<App />);

    const warning: WarningInfo = {
      warningCode: 'LARGE_FILE',
      message: 'This file is 15.0 MB. Loading may take a moment.',
      filePath: '/path/to/large.txt',
      fileSizeBytes: 15 * 1024 * 1024,
    };

    act(() => {
      simulateBackendMessage('WarningResponse', warning);
    });

    expect(screen.getByRole('alert')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Dismiss warning'));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('clears warning when a new file is loaded', () => {
    render(<App />);

    const warning: WarningInfo = {
      warningCode: 'LARGE_FILE',
      message: 'This file is 15.0 MB. Loading may take a moment.',
      filePath: '/path/to/large.txt',
      fileSizeBytes: 15 * 1024 * 1024,
    };

    act(() => {
      simulateBackendMessage('WarningResponse', warning);
    });

    expect(screen.getByRole('alert')).toBeInTheDocument();

    const fileContent: FileContent = {
      content: 'hello world',
      filePath: '/path/to/file.txt',
      fileName: 'file.txt',
      metadata: {
        fileSizeBytes: 11,
        lineCount: 1,
        encoding: 'UTF-8',
        lastModified: new Date().toISOString(),
      },
    };

    act(() => {
      simulateBackendMessage('FileLoadedResponse', fileContent);
    });

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('displays error message when ErrorResponse is received', () => {
    render(<App />);

    const error: ErrorInfo = {
      errorCode: 'FILE_NOT_FOUND',
      message: 'The selected file could not be found.',
    };

    act(() => {
      simulateBackendMessage('ErrorResponse', error);
    });

    expect(screen.getByText(error.message)).toBeInTheDocument();
  });

  it('clears error state when new file open is triggered', () => {
    render(<App />);

    const error: ErrorInfo = {
      errorCode: 'FILE_NOT_FOUND',
      message: 'The selected file could not be found.',
    };

    act(() => {
      simulateBackendMessage('ErrorResponse', error);
    });

    expect(screen.getByText(error.message)).toBeInTheDocument();

    // Trigger Ctrl+O to open a new file — this should clear the error
    fireEvent.keyDown(window, { key: 'o', ctrlKey: true });

    // Error should be cleared, loading should be shown
    expect(screen.queryByText(error.message)).not.toBeInTheDocument();
    expect(screen.getByText('Loading file...')).toBeInTheDocument();
  });
});
