// App.tsx — compiled by tsc to wwwroot/js/App.js
// No module imports — React, ReactDOM, and all components are globals
// exposed on window by their respective script files.

interface FileMeta {
  fileName: string;
  totalLines: number;
  fileSizeBytes: number;
  encoding: string;
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

function App() {
  const [fileMeta, setFileMeta] = React.useState<FileMeta | null>(null);
  const [lines, setLines] = React.useState<string[] | null>(null);
  const [linesStartLine, setLinesStartLine] = React.useState(0);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<ErrorInfo | null>(null);
  const [titleBarText, setTitleBarText] = React.useState('Editor');
  const [wrapLines, setWrapLines] = React.useState(false);

  const interopRef = React.useRef<any>(null);
  const lastRequestedStartRef = React.useRef<number>(0);
  const isJumpRequestRef = React.useRef(false);

  // Use refs to track current buffer for merging in the async callback.
  const linesRef = React.useRef<string[] | null>(null);
  const linesStartRef = React.useRef(0);

  // Keep refs in sync with state.
  React.useEffect(() => { linesRef.current = lines; }, [lines]);
  React.useEffect(() => { linesStartRef.current = linesStartLine; }, [linesStartLine]);

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
      setFileMeta(data);
      setIsLoading(false);
      setError(null);
      setTitleBarText(`${data.fileName} - Editor`);
      // Request initial lines — use a generous count to fill the buffer.
      // ContentArea will request more if needed once it knows the viewport size.
      lastRequestedStartRef.current = 0;
      interop.sendRequestLines(0, 200);
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
      })}
    </div>
  );
}

// Mount the React app
ReactDOM.createRoot(document.getElementById('root')!).render(React.createElement(App));
