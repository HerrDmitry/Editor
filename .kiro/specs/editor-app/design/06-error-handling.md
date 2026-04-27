# Error Handling

## Error Categories

The application handles errors at multiple layers with consistent error reporting to the user:

### 1. File System Errors

**File Not Found**:
- **Detection**: Backend catches `FileNotFoundException` when attempting to scan or read file
- **Response**: Send `ErrorResponse` with code `FILE_NOT_FOUND`
- **User Message**: "The selected file could not be found."
- **Recovery**: User can try opening a different file

**Permission Denied**:
- **Detection**: Backend catches `UnauthorizedAccessException` when attempting to scan or read file
- **Response**: Send `ErrorResponse` with code `PERMISSION_DENIED`
- **User Message**: "You do not have permission to read this file."
- **Recovery**: User can try opening a different file or adjust file permissions

### 2. Interop Errors

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

### 3. Encoding Errors

**Unreadable Encoding**:
- **Detection**: Backend catches `DecoderFallbackException` when reading lines
- **Response**: Attempt to read as UTF-8 with replacement characters, send the lines with replacement characters (�) for unreadable bytes
- **User Message**: Displayed inline — unreadable bytes appear as replacement characters
- **Recovery**: File is displayed with replacement characters

## Error Handling Strategy

**Backend Error Handling**:
```csharp
public async Task<FileOpenMetadata> OpenFileAsync(string filePath)
{
    try
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var fileInfo = new FileInfo(filePath);
        var encoding = DetectEncoding(filePath);

        // Scan file to build line offset index
        var lineOffsets = new List<long>();
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true))
        {
            lineOffsets.Add(stream.Position);
            while (reader.ReadLine() != null)
            {
                lineOffsets.Add(stream.Position);
            }
        }

        // Store index for later ReadLinesAsync calls
        _lineIndexCache[filePath] = lineOffsets;

        return new FileOpenMetadata(
            filePath, fileInfo.Name, lineOffsets.Count - 1,
            fileInfo.Length, encoding.EncodingName);
    }
    catch (FileNotFoundException ex)
    {
        await SendErrorAsync("FILE_NOT_FOUND", "The selected file could not be found.", ex.FileName);
        throw;
    }
    catch (UnauthorizedAccessException)
    {
        await SendErrorAsync("PERMISSION_DENIED", "You do not have permission to read this file.", filePath);
        throw;
    }
    catch (Exception)
    {
        await SendErrorAsync("UNKNOWN_ERROR", "An unexpected error occurred while opening the file.", filePath);
        throw;
    }
}

public async Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount)
{
    var lineOffsets = _lineIndexCache[filePath];
    var totalLines = lineOffsets.Count - 1;
    var actualCount = Math.Min(lineCount, totalLines - startLine);

    var encoding = DetectEncoding(filePath);
    var lines = new string[actualCount];

    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    stream.Seek(lineOffsets[startLine], SeekOrigin.Begin);
    using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

    for (int i = 0; i < actualCount; i++)
    {
        lines[i] = await reader.ReadLineAsync() ?? string.Empty;
    }

    return new LinesResult(startLine, lines, totalLines);
}
```

**Frontend Error Handling**:
```typescript
const handleError = (error: ErrorInfo) => {
  setError(error);
  setIsLoading(false);
  setFileMeta(null);
  setLines(null);
};

const handleInteropTimeout = () => {
  setError({
    errorCode: 'INTEROP_FAILURE',
    message: 'The application is not responding. Please restart.',
  });
  setIsLoading(false);
};
```

## Logging Strategy

**Backend Logging**:
- Use structured logging with severity levels (Info, Warning, Error)
- Log all file operations (open/scan, line reads)
- Log all interop messages (sent/received)
- Log all errors with stack traces
- Log file: `editor-app.log` in application directory

**Frontend Logging**:
- Use console logging for development
- Log all interop messages (sent/received)
- Log all state transitions
- Log all errors
- Production: Consider disabling console logs or using a logging service
