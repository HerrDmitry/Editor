// App.tsx — compiled by tsc to wwwroot/js/App.js
// No module imports — React, ReactDOM, and all components are globals
// exposed on window by their respective script files.

interface FileContent {
  content: string;
  filePath: string;
  fileName: string;
  metadata: {
    fileSizeBytes: number;
    lineCount: number;
    encoding: string;
    lastModified: string;
  };
}

interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}

interface WarningInfo {
  warningCode: string;
  message: string;
  filePath: string;
  fileSizeBytes: number;
}

function App() {
  const [fileContent, setFileContent] = React.useState<FileContent | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<ErrorInfo | null>(null);
  const [warning, setWarning] = React.useState<WarningInfo | null>(null);
  const [titleBarText, setTitleBarText] = React.useState('Editor');

  const handleDismissWarning = React.useCallback(() => {
    setWarning(null);
  }, []);

  React.useEffect(() => {
    const interop = (window as any).createInteropService();

    interop.onFileLoaded((data: FileContent) => {
      setFileContent(data);
      setIsLoading(false);
      setError(null);
      setWarning(null);
      setTitleBarText(`${data.fileName} - Editor`);
    });

    interop.onError((err: ErrorInfo) => {
      setError(err);
      setIsLoading(false);
      setWarning(null);
    });

    interop.onWarning((warn: WarningInfo) => {
      setWarning(warn);
    });

    // Ctrl+O / Cmd+O keyboard shortcut
    function handleKeyDown(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'o' || e.key === 'O')) {
        e.preventDefault();
        setIsLoading(true);
        setError(null);
        setWarning(null);
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
      {warning && (
        <div className="warning-banner" role="alert">
          <span className="warning-banner__icon" aria-hidden="true">⚠</span>
          <span className="warning-banner__message">{warning.message}</span>
          <button
            className="warning-banner__dismiss"
            onClick={handleDismissWarning}
            aria-label="Dismiss warning"
          >
            ✕
          </button>
        </div>
      )}
      {React.createElement(ContentAreaComponent, { fileContent, isLoading, error })}
      {React.createElement(StatusBarComponent, { metadata: fileContent?.metadata ?? null })}
    </div>
  );
}

// Mount the React app
ReactDOM.createRoot(document.getElementById('root')!).render(React.createElement(App));
