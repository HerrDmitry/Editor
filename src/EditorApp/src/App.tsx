// App.tsx — compiled by tsc to wwwroot/js/App.js
// No module imports — React, ReactDOM, and all components are globals
// exposed on window by their respective script files.

interface FileMeta {
  fileName: string;
  totalLines: number;
  fileSizeBytes: number;
  encoding: string;
  isPartial: boolean;
  isRefresh: boolean;
}

interface LinesResponsePayload {
  startLine: number;
  lines: string[];
  totalLines: number;
}

interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}

interface FileLoadProgressPayload {
  fileName: string;
  percent: number;
  fileSizeBytes: number;
}

/** Lines to request from backend per fetch (matches ContentArea FETCH_SIZE). */
const APP_FETCH_SIZE = 200;

function App() {
  const [fileMeta, setFileMeta] = React.useState<FileMeta | null>(null);
  const [lines, setLines] = React.useState<string[] | null>(null);
  const [linesStartLine, setLinesStartLine] = React.useState(0);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<ErrorInfo | null>(null);
  const [titleBarText, setTitleBarText] = React.useState('Editor');
  const [wrapLines, setWrapLines] = React.useState(false);
  const [loadProgress, setLoadProgress] = React.useState<FileLoadProgressPayload | null>(null);

  const interopRef = React.useRef<any>(null);
  const lastRequestedStartRef = React.useRef<number>(0);
  const isJumpRequestRef = React.useRef(false);

  // Use refs to track current buffer for merging in the async callback.
  const linesRef = React.useRef<string[] | null>(null);
  const linesStartRef = React.useRef(0);

  // Track current fileMeta via ref to avoid stale closure in onFileOpened callback.
  const fileMetaRef = React.useRef<FileMeta | null>(null);

  // Keep refs in sync with state.
  React.useEffect(() => { linesRef.current = lines; }, [lines]);
  React.useEffect(() => { linesStartRef.current = linesStartLine; }, [linesStartLine]);
  React.useEffect(() => { fileMetaRef.current = fileMeta; }, [fileMeta]);

  const handleWrapLinesChange = React.useCallback((enabled: boolean) => {
    setWrapLines(enabled);
  }, []);

  const handleRequestLines = React.useCallback((startLine: number, lineCount: number) => {
    if (interopRef.current) {
      lastRequestedStartRef.current = startLine;
      interopRef.current.sendRequestLines(startLine, lineCount);
    }
  }, []);

  // Called by ContentArea for scrollbar jump — replace buffer entirely.
  const handleJumpToLine = React.useCallback((startLine: number, lineCount: number) => {
    if (interopRef.current) {
      isJumpRequestRef.current = true;
      lastRequestedStartRef.current = startLine;
      interopRef.current.sendRequestLines(startLine, lineCount);
    }
  }, []);

  // Called by ContentArea when it trims the buffer after measuring DOM heights.
  const handleTrimBuffer = React.useCallback((newStart: number, newLines: string[]) => {
    setLinesStartLine(newStart);
    setLines(newLines);
  }, []);

  React.useEffect(() => {
    const interop = (window as any).createInteropService();
    interopRef.current = interop;

    interop.onFileOpened((data: FileMeta) => {
      if (data.isRefresh) {
        // Refresh — preserve scroll position, re-request buffer
        const currentStart = linesStartRef.current;
        const currentCount = linesRef.current ? linesRef.current.length : 0;

        // Update metadata (totalLines may have changed)
        setFileMeta(data);
        setError(null);

        if (data.totalLines === 0) {
          // File emptied
          setLines(null);
          setLinesStartLine(0);
          return;
        }

        // Clamp if file shrunk past current position
        let newStart = currentStart;
        const bufferLen = Math.max(currentCount, APP_FETCH_SIZE);
        if (newStart >= data.totalLines) {
          newStart = Math.max(0, data.totalLines - bufferLen);
          setLinesStartLine(newStart);
        }

        // Re-request current buffer range
        const count = Math.min(bufferLen, data.totalLines - newStart);
        lastRequestedStartRef.current = newStart;
        isJumpRequestRef.current = true; // Replace buffer entirely
        interop.sendRequestLines(newStart, count);
        return;
      }

      if (data.isPartial) {
        // Partial metadata — show content immediately
        setFileMeta(data);
        setIsLoading(false);
        setError(null);
        setTitleBarText(`${data.fileName} - Editor`);
        // DON'T clear loadProgress — progress bar stays visible
        // Request initial lines
        lastRequestedStartRef.current = 0;
        interop.sendRequestLines(0, 200);
      } else {
        // Final metadata
        if (fileMetaRef.current && fileMetaRef.current.fileName === data.fileName) {
          // Same file scan complete — update metadata and re-request current buffer
          // so displayed content reflects the fully-indexed file.
          const currentStart = linesStartRef.current;
          const currentCount = linesRef.current ? linesRef.current.length : 0;
          setFileMeta(data);
          setLoadProgress(null);

          // Re-request current buffer range to refresh content
          const bufferLen = Math.max(currentCount, APP_FETCH_SIZE);
          const newStart = Math.min(currentStart, Math.max(0, data.totalLines - bufferLen));
          const count = Math.min(bufferLen, data.totalLines - newStart);
          lastRequestedStartRef.current = newStart;
          isJumpRequestRef.current = true;
          interop.sendRequestLines(newStart, count);
        } else {
          // Different file (small file or first open) — full reset
          setFileMeta(data);
          setIsLoading(false);
          setError(null);
          setLoadProgress(null);
          setTitleBarText(`${data.fileName} - Editor`);
          lastRequestedStartRef.current = 0;
          interop.sendRequestLines(0, 200);
        }
      }
    });

    interop.onLinesResponse((data: LinesResponsePayload) => {
      // Jump request — replace buffer entirely, don't merge
      if (isJumpRequestRef.current) {
        isJumpRequestRef.current = false;
        setLines(data.lines);
        setLinesStartLine(data.startLine);
        return;
      }

      const prevLines = linesRef.current;
      const prevStart = linesStartRef.current;

      if (!prevLines || prevLines.length === 0) {
        // First load
        setLines(data.lines);
        setLinesStartLine(data.startLine);
        return;
      }

      const prevEnd = prevStart + prevLines.length;
      const newEnd = data.startLine + data.lines.length;

      // Calculate merged range
      const mergedStart = Math.min(prevStart, data.startLine);
      const mergedEnd = Math.max(prevEnd, newEnd);

      // Build merged array
      const merged: string[] = new Array(mergedEnd - mergedStart).fill('');

      // Fill with existing lines
      for (let i = 0; i < prevLines.length; i++) {
        merged[prevStart - mergedStart + i] = prevLines[i];
      }

      // Overwrite/fill with new lines
      for (let i = 0; i < data.lines.length; i++) {
        merged[data.startLine - mergedStart + i] = data.lines[i];
      }

      // No trimming here — just merge and pass to ContentArea.
      // ContentArea will handle trimming with scroll position adjustment.
      setLines(merged);
      setLinesStartLine(mergedStart);
    });

    interop.onError((err: ErrorInfo) => {
      setError(err);
      setIsLoading(false);
      setLoadProgress(null);
    });

    interop.onFileLoadProgress((data: FileLoadProgressPayload) => {
      if (data.percent === 100) {
        setLoadProgress(null);
      } else {
        setLoadProgress(data);
      }
    });

    // Ctrl+O / Cmd+O keyboard shortcut
    function handleKeyDown(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'o' || e.key === 'O')) {
        e.preventDefault();
        setIsLoading(true);
        setError(null);
        interop.sendOpenFileRequest();
      }
    }

    window.addEventListener('keydown', handleKeyDown);

    return () => {
      interop.dispose();
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, []);

  // Reference global components from window
  const TitleBarComponent = (window as any).TitleBar;
  const ContentAreaComponent = (window as any).ContentArea;
  const StatusBarComponent = (window as any).StatusBar;

  return (
    <div className="app">
      {React.createElement(TitleBarComponent, { title: titleBarText })}
      {React.createElement(ContentAreaComponent, {
        fileMeta,
        lines,
        linesStartLine,
        isLoading,
        error,
        wrapLines,
        onRequestLines: handleRequestLines,
        onJumpToLine: handleJumpToLine,
        onTrimBuffer: handleTrimBuffer,
      })}
      {React.createElement(StatusBarComponent, {
        metadata: fileMeta,
        wrapLines,
        onWrapLinesChange: handleWrapLinesChange,
        loadProgress,
      })}
    </div>
  );
}

// Mount the React app
ReactDOM.createRoot(document.getElementById('root')!).render(React.createElement(App));
