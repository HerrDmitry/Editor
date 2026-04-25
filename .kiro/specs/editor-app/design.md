# Design Document: Editor App

## Overview

The Editor App is a cross-platform desktop application for read-only file viewing, built using a hybrid architecture that combines C# .NET 10 with Photino.Blazor for native window hosting and React/TypeScript for the user interface layer. The application compiles into a single self-contained executable that embeds all resources, eliminating external dependencies.

### Key Design Goals

1. **Cross-Platform Native Experience**: Leverage Photino.Blazor to provide native window management across Windows, macOS, and Linux while maintaining a consistent UI through React
2. **Single Executable Deployment**: Embed all resources (React bundle, static assets) into the compiled binary for zero-installation deployment
3. **Clear Separation of Concerns**: Maintain distinct boundaries between native backend (file I/O, OS integration) and web-based UI (rendering, user interaction)
4. **Performance for Large Files**: Implement progressive loading and size limits to maintain responsiveness
5. **Reliable Interop**: Establish robust message-passing between C# backend and React frontend

### Technology Stack

- **Backend**: C# .NET 10, Photino.NET, Photino.Blazor
- **Frontend**: React 18+, TypeScript 5+, Vite (build tool)
- **Interop**: Photino's JavaScript interop bridge (`window.external.sendMessage` / `ReceiveMessage`)
- **Packaging**: .NET publish with `PublishSingleFile=true` and `SelfContained=true`

## Architecture

### High-Level Architecture

The application follows a **two-tier architecture** with clear separation between native and web layers:

```
┌─────────────────────────────────────────────────────────┐
│                    Operating System                      │
│  (Windows / macOS / Linux)                              │
└────────────────┬────────────────────────────────────────┘
                 │
                 │ Native APIs
                 │
┌────────────────▼────────────────────────────────────────┐
│              C# Backend Layer                            │
│  ┌──────────────────────────────────────────────────┐  │
│  │  Photino.Blazor Host                             │  │
│  │  - Window Management                             │  │
│  │  - Native File Dialogs                           │  │
│  │  - File System Access                            │  │
│  │  - Message Router                                │  │
│  └──────────────────────────────────────────────────┘  │
└────────────────┬────────────────────────────────────────┘
                 │
                 │ Web Message Interop
                 │ (JSON over Photino bridge)
                 │
┌────────────────▼────────────────────────────────────────┐
│              React UI Layer                              │
│  ┌──────────────────────────────────────────────────┐  │
│  │  React Components                                │  │
│  │  - App Shell (Title Bar, Status Bar)            │  │
│  │  - File Viewer (Content Display)                │  │
│  │  - Loading States & Error Messages              │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │  State Management                                │  │
│  │  - File Content State                           │  │
│  │  - UI State (loading, errors)                   │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### Component Interaction Flow

**File Open Flow:**
1. User triggers open action (keyboard shortcut Ctrl+O or UI button)
2. React UI sends `OpenFileRequest` message to backend
3. Backend displays native file picker dialog
4. User selects file → Backend validates file size
5. Backend reads file contents and metadata
6. Backend sends `FileLoadedResponse` message to React UI
7. React UI updates state and renders file contents

**Error Handling Flow:**
1. Backend encounters error (permission denied, file not found, size limit)
2. Backend sends `ErrorResponse` message to React UI
3. React UI displays error message to user
4. Application returns to previous state

### Deployment Architecture

The application is packaged as a single executable using .NET's single-file publishing:

```
editor-app.exe (or editor-app on Unix)
├── .NET Runtime (embedded)
├── C# Application Code
├── Photino.NET Native Libraries
├── Embedded Resources
│   ├── index.html (Blazor host page)
│   └── wwwroot/
│       ├── assets/ (React bundle)
│       ├── index.js
│       └── index.css
```

## Components and Interfaces

### Backend Components

#### 1. PhotinoHostService

**Responsibility**: Initialize and manage the Photino window, handle application lifecycle.

**Key Methods**:
- `Initialize()`: Create Photino window, configure size/title, register message handlers
- `Run()`: Start the application event loop
- `Shutdown()`: Clean up resources and close window

**Configuration**:
- Window size: 1200x800 (default)
- Window title: "Editor"
- Resizable: true
- Centered on screen

#### 2. FileService

**Responsibility**: Handle all file system operations including reading files, validating sizes, and detecting encoding.

**Key Methods**:
```csharp
public interface IFileService
{
    Task<FileOpenResult> OpenFileDialogAsync();
    Task<FileContent> ReadFileAsync(string filePath);
    Task<FileMetadata> GetFileMetadataAsync(string filePath);
    bool ValidateFileSize(long fileSize, out string? warningMessage);
}
```

**Data Structures**:
```csharp
public record FileOpenResult(bool Success, string? FilePath, string? ErrorMessage);

public record FileContent(
    string Content,
    string FilePath,
    string FileName,
    FileMetadata Metadata
);

public record FileMetadata(
    long FileSizeBytes,
    int LineCount,
    string Encoding,
    DateTime LastModified
);
```

**Size Limits**:
- Warning threshold: 10 MB
- Maximum file size: 50 MB

**Encoding Detection**:
- Use `System.Text.Encoding.GetEncoding()` with BOM detection
- Fallback to UTF-8 if detection fails
- Report detected encoding in metadata

#### 3. MessageRouter

**Responsibility**: Route messages between C# backend and React frontend, serialize/deserialize JSON payloads.

**Key Methods**:
```csharp
public interface IMessageRouter
{
    void RegisterHandler<TRequest>(Func<TRequest, Task> handler) 
        where TRequest : IMessage;
    Task SendToUIAsync<TMessage>(TMessage message) 
        where TMessage : IMessage;
    void StartListening();
}
```

**Message Flow**:
- Incoming: `window.external.sendMessage(json)` → C# `ReceiveMessage` handler → Deserialize → Route to handler
- Outgoing: C# handler → Serialize → `SendWebMessage(json)` → React `window.addEventListener('message')`

#### 4. KeyboardShortcutHandler

**Responsibility**: Register and handle keyboard shortcuts at the native level.

**Key Methods**:
```csharp
public interface IKeyboardShortcutHandler
{
    void RegisterShortcut(string key, ModifierKeys modifiers, Action handler);
    void Initialize(PhotinoWindow window);
}
```

**Registered Shortcuts**:
- `Ctrl+O` (Windows/Linux) / `Cmd+O` (macOS): Open file dialog

### Frontend Components

#### 1. App Component

**Responsibility**: Root component managing application state and layout structure.

**State**:
```typescript
interface AppState {
  fileContent: FileContent | null;
  isLoading: boolean;
  error: ErrorInfo | null;
  titleBarText: string;
}
```

**Layout Structure**:
```tsx
<div className="app">
  <TitleBar title={titleBarText} />
  <ContentArea 
    fileContent={fileContent}
    isLoading={isLoading}
    error={error}
  />
  <StatusBar metadata={fileContent?.metadata} />
</div>
```

#### 2. TitleBar Component

**Responsibility**: Display application name and current file name.

**Props**:
```typescript
interface TitleBarProps {
  title: string; // "Editor" or "filename.txt - Editor"
}
```

**Rendering Logic**:
- No file open: Display "Editor"
- File open: Display "{fileName} - Editor"

#### 3. ContentArea Component

**Responsibility**: Display file contents with line numbers, scrolling, and loading/error states.

**Props**:
```typescript
interface ContentAreaProps {
  fileContent: FileContent | null;
  isLoading: boolean;
  error: ErrorInfo | null;
}
```

**Rendering Modes**:
- **Empty State**: No file open → Display prompt message "Press Ctrl+O to open a file"
- **Loading State**: File being loaded → Display spinner with "Loading file..."
- **Error State**: Error occurred → Display error message with icon
- **Content State**: File loaded → Display line-numbered text content

**Content Rendering**:
```tsx
<div className="content-area">
  <div className="line-numbers">
    {lines.map((_, index) => (
      <div key={index} className="line-number">{index + 1}</div>
    ))}
  </div>
  <div className="content-lines">
    {lines.map((line, index) => (
      <pre key={index} className="content-line">{line}</pre>
    ))}
  </div>
</div>
```

**Styling Requirements**:
- Monospaced font: `'Consolas', 'Monaco', 'Courier New', monospace`
- Vertical scrolling: `overflow-y: auto`
- Horizontal scrolling: `overflow-x: auto`
- Preserve whitespace: `white-space: pre`

#### 4. StatusBar Component

**Responsibility**: Display file metadata (size, line count, encoding).

**Props**:
```typescript
interface StatusBarProps {
  metadata: FileMetadata | null;
}
```

**Display Format**:
- File size: Format as "1.2 KB", "3.4 MB", etc.
- Line count: "150 lines"
- Encoding: "UTF-8", "ASCII", etc.
- When no file open: Display empty or "No file open"

#### 5. InteropService (Frontend)

**Responsibility**: Manage communication with C# backend, send requests and handle responses.

**Key Methods**:
```typescript
interface InteropService {
  sendOpenFileRequest(): void;
  onFileLoaded(callback: (data: FileContent) => void): void;
  onError(callback: (error: ErrorInfo) => void): void;
  onWarning(callback: (warning: WarningInfo) => void): void;
}
```

**Implementation**:
```typescript
class InteropServiceImpl implements InteropService {
  sendOpenFileRequest() {
    window.external.sendMessage(JSON.stringify({
      type: 'OpenFileRequest'
    }));
  }

  onFileLoaded(callback: (data: FileContent) => void) {
    window.addEventListener('message', (event) => {
      const message = JSON.parse(event.data);
      if (message.type === 'FileLoadedResponse') {
        callback(message.payload);
      }
    });
  }
  
  // ... similar for onError, onWarning
}
```

## Data Models

### Message Schemas

All messages between backend and frontend use JSON serialization with a common envelope structure:

```typescript
interface MessageEnvelope {
  type: string;
  payload?: any;
  timestamp: string; // ISO 8601
}
```

#### Frontend → Backend Messages

**OpenFileRequest**:
```json
{
  "type": "OpenFileRequest",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

#### Backend → Frontend Messages

**FileLoadedResponse**:
```json
{
  "type": "FileLoadedResponse",
  "payload": {
    "content": "file contents as string",
    "filePath": "/path/to/file.txt",
    "fileName": "file.txt",
    "metadata": {
      "fileSizeBytes": 2048,
      "lineCount": 42,
      "encoding": "UTF-8",
      "lastModified": "2024-01-15T09:00:00Z"
    }
  },
  "timestamp": "2024-01-15T10:30:01Z"
}
```

**ErrorResponse**:
```json
{
  "type": "ErrorResponse",
  "payload": {
    "errorCode": "FILE_NOT_FOUND",
    "message": "The selected file could not be found.",
    "details": "/path/to/missing.txt"
  },
  "timestamp": "2024-01-15T10:30:01Z"
}
```

**Error Codes**:
- `FILE_NOT_FOUND`: File does not exist
- `PERMISSION_DENIED`: Insufficient permissions to read file
- `FILE_TOO_LARGE`: File exceeds 50 MB limit
- `INTEROP_FAILURE`: Communication failure between backend and frontend
- `UNKNOWN_ERROR`: Unexpected error occurred

**WarningResponse**:
```json
{
  "type": "WarningResponse",
  "payload": {
    "warningCode": "LARGE_FILE",
    "message": "This file is 15 MB. Loading may take a moment.",
    "filePath": "/path/to/large.txt",
    "fileSizeBytes": 15728640
  },
  "timestamp": "2024-01-15T10:30:01Z"
}
```

### Frontend State Models

**FileContent**:
```typescript
interface FileContent {
  content: string;
  filePath: string;
  fileName: string;
  metadata: FileMetadata;
}
```

**FileMetadata**:
```typescript
interface FileMetadata {
  fileSizeBytes: number;
  lineCount: number;
  encoding: string;
  lastModified: string; // ISO 8601
}
```

**ErrorInfo**:
```typescript
interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}
```

**WarningInfo**:
```typescript
interface WarningInfo {
  warningCode: string;
  message: string;
  filePath: string;
  fileSizeBytes: number;
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Content Preservation Through Load-and-Display Pipeline

*For any* text file content (including various line endings, whitespace characters, and special characters), when the file is read by the Backend and sent to the File_Viewer for display, the displayed content SHALL exactly match the original file content with all line endings, spaces, tabs, and characters preserved.

**Validates: Requirements 2.2, 3.1, 3.6**

### Property 2: Dialog Cancellation Idempotence

*For any* application state, when the user cancels the File_Picker dialog, the application state SHALL remain unchanged (no file loaded, no state modified, no side effects).

**Validates: Requirements 2.3**

### Property 3: Line Number Sequential Generation

*For any* file content with N lines, the File_Viewer SHALL display line numbers as a sequential series from 1 to N, where each line number corresponds to its line position in the file.

**Validates: Requirements 3.2**

### Property 4: Title Bar Format Consistency

*For any* file name, when a file is successfully loaded, the Title_Bar SHALL display the title in the format "{fileName} - Editor" where {fileName} is the base name of the file without the full path.

**Validates: Requirements 4.2**

### Property 5: File Size Human-Readable Formatting

*For any* file size in bytes, the Status_Bar SHALL display the size in human-readable format following these rules:
- Sizes < 1024 bytes: display as "X bytes"
- Sizes >= 1024 and < 1048576 bytes: display as "X.Y KB" (rounded to 1 decimal place)
- Sizes >= 1048576 bytes: display as "X.Y MB" (rounded to 1 decimal place)

**Validates: Requirements 5.1**

### Property 6: Line Count Accuracy

*For any* file content, the Status_Bar SHALL display a line count that exactly matches the number of lines in the file, where lines are delimited by line ending characters (\n, \r\n, or \r).

**Validates: Requirements 5.2**

### Property 7: Encoding Detection Correctness

*For any* file with a detectable text encoding (UTF-8, UTF-16, ASCII, etc.), the Status_Bar SHALL display the correct encoding name as detected by the Backend's encoding detection logic.

**Validates: Requirements 5.3**

### Property 8: File Size Validation Thresholds

*For any* file size:
- If size > 10 MB AND size <= 50 MB: the App SHALL display a warning message before loading
- If size > 50 MB: the App SHALL reject the file and display an error message indicating the file is too large
- If size <= 10 MB: the App SHALL load the file without warnings

**Validates: Requirements 6.1, 6.3**

### Property 9: Interop Message Structure Correctness

*For any* file content and metadata, when the Backend sends a FileLoadedResponse message to the React UI, the message SHALL conform to the MessageEnvelope schema with type "FileLoadedResponse" and a payload containing content, filePath, fileName, and metadata fields.

**Validates: Requirements 7.1**

### Property 10: Cancellation State Preservation

*For any* application state with a currently loaded file, when the user triggers the open file action and then cancels the File_Picker, the previously loaded file SHALL remain displayed without changes.

**Validates: Requirements 2.3**


## Error Handling

### Error Categories

The application handles errors at multiple layers with consistent error reporting to the user:

#### 1. File System Errors

**File Not Found**:
- **Detection**: Backend catches `FileNotFoundException` when attempting to read file
- **Response**: Send `ErrorResponse` with code `FILE_NOT_FOUND`
- **User Message**: "The selected file could not be found."
- **Recovery**: User can try opening a different file

**Permission Denied**:
- **Detection**: Backend catches `UnauthorizedAccessException` when attempting to read file
- **Response**: Send `ErrorResponse` with code `PERMISSION_DENIED`
- **User Message**: "You do not have permission to read this file."
- **Recovery**: User can try opening a different file or adjust file permissions

**File Too Large**:
- **Detection**: Backend checks file size before reading
- **Response**: Send `ErrorResponse` with code `FILE_TOO_LARGE`
- **User Message**: "This file is too large to open (maximum 50 MB)."
- **Recovery**: User can try opening a smaller file

#### 2. Interop Errors

**Message Serialization Failure**:
- **Detection**: Backend catches `JsonException` during message serialization
- **Response**: Log error, send `ErrorResponse` with code `INTEROP_FAILURE`
- **User Message**: "An internal communication error occurred."
- **Recovery**: User can try the operation again

**Message Delivery Failure**:
- **Detection**: Frontend timeout waiting for response (5 seconds)
- **Response**: Frontend displays error locally
- **User Message**: "The application is not responding. Please restart."
- **Recovery**: User should restart the application

#### 3. Encoding Errors

**Unreadable Encoding**:
- **Detection**: Backend catches `DecoderFallbackException` when reading file
- **Response**: Attempt to read as UTF-8 with replacement characters, send warning
- **User Message**: "Some characters in this file could not be decoded and have been replaced."
- **Recovery**: File is displayed with replacement characters (�) for unreadable bytes

### Error Handling Strategy

**Backend Error Handling**:
```csharp
public async Task<FileContent> ReadFileAsync(string filePath)
{
    try
    {
        // Validate file exists
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        // Validate file size
        var fileInfo = new FileInfo(filePath);
        if (!ValidateFileSize(fileInfo.Length, out var warning))
        {
            throw new FileTooLargeException($"File exceeds maximum size: {fileInfo.Length} bytes");
        }

        // Read file with encoding detection
        var encoding = DetectEncoding(filePath);
        var content = await File.ReadAllTextAsync(filePath, encoding);
        
        // Build metadata
        var metadata = await GetFileMetadataAsync(filePath);
        
        return new FileContent(content, filePath, fileInfo.Name, metadata);
    }
    catch (FileNotFoundException ex)
    {
        await SendErrorAsync("FILE_NOT_FOUND", "The selected file could not be found.", ex.FileName);
        throw;
    }
    catch (UnauthorizedAccessException ex)
    {
        await SendErrorAsync("PERMISSION_DENIED", "You do not have permission to read this file.", filePath);
        throw;
    }
    catch (FileTooLargeException ex)
    {
        await SendErrorAsync("FILE_TOO_LARGE", "This file is too large to open (maximum 50 MB).", filePath);
        throw;
    }
    catch (Exception ex)
    {
        await SendErrorAsync("UNKNOWN_ERROR", "An unexpected error occurred while reading the file.", filePath);
        throw;
    }
}
```

**Frontend Error Handling**:
```typescript
const handleFileLoadError = (error: ErrorInfo) => {
  setError(error);
  setIsLoading(false);
  setFileContent(null);
  
  // Display error message to user
  // Error persists until user takes another action (open new file)
};

const handleInteropTimeout = () => {
  setError({
    errorCode: 'INTEROP_FAILURE',
    message: 'The application is not responding. Please restart.',
  });
  setIsLoading(false);
};
```

### Logging Strategy

**Backend Logging**:
- Use structured logging with severity levels (Info, Warning, Error)
- Log all file operations (open, read, size validation)
- Log all interop messages (sent/received)
- Log all errors with stack traces
- Log file: `editor-app.log` in application directory

**Frontend Logging**:
- Use console logging for development
- Log all interop messages (sent/received)
- Log all state transitions
- Log all errors
- Production: Consider disabling console logs or using a logging service

## Testing Strategy

### Overview

The testing strategy employs a dual approach combining property-based testing for universal correctness properties with example-based unit tests for specific scenarios, edge cases, and integration points.

### Property-Based Testing

**Framework**: Use **FsCheck** for C# backend property tests and **fast-check** for TypeScript frontend property tests.

**Configuration**:
- Minimum 100 iterations per property test
- Each property test must reference its design document property using a comment tag
- Tag format: `// Feature: editor-app, Property {number}: {property_text}`

**Backend Property Tests** (C# with FsCheck):

1. **Content Preservation** (Property 1):
   ```csharp
   // Feature: editor-app, Property 1: Content preservation through load-and-display pipeline
   [Property]
   public Property ContentPreservationThroughPipeline()
   {
       return Prop.ForAll(
           Arb.Generate<string>().Where(s => s != null),
           content =>
           {
               // Write content to temp file
               var tempFile = WriteToTempFile(content);
               
               // Read file using FileService
               var fileContent = fileService.ReadFileAsync(tempFile).Result;
               
               // Verify content matches
               return fileContent.Content == content;
           }
       );
   }
   ```

2. **Line Count Accuracy** (Property 6):
   ```csharp
   // Feature: editor-app, Property 6: Line count accuracy
   [Property]
   public Property LineCountAccuracy()
   {
       return Prop.ForAll(
           Arb.Generate<string>(),
           content =>
           {
               var expectedLineCount = CountLines(content);
               var metadata = GetFileMetadata(content);
               return metadata.LineCount == expectedLineCount;
           }
       );
   }
   ```

3. **File Size Formatting** (Property 5):
   ```csharp
   // Feature: editor-app, Property 5: File size human-readable formatting
   [Property]
   public Property FileSizeFormatting()
   {
       return Prop.ForAll(
           Arb.Generate<long>().Where(size => size >= 0),
           fileSize =>
           {
               var formatted = FormatFileSize(fileSize);
               
               if (fileSize < 1024)
                   return formatted.EndsWith(" bytes");
               else if (fileSize < 1048576)
                   return formatted.EndsWith(" KB");
               else
                   return formatted.EndsWith(" MB");
           }
       );
   }
   ```

4. **File Size Validation Thresholds** (Property 8):
   ```csharp
   // Feature: editor-app, Property 8: File size validation thresholds
   [Property]
   public Property FileSizeValidationThresholds()
   {
       return Prop.ForAll(
           Arb.Generate<long>().Where(size => size >= 0),
           fileSize =>
           {
               var result = ValidateFileSize(fileSize, out var warning);
               
               if (fileSize > 50 * 1024 * 1024)
                   return !result; // Should reject
               else if (fileSize > 10 * 1024 * 1024)
                   return result && warning != null; // Should warn
               else
                   return result && warning == null; // Should accept
           }
       );
   }
   ```

5. **Interop Message Structure** (Property 9):
   ```csharp
   // Feature: editor-app, Property 9: Interop message structure correctness
   [Property]
   public Property InteropMessageStructure()
   {
       return Prop.ForAll(
           Arb.Generate<FileContent>(),
           fileContent =>
           {
               var message = messageRouter.SerializeFileLoadedResponse(fileContent);
               var envelope = JsonSerializer.Deserialize<MessageEnvelope>(message);
               
               return envelope.Type == "FileLoadedResponse" &&
                      envelope.Payload != null &&
                      envelope.Timestamp != null;
           }
       );
   }
   ```

**Frontend Property Tests** (TypeScript with fast-check):

1. **Title Bar Format** (Property 4):
   ```typescript
   // Feature: editor-app, Property 4: Title bar format consistency
   test('title bar format consistency', () => {
     fc.assert(
       fc.property(fc.string(), (fileName) => {
         const title = formatTitleBar(fileName);
         return title === `${fileName} - Editor`;
       }),
       { numRuns: 100 }
     );
   });
   ```

2. **Line Number Generation** (Property 3):
   ```typescript
   // Feature: editor-app, Property 3: Line number sequential generation
   test('line numbers are sequential', () => {
     fc.assert(
       fc.property(fc.array(fc.string()), (lines) => {
         const lineNumbers = generateLineNumbers(lines.length);
         return lineNumbers.every((num, idx) => num === idx + 1);
       }),
       { numRuns: 100 }
     );
   });
   ```

3. **Dialog Cancellation Idempotence** (Property 2):
   ```typescript
   // Feature: editor-app, Property 2: Dialog cancellation idempotence
   test('canceling dialog preserves state', () => {
     fc.assert(
       fc.property(fc.record({
         fileContent: fc.option(fc.string(), { nil: null }),
         isLoading: fc.boolean(),
         error: fc.option(fc.string(), { nil: null })
       }), (initialState) => {
         const stateBefore = { ...initialState };
         handleDialogCancel(initialState);
         return deepEqual(initialState, stateBefore);
       }),
       { numRuns: 100 }
     );
   });
   ```

### Unit Testing

**Purpose**: Test specific examples, edge cases, error conditions, and integration points that are not suitable for property-based testing.

**Framework**: Use **xUnit** for C# backend tests and **Vitest** for TypeScript frontend tests.

**Backend Unit Tests**:

1. **Initial State Display** (Requirement 1.2):
   ```csharp
   [Fact]
   public void App_OnLaunch_ShowsEmptyContentArea()
   {
       var app = new App();
       var initialState = app.GetInitialState();
       
       Assert.Null(initialState.FileContent);
       Assert.False(initialState.IsLoading);
       Assert.Null(initialState.Error);
   }
   ```

2. **Permission Error Handling** (Requirement 2.4):
   ```csharp
   [Fact]
   public async Task ReadFile_PermissionDenied_SendsErrorResponse()
   {
       var mockFileSystem = new Mock<IFileSystem>();
       mockFileSystem
           .Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<Encoding>()))
           .ThrowsAsync(new UnauthorizedAccessException());
       
       var fileService = new FileService(mockFileSystem.Object);
       
       await Assert.ThrowsAsync<UnauthorizedAccessException>(
           () => fileService.ReadFileAsync("test.txt")
       );
       
       // Verify error message was sent
       mockMessageRouter.Verify(mr => mr.SendToUIAsync(
           It.Is<ErrorResponse>(er => er.ErrorCode == "PERMISSION_DENIED")
       ));
   }
   ```

3. **File Not Found Error Handling** (Requirement 2.5):
   ```csharp
   [Fact]
   public async Task ReadFile_FileNotFound_SendsErrorResponse()
   {
       var mockFileSystem = new Mock<IFileSystem>();
       mockFileSystem
           .Setup(fs => fs.Exists(It.IsAny<string>()))
           .Returns(false);
       
       var fileService = new FileService(mockFileSystem.Object);
       
       await Assert.ThrowsAsync<FileNotFoundException>(
           () => fileService.ReadFileAsync("missing.txt")
       );
       
       mockMessageRouter.Verify(mr => mr.SendToUIAsync(
           It.Is<ErrorResponse>(er => er.ErrorCode == "FILE_NOT_FOUND")
       ));
   }
   ```

4. **Empty State Title Bar** (Requirement 4.1):
   ```csharp
   [Fact]
   public void TitleBar_NoFileOpen_DisplaysAppName()
   {
       var titleBar = new TitleBar();
       var title = titleBar.GetTitle(null);
       
       Assert.Equal("Editor", title);
   }
   ```

5. **Empty State Status Bar** (Requirement 5.4):
   ```csharp
   [Fact]
   public void StatusBar_NoFileOpen_DisplaysNoMetadata()
   {
       var statusBar = new StatusBar();
       var display = statusBar.GetDisplay(null);
       
       Assert.Empty(display);
   }
   ```

**Frontend Unit Tests**:

1. **Loading State Display** (Requirement 6.2):
   ```typescript
   test('displays loading indicator while file is loading', () => {
     const { getByText } = render(
       <ContentArea fileContent={null} isLoading={true} error={null} />
     );
     
     expect(getByText('Loading file...')).toBeInTheDocument();
   });
   ```

2. **Error State Display**:
   ```typescript
   test('displays error message when error occurs', () => {
     const error = {
       errorCode: 'FILE_NOT_FOUND',
       message: 'The selected file could not be found.'
     };
     
     const { getByText } = render(
       <ContentArea fileContent={null} isLoading={false} error={error} />
     );
     
     expect(getByText(error.message)).toBeInTheDocument();
   });
   ```

3. **Monospaced Font Styling** (Requirement 3.3):
   ```typescript
   test('content area uses monospaced font', () => {
     const { container } = render(
       <ContentArea fileContent={mockFileContent} isLoading={false} error={null} />
     );
     
     const contentLine = container.querySelector('.content-line');
     const styles = window.getComputedStyle(contentLine);
     
     expect(styles.fontFamily).toMatch(/Consolas|Monaco|Courier New|monospace/);
   });
   ```

4. **Vertical Scrolling** (Requirement 3.4):
   ```typescript
   test('content area provides vertical scrolling for large content', () => {
     const largeContent = Array(1000).fill('line').join('\n');
     const mockFile = { ...mockFileContent, content: largeContent };
     
     const { container } = render(
       <ContentArea fileContent={mockFile} isLoading={false} error={null} />
     );
     
     const contentArea = container.querySelector('.content-area');
     expect(contentArea.scrollHeight).toBeGreaterThan(contentArea.clientHeight);
   });
   ```

5. **Interop Communication Failure** (Requirement 7.3):
   ```typescript
   test('displays error when interop fails', () => {
     const mockInterop = {
       sendOpenFileRequest: jest.fn(() => {
         throw new Error('Interop failure');
       })
     };
     
     const { getByText } = render(<App interop={mockInterop} />);
     
     fireEvent.click(getByText('Open File'));
     
     expect(getByText(/communication failure/i)).toBeInTheDocument();
   });
   ```

### Integration Testing

**Purpose**: Test end-to-end flows and integration with external systems (OS file dialogs, Photino interop).

**Approach**: Use manual testing or UI automation tools for integration tests.

**Key Integration Tests**:

1. **Application Startup** (Requirement 1.1):
   - Launch application binary
   - Verify window appears within 3 seconds
   - Verify initial state is displayed

2. **Native File Picker** (Requirement 2.1):
   - Trigger open file action
   - Verify native OS file dialog appears
   - Verify dialog is modal and blocks application

3. **Keyboard Shortcut** (Requirement 8.1):
   - Press Ctrl+O (or Cmd+O on macOS)
   - Verify file picker dialog appears

4. **Cross-Platform Compatibility** (Requirement 1.4):
   - Run application on Windows, macOS, and Linux
   - Verify all features work on each platform

5. **Interop Message Flow** (Requirement 7.2):
   - Send OpenFileRequest from React UI
   - Verify Backend receives message
   - Verify Backend invokes file picker

### Test Coverage Goals

- **Backend**: 80%+ code coverage for FileService, MessageRouter, and core logic
- **Frontend**: 80%+ code coverage for React components and interop service
- **Property Tests**: 100% coverage of all correctness properties (10 properties)
- **Integration Tests**: Coverage of all cross-boundary interactions (interop, file system, OS dialogs)

### Test Organization

**Backend Tests**:
```
tests/
├── EditorApp.Tests/
│   ├── Unit/
│   │   ├── FileServiceTests.cs
│   │   ├── MessageRouterTests.cs
│   │   └── KeyboardShortcutHandlerTests.cs
│   ├── Properties/
│   │   ├── ContentPreservationProperties.cs
│   │   ├── FileSizeValidationProperties.cs
│   │   ├── FormattingProperties.cs
│   │   └── InteropProperties.cs
│   └── Integration/
│       ├── PhotinoHostTests.cs
│       └── EndToEndTests.cs
```

**Frontend Tests**:
```
src/
├── components/
│   ├── __tests__/
│   │   ├── App.test.tsx
│   │   ├── ContentArea.test.tsx
│   │   ├── TitleBar.test.tsx
│   │   └── StatusBar.test.tsx
│   └── __properties__/
│       ├── titleBar.properties.test.ts
│       ├── lineNumbers.properties.test.ts
│       └── statePreservation.properties.test.ts
└── services/
    └── __tests__/
        └── InteropService.test.ts
```

### Continuous Integration

- Run all unit tests and property tests on every commit
- Run integration tests on pull requests
- Enforce minimum code coverage thresholds
- Run tests on all target platforms (Windows, macOS, Linux) in CI pipeline

