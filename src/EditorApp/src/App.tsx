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

  const interopRef = React.useRef<any>(null);
  const lastRequestedStartRef = React.useRef<number>(0);

  const handleRequestLines = React.useCallback((startLine: number, lineCount: number) => {
    if (interopRef.current) {
      lastRequestedStartRef.current = startLine;
      interopRef.current.sendRequestLines(startLine, lineCount);
    }
  }, []);

  React.useEffect(() => {
    const interop = (window as any).createInteropService();
    interopRef.current = interop;

    interop.onFileOpened((data: FileMeta) => {
      setFileMeta(data);
      setIsLoading(false);
      setError(null);
      setTitleBarText(`${data.fileName} - Editor`);
      // Request initial lines
      lastRequestedStartRef.current = 0;
      interop.sendRequestLines(0, 50);
    });

    interop.onLinesResponse((data: LinesResponsePayload) => {
      // Only accept the response if it matches the most recently requested startLine.
      // This prevents stale responses from overwriting the current view during fast scrolling.
      if (data.startLine === lastRequestedStartRef.current) {
        setLines(data.lines);
        setLinesStartLine(data.startLine);
      }
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
        onRequestLines: handleRequestLines,
      })}
      {React.createElement(StatusBarComponent, { metadata: fileMeta })}
    </div>
  );
}

// Mount the React app
ReactDOM.createRoot(document.getElementById('root')!).render(React.createElement(App));
