using System.Collections.Concurrent;
using System.Text;
using EditorApp.Models;

namespace EditorApp.Services;

/// <summary>
/// Stores cached metadata for an opened file alongside its line index.
/// </summary>
internal record CacheEntry(CompressedLineIndex Index, Encoding Encoding, long FileSize, DateTime LastWriteTimeUtc);

/// <summary>
/// Handles all file system operations using streamed reading with a line offset index.
/// Files of any size are supported — the entire file is never loaded into memory.
/// </summary>
public class FileService : IFileService
{
    /// <summary>
    /// Files larger than this threshold (in bytes) will report progress during scanning.
    /// </summary>
    public const long SizeThresholdBytes = 256_000;

    /// <summary>
    /// Minimum milliseconds between progress reports to avoid flooding the message bridge.
    /// </summary>
    private const int ProgressThrottleMs = 50;

    /// <summary>
    /// Cache of line offset indices and metadata keyed by file path.
    /// Uses ConcurrentDictionary for thread-safe access without manual locking.
    /// </summary>
    private readonly ConcurrentDictionary<string, CacheEntry> _lineIndexCache = new();

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

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536);

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
                    index.AddOffset(nextLineOffset);
                    prevWasCR = false;
                }
                else if (prevWasCR)
                {
                    // Previous byte was \r but this byte is not \n — standalone \r line ending
                    long nextLineOffset = stream.Position - bytesRead + i;
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

                _lineIndexCache[filePath] = new CacheEntry(index, encoding, fileInfo.Length, fileInfo.LastWriteTimeUtc);

                var partialEncodingName = GetEncodingDisplayName(encoding);
                onPartialMetadata?.Invoke(new FileOpenMetadata(
                    filePath,
                    fileInfo.Name,
                    index.LineCount,
                    fileInfo.Length,
                    partialEncodingName
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

        // Report final 100% for large files
        if (isLargeFile && progress is not null)
        {
            progress.Report(new FileLoadProgress(fileInfo.Name, 100, fileSize));
        }

        // Seal the index — finalizes any remaining partial block
        index.Seal();

        // Store index for later ReadLinesAsync calls
        _lineIndexCache[filePath] = new CacheEntry(index, encoding, fileInfo.Length, fileInfo.LastWriteTimeUtc);

        // Total lines = number of line start offsets
        var totalLines = index.LineCount;

        var encodingName = GetEncodingDisplayName(encoding);

        return new FileOpenMetadata(
            filePath,
            fileInfo.Name,
            totalLines,
            fileInfo.Length,
            encodingName
        );
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

        // Stale file detection — verify file hasn't been modified since OpenFileAsync
        var currentFileInfo = new FileInfo(filePath);
        if (currentFileInfo.Length != cacheEntry.FileSize ||
            currentFileInfo.LastWriteTimeUtc != cacheEntry.LastWriteTimeUtc)
        {
            throw new InvalidOperationException(
                $"File has been modified since it was opened: {filePath}");
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

        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(startOffset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

        for (int i = 0; i < lineCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines[i] = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        }

        return new LinesResult(startLine, lines, snapshotCount);
    }

    /// <inheritdoc />
    public void CloseFile(string filePath)
    {
        if (_lineIndexCache.TryRemove(filePath, out var cacheEntry))
        {
            cacheEntry.Index.Dispose();
        }
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
