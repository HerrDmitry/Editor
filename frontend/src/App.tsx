import { useState, useEffect, useCallback } from 'react';
import type { FileContent, ErrorInfo, WarningInfo } from './types/messages';
import { createInteropService } from './services/interopService';
import TitleBar from './components/TitleBar';
import ContentArea from './components/ContentArea';
import StatusBar from './components/StatusBar';
import './App.css';

function App() {
  const [fileContent, setFileContent] = useState<FileContent | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<ErrorInfo | null>(null);
  const [warning, setWarning] = useState<WarningInfo | null>(null);
  const [titleBarText, setTitleBarText] = useState('Editor');

  const handleDismissWarning = useCallback(() => {
    setWarning(null);
  }, []);

  useEffect(() => {
    const interop = createInteropService();

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

  return (
    <div className="app">
      <TitleBar title={titleBarText} />
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
      <ContentArea fileContent={fileContent} isLoading={isLoading} error={error} />
      <StatusBar metadata={fileContent?.metadata ?? null} />
    </div>
  );
}

export default App;
