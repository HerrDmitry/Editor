using System.Collections.Concurrent;
using System.Text;
using EditorApp.Models;

namespace EditorApp.Services;

/// <summary>
/// Stores cached metadata for an opened file alongside its line index.
/// </summary>
internal record CacheEntry(CompressedLineIndex Index, Encoding Encoding, long FileSize, DateTime LastWriteTimeUtc, int MaxLineLength);

/// <summary>
/// Handles all file system operations using streamed reading with a line offset index.
/// Files of any size are supported — the entire file is never loaded into memory.
/// </summary>
public class FileService : IFileService
{
    /// <summary>
    /// Event raised when ReadLinesAsync detects a stale file.
    /// PhotinoHostService subscribes to trigger refresh cycle.
    /// </summary>
    public event Action<string>? OnStaleFileDetected;

    /// <summary>
    /// Files larger than this threshold (in bytes) will report progress during scanning.
    /// </summary>
    public const long SizeThresholdBytes = 256_000;

    /// <summary>
    /// Minimum milliseconds between progress reports to avoid flooding the message bridge.
    /// </summary>
    private const int ProgressThrottleMs = 50;

    /// <summary>
    /// Characters per chunk. Lines below this length are "normal" and sent in full.
    /// </summary>
    public const int ChunkSizeChars = 65_536;

    /// <summary>
    /// Max JSON message payload bytes (safety limit for Photino bridge messages).
    /// </summary>
    public const int MaxMessagePayloadBytes = 4_000_000;

    /// <summary>
    /// Tracks the last time OnStaleFileDetected was fired per file path,
    /// to avoid flooding the event on every ReadLinesAsync call.
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _lastStaleEventTime = new();

    /// <summary>
    /// Minimum interval between stale detection events for the same file (ms).
    /// Must be longer than debounce window to avoid feedback loop.
    /// </summary>
    private const int StaleEventThrottleMs = 2000;

    /// <summary>
    /// Cache of line offset indices and metadata keyed by file path.
    /// Uses ConcurrentDictionary for thread-safe access without manual locking.
    /// </summary>
    private readonly ConcurrentDictionary<string, CacheEntry> _lineIndexCache = new();

    /// <summary>
    /// Cache of character lengths for lines containing multibyte characters.
    /// Key = (filePath, lineNumber). Only populated when byte_length != char_length.
    /// </summary>
    private readonly ConcurrentDictionary<(string, int), int> _charLengthCache = new();

    /// <inheritdoc />
    public Task<FileOpenResult> OpenFileDialogAsync()
    {
        try
        {
            // The actual native dialog integration is handled by the host layer
            // (PhotinoHostService) which owns the window reference.
            return Task.FromResult(new FileOpenResult(false, null, "File dialog not available outside of window context."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new FileOpenResult(false, null, ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<FileOpenMetadata> OpenFileAsync(
        string filePath,
        Action<FileOpenMetadata>? onPartialMetadata = null,
        IProgress<FileLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file could not be found.", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        var encoding = DetectEncoding(filePath);
        var fileSize = fileInfo.Length;
        var isLargeFile = fileSize > SizeThresholdBytes;

        // Scan file to build line offset index by reading raw bytes.
        // We cannot use StreamReader for this because its internal buffer
        // causes stream.Position to be inaccurate after ReadLine().
        var index = new CompressedLineIndex();
        var bomLength = GetBomLength(encoding);

        // Report initial progress for large files
        if (isLargeFile && progress is not null)
        {
            progress.Report(new FileLoadProgress(fileInfo.Name, 0, fileSize));
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);

        // Skip BOM if present
        if (bomLength > 0)
        {
            stream.Seek(bomLength, SeekOrigin.Begin);
        }

        // First line starts after BOM (or at 0 if no BOM)
        index.AddOffset(stream.Position);

        var buffer = new byte[65536];
        int bytesRead;
        bool prevWasCR = false;
        long totalBytesRead = 0;
        bool partialEmitted = false;
        var lastReportTime = Environment.TickCount64;
        long prevLineOffset = stream.Position;
        int maxLineLength = 0;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            totalBytesRead += bytesRead;

            for (int i = 0; i < bytesRead; i++)
            {
                byte b = buffer[i];

                if (b == (byte)'\n')
                {
                    // \n or the \n part of \r\n — new line starts after this byte
                    long nextLineOffset = stream.Position - bytesRead + i + 1;
                    int lineByteLen = (int)(nextLineOffset - prevLineOffset);
                    if (lineByteLen > maxLineLength) maxLineLength = lineByteLen;
                    prevLineOffset = nextLineOffset;
                    index.AddOffset(nextLineOffset);
                    prevWasCR = false;
                }
                else if (prevWasCR)
                {
                    // Previous byte was \r but this byte is not \n — standalone \r line ending
                    long nextLineOffset = stream.Position - bytesRead + i;
                    int lineByteLen = (int)(nextLineOffset - prevLineOffset);
                    if (lineByteLen > maxLineLength) maxLineLength = lineByteLen;
                    prevLineOffset = nextLineOffset;
                    index.AddOffset(nextLineOffset);
                    prevWasCR = (b == (byte)'\r');
                }
                else
                {
                    prevWasCR = (b == (byte)'\r');
                }
            }

            // Check if threshold crossed — emit partial metadata once
            if (totalBytesRead >= SizeThresholdBytes && !partialEmitted)
            {
                index.EnableConcurrentAccess();

                _lineIndexCache[filePath] = new CacheEntry(index, encoding, fileInfo.Length, fileInfo.LastWriteTimeUtc, maxLineLength);

                var partialEncodingName = GetEncodingDisplayName(encoding);
                onPartialMetadata?.Invoke(new FileOpenMetadata(
                    filePath,
                    fileInfo.Name,
                    index.LineCount,
                    fileInfo.Length,
                    partialEncodingName,
                    maxLineLength
                ));

                partialEmitted = true;
            }

            // Report progress for large files with throttling
            if (isLargeFile && progress is not null)
            {
                var percent = (int)Math.Round((double)totalBytesRead / fileSize * 100);
                var now = Environment.TickCount64;
                if (now - lastReportTime >= ProgressThrottleMs || percent == 100)
                {
                    progress.Report(new FileLoadProgress(fileInfo.Name, percent, fileSize));
                    lastReportTime = now;
                }
            }
        }

        // Handle trailing \r at end of file
        if (prevWasCR)
        {
            long nextLineOffset = stream.Length;
            int lineByteLen = (int)(nextLineOffset - prevLineOffset);
            if (lineByteLen > maxLineLength) maxLineLength = lineByteLen;
            prevLineOffset = nextLineOffset;
            index.AddOffset(stream.Length);
        }

        // Remove trailing offset that points to EOF — a file ending with a
        // newline does not create an additional empty line.
        if (index.LineCount > 1)
        {
            int lastLine = index.LineCount - 1;
            if (index.GetOffset(lastLine) == stream.Length)
            {
                index.RemoveLastOffset();
            }
        }

        // Compute last line's byte length (from last line start to EOF)
        {
            int lastLineIdx = index.LineCount - 1;
            long lastLineOffset = index.GetOffset(lastLineIdx);
            int lastLineByteLen = (int)(stream.Length - lastLineOffset);
            if (lastLineByteLen > maxLineLength) maxLineLength = lastLineByteLen;
        }

        // Report final 100% for large files
        if (isLargeFile && progress is not null)
        {
            progress.Report(new FileLoadProgress(fileInfo.Name, 100, fileSize));
        }

        // Seal the index — finalizes any remaining partial block
        index.Seal();

        // Store index for later ReadLinesAsync calls — dispose old entry if different
        if (_lineIndexCache.TryGetValue(filePath, out var previousEntry) && previousEntry.Index != index)
        {
            previousEntry.Index.Dispose();
        }
        _lineIndexCache[filePath] = new CacheEntry(index, encoding, fileInfo.Length, fileInfo.LastWriteTimeUtc, maxLineLength);

        // Total lines = number of line start offsets
        var totalLines = index.LineCount;

        var encodingName = GetEncodingDisplayName(encoding);

        return new FileOpenMetadata(
            filePath,
            fileInfo.Name,
            totalLines,
            fileInfo.Length,
            encodingName,
            maxLineLength
        );
    }

    /// <inheritdoc />
    public async Task<FileOpenMetadata> RefreshFileAsync(
        string filePath,
        IProgress<FileLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Quick check: skip re-scan if file hasn't actually changed since last cache entry.
        // This prevents redundant full scans when debounce fires but file is unchanged,
        // and breaks the stale-detection → refresh feedback loop.
        if (_lineIndexCache.TryGetValue(filePath, out var existing))
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists && fi.Length == existing.FileSize && fi.LastWriteTimeUtc == existing.LastWriteTimeUtc)
            {
                // File unchanged — return current metadata without re-scanning
                var encodingName = GetEncodingDisplayName(existing.Encoding);
                return new FileOpenMetadata(
                    filePath,
                    fi.Name,
                    existing.Index.LineCount,
                    existing.FileSize,
                    encodingName,
                    existing.MaxLineLength
                );
            }
        }

        // OpenFileAsync handles old index disposal via cache swap
        return await OpenFileAsync(filePath, onPartialMetadata: null, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
    {
        int snapshotCount;
        long startOffset;

        // Check cancellation before any I/O
        cancellationToken.ThrowIfCancellationRequested();

        // ConcurrentDictionary provides thread-safe access without manual locking
        if (!_lineIndexCache.TryGetValue(filePath, out var cacheEntry))
        {
            throw new InvalidOperationException($"File has not been opened: {filePath}");
        }

        // Stale file detection — if file modified since OpenFileAsync, fire event (throttled) and serve from existing cache
        var currentFileInfo = new FileInfo(filePath);
        if (currentFileInfo.Length != cacheEntry.FileSize ||
            currentFileInfo.LastWriteTimeUtc != cacheEntry.LastWriteTimeUtc)
        {
            var now = Environment.TickCount64;
            var lastFired = _lastStaleEventTime.GetOrAdd(filePath, 0L);
            if (now - lastFired >= StaleEventThrottleMs)
            {
                _lastStaleEventTime[filePath] = now;
                OnStaleFileDetected?.Invoke(filePath);
            }
        }

        var index = cacheEntry.Index;
        snapshotCount = index.LineCount;

        // Clamp startLine
        if (startLine < 0) startLine = 0;
        if (startLine >= snapshotCount)
        {
            return new LinesResult(startLine, Array.Empty<string>(), snapshotCount);
        }

        // Clamp lineCount to snapshot
        var clampedCount = Math.Min(lineCount, snapshotCount - startLine);
        if (clampedCount <= 0)
        {
            return new LinesResult(startLine, Array.Empty<string>(), snapshotCount);
        }

        startOffset = index.GetOffset(startLine);
        lineCount = clampedCount;

        // Use cached encoding from CacheEntry instead of re-detecting
        var encoding = cacheEntry.Encoding;
        var lines = new string[lineCount];
        var lineLengths = new int[lineCount];
        bool hasLargeLines = false;

        cancellationToken.ThrowIfCancellationRequested();

        // First pass: check if any line in range is large
        // If so, we cannot use sequential StreamReader (it would read entire large lines).
        // Instead, read each line individually via seeking.
        bool anyLargeLine = false;
        for (int i = 0; i < lineCount; i++)
        {
            int currentLine = startLine + i;
            long lineByteLen = GetLineByteLength(index, currentLine, cacheEntry.FileSize);
            if (lineByteLen > ChunkSizeChars * 2)
            {
                anyLargeLine = true;
                break;
            }
        }

        if (anyLargeLine)
        {
            // Mixed mode: read each line individually (seek-based)
            for (int i = 0; i < lineCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int currentLine = startLine + i;
                long lineByteLen = GetLineByteLength(index, currentLine, cacheEntry.FileSize);

                if (lineByteLen > ChunkSizeChars * 2)
                {
                    // Large line — read only first chunk via seek
                    var chunk = await ReadLineChunkAsync(filePath, currentLine, 0, ChunkSizeChars, cancellationToken);
                    lines[i] = chunk.Text;
                    lineLengths[i] = chunk.TotalLineChars;
                    hasLargeLines = true;
                }
                else
                {
                    // Normal line — seek to its offset and read via StreamReader
                    long lineOffset = index.GetOffset(currentLine);
                    await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    stream.Seek(lineOffset, SeekOrigin.Begin);
                    using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);
                    var lineText = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
                    lines[i] = lineText;
                    lineLengths[i] = lineText.Length;
                }
            }
        }
        else
        {
            // Fast path: all lines normal — use sequential StreamReader
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(startOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

            for (int i = 0; i < lineCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lineText = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
                lines[i] = lineText;
                lineLengths[i] = lineText.Length;
            }
        }

        // Set lineLengths to null when all lines are normal (backward compat)
        int[]? resultLineLengths = hasLargeLines ? lineLengths : null;

        return new LinesResult(startLine, lines, snapshotCount, resultLineLengths);
    }

    /// <inheritdoc />
    public async Task<List<int>> SearchInLargeLineAsync(
        string filePath, int lineNumber, string searchTerm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(searchTerm))
            return new List<int>();

        if (!_lineIndexCache.TryGetValue(filePath, out var cacheEntry))
            throw new InvalidOperationException($"File has not been opened: {filePath}");

        var index = cacheEntry.Index;
        if (lineNumber < 0 || lineNumber >= index.LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), lineNumber,
                $"Line number must be in [0, {index.LineCount}).");

        var lineStartByte = index.GetOffset(lineNumber);
        long lineByteLen = GetLineByteLength(index, lineNumber, cacheEntry.FileSize);
        var encoding = cacheEntry.Encoding;
        var matches = new List<int>();

        // For non-large lines, read the full line and do simple search
        if (lineByteLen <= ChunkSizeChars * 2)
        {
            await using var stream = new FileStream(filePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
            stream.Seek(lineStartByte, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, encoding,
                detectEncodingFromByteOrderMarks: false, bufferSize: 65536);

            var lineText = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            int idx = 0;
            while (idx <= lineText.Length - searchTerm.Length)
            {
                int found = lineText.IndexOf(searchTerm, idx, StringComparison.Ordinal);
                if (found < 0) break;
                matches.Add(found);
                idx = found + 1;
            }
            return matches;
        }

        // Large line: read in chunks with overlap to catch boundary matches
        int overlap = searchTerm.Length - 1;
        int chunkSize = ChunkSizeChars;
        var chunkBuffer = new char[chunkSize + overlap];
        int chunkStartCol = 0;
        int carryOver = 0; // chars carried from previous chunk (overlap region)

        await using var fs = new FileStream(filePath, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
        fs.Seek(lineStartByte, SeekOrigin.Begin);
        using var sr = new StreamReader(fs, encoding,
            detectEncodingFromByteOrderMarks: false, bufferSize: 65536);

        bool endOfLine = false;
        while (!endOfLine)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read chars into buffer after the carry-over region
            int toRead = chunkSize;
            int totalRead = carryOver;
            while (totalRead < carryOver + toRead)
            {
                int read = await sr.ReadAsync(chunkBuffer.AsMemory(totalRead, carryOver + toRead - totalRead), cancellationToken);
                if (read == 0)
                {
                    endOfLine = true;
                    break;
                }
                totalRead += read;
            }

            // Check for newline — trim at end of line
            int usableLength = totalRead;
            for (int i = carryOver; i < totalRead; i++)
            {
                if (chunkBuffer[i] == '\r' || chunkBuffer[i] == '\n')
                {
                    usableLength = i;
                    endOfLine = true;
                    break;
                }
            }

            // Search within the usable portion of the buffer
            var searchSpan = new ReadOnlySpan<char>(chunkBuffer, 0, usableLength);
            int searchStart = 0;
            while (searchStart <= usableLength - searchTerm.Length)
            {
                int found = searchSpan.Slice(searchStart).IndexOf(searchTerm.AsSpan(), StringComparison.Ordinal);
                if (found < 0) break;

                int absoluteCol = chunkStartCol + searchStart + found;
                // Avoid duplicates from overlap: only add if match starts at or after chunkStartCol + carryOver
                // (matches in the overlap region were already found in previous chunk, except on first chunk)
                if (carryOver == 0 || (searchStart + found) >= carryOver)
                {
                    matches.Add(absoluteCol);
                }
                searchStart += found + 1;
            }

            if (!endOfLine)
            {
                // Prepare carry-over: copy last 'overlap' chars to start of buffer
                int newChunkStartCol = chunkStartCol + usableLength - overlap;
                Array.Copy(chunkBuffer, usableLength - overlap, chunkBuffer, 0, overlap);
                carryOver = overlap;
                chunkStartCol = newChunkStartCol;
            }
        }

        return matches;
    }

    /// <inheritdoc />
    public int GetTotalLines(string filePath)
    {
        if (!_lineIndexCache.TryGetValue(filePath, out var cacheEntry))
            throw new InvalidOperationException($"File has not been opened: {filePath}");

        return cacheEntry.Index.LineCount;
    }

    /// <inheritdoc />
    public int GetMaxLineLength(string filePath)
    {
        if (!_lineIndexCache.TryGetValue(filePath, out var cacheEntry))
            throw new InvalidOperationException($"File has not been opened: {filePath}");

        return cacheEntry.MaxLineLength;
    }

    /// <inheritdoc />
    public void CloseFile(string filePath)
    {
        _lastStaleEventTime.TryRemove(filePath, out _);
        if (_lineIndexCache.TryRemove(filePath, out var cacheEntry))
        {
            cacheEntry.Index.Dispose();
        }
    }

    /// <inheritdoc />
    public int GetLineCharLength(string filePath, int lineNumber)
    {
        if (!_lineIndexCache.TryGetValue(filePath, out var cacheEntry))
            throw new InvalidOperationException($"File has not been opened: {filePath}");

        var index = cacheEntry.Index;
        if (lineNumber < 0 || lineNumber >= index.LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), lineNumber,
                $"Line number must be in [0, {index.LineCount}).");

        // Check multibyte cache first
        if (_charLengthCache.TryGetValue((filePath, lineNumber), out var cached))
            return cached;

        // Derive byte length from consecutive offsets
        long lineStartByte = index.GetOffset(lineNumber);
        long lineEndByte = (lineNumber + 1 < index.LineCount)
            ? index.GetOffset(lineNumber + 1)
            : cacheEntry.FileSize;

        long byteLength = lineEndByte - lineStartByte;

        var encoding = cacheEntry.Encoding;

        // For single-byte encodings: byte_length == char_length (minus newline)
        if (encoding.IsSingleByte)
        {
            // Subtract conservatively 2 for CRLF newline bytes
            return (int)Math.Max(0, byteLength - 2);
        }

        // For UTF-8: optimistic ASCII assumption (byte == char)
        if (encoding is UTF8Encoding)
        {
            // Subtract conservatively 2 for CRLF newline bytes
            return (int)Math.Max(0, byteLength - 2);
        }

        // For fixed-width multi-byte (UTF-16, UTF-32): divide by bytes-per-char
        int bytesPerChar = encoding.GetMaxByteCount(1);
        return (int)Math.Max(0, byteLength / bytesPerChar - 1); // -1 for newline char
    }

    /// <inheritdoc />
    public async Task<LineChunkResult> ReadLineChunkAsync(
        string filePath, int lineNumber, int startColumn, int columnCount,
        CancellationToken cancellationToken = default)
    {
        if (!_lineIndexCache.TryGetValue(filePath, out var cacheEntry))
            throw new InvalidOperationException($"File has not been opened: {filePath}");

        var index = cacheEntry.Index;
        if (lineNumber < 0 || lineNumber >= index.LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), lineNumber,
                $"Line number must be in [0, {index.LineCount}).");

        var lineStartByte = index.GetOffset(lineNumber);
        var encoding = cacheEntry.Encoding;

        await using var stream = new FileStream(filePath, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
        stream.Seek(lineStartByte, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, encoding,
            detectEncodingFromByteOrderMarks: false, bufferSize: 65536);

        // Skip startColumn characters using 8192-char buffers
        var skipBuffer = new char[Math.Min(8192, Math.Max(startColumn, 1))];
        int skipped = 0;
        while (skipped < startColumn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toRead = Math.Min(skipBuffer.Length, startColumn - skipped);
            var read = await reader.ReadAsync(skipBuffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0) break; // End of stream reached before startColumn
            // Check if we hit a newline during skip (end of line)
            for (int i = 0; i < read; i++)
            {
                if (skipBuffer[i] == '\r' || skipBuffer[i] == '\n')
                {
                    // Line ended before startColumn — return empty
                    var totalCharsEarly = GetLineCharLength(filePath, lineNumber);
                    return new LineChunkResult(lineNumber, startColumn, string.Empty, totalCharsEarly, false);
                }
            }
            skipped += read;
        }

        // Read the requested chunk (capped at ChunkSizeChars)
        var chunkSize = Math.Min(columnCount, ChunkSizeChars);
        var chunkBuffer = new char[chunkSize];
        int totalRead = 0;
        while (totalRead < chunkBuffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(chunkBuffer.AsMemory(totalRead, chunkBuffer.Length - totalRead), cancellationToken);
            if (read == 0) break;
            totalRead += read;
        }

        // Trim at newline if end-of-line reached
        var text = new string(chunkBuffer, 0, totalRead);
        var newlineIdx = text.IndexOfAny(['\r', '\n']);
        if (newlineIdx >= 0)
            text = text[..newlineIdx];

        var totalLineChars = GetLineCharLength(filePath, lineNumber);
        var hasMore = (startColumn + text.Length) < totalLineChars;

        return new LineChunkResult(lineNumber, startColumn, text, totalLineChars, hasMore);
    }

    /// <summary>
    /// Computes the byte length of a line (including line-ending bytes) from
    /// CompressedLineIndex offsets. For the last line, uses fileSize as the
    /// upper bound instead of a next-line offset.
    /// </summary>
    private static long GetLineByteLength(CompressedLineIndex index, int lineNumber, long fileSize)
    {
        long lineStart = index.GetOffset(lineNumber);
        if (lineNumber < index.LineCount - 1)
        {
            return index.GetOffset(lineNumber + 1) - lineStart;
        }
        return fileSize - lineStart;
    }

    /// <summary>
    /// Get the byte length of the BOM for a given encoding.
    /// </summary>
    private static int GetBomLength(Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        return preamble.Length;
    }

    /// <summary>
    /// Detect the encoding of a file using BOM (Byte Order Mark) detection.
    /// Falls back to UTF-8 if no BOM is found.
    /// </summary>
    internal static Encoding DetectEncoding(string filePath)
    {
        var bom = new byte[4];
        int bytesRead;

        using (var stream = File.OpenRead(filePath))
        {
            bytesRead = stream.Read(bom, 0, 4);
        }

        if (bytesRead < 2)
        {
            // No BOM possible with fewer than 2 bytes — fall back to UTF-8 without BOM.
            return new UTF8Encoding(false);
        }

        // Check for UTF-32 BE BOM: 00 00 FE FF
        if (bytesRead >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
        {
            return Encoding.GetEncoding("utf-32BE");
        }

        // Check for UTF-32 LE BOM: FF FE 00 00
        if (bytesRead >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
        {
            return Encoding.UTF32;
        }

        // Check for UTF-8 BOM: EF BB BF
        if (bytesRead >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        // Check for UTF-16 BE BOM: FE FF
        if (bom[0] == 0xFE && bom[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        // Check for UTF-16 LE BOM: FF FE
        if (bom[0] == 0xFF && bom[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        // No BOM detected — fall back to UTF-8 without BOM.
        // Using UTF8Encoding(false) so GetPreamble() returns empty,
        // which prevents OpenFileAsync from skipping non-existent BOM bytes.
        return new UTF8Encoding(false);
    }

    /// <summary>
    /// Count the number of lines in a string. An empty string has 1 line.
    /// Lines are delimited by \n, \r\n, or \r.
    /// This is a test utility — not used in production code paths.
    /// Production line counting is performed via byte scanning in OpenFileAsync.
    /// </summary>
    [Obsolete("Test utility only — not used in production code paths.")]
    internal static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content is null ? 0 : 1;
        }

        int lineCount = 1;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r')
            {
                lineCount++;
                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (content[i] == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }

    /// <summary>
    /// Get a human-readable display name for an encoding.
    /// </summary>
    private static string GetEncodingDisplayName(Encoding encoding)
    {
        return encoding.WebName.ToUpperInvariant() switch
        {
            "UTF-8" => "UTF-8",
            "UTF-16" => "UTF-16 LE",
            "UTF-16BE" => "UTF-16 BE",
            "UTF-32" => "UTF-32 LE",
            "UTF-32BE" => "UTF-32 BE",
            "US-ASCII" => "ASCII",
            _ => encoding.WebName.ToUpperInvariant()
        };
    }
}
