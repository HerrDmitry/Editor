using System.Text;
using EditorApp.Models;

namespace EditorApp.Services;

/// <summary>
/// Handles all file system operations using streamed reading with a line offset index.
/// Files of any size are supported — the entire file is never loaded into memory.
/// </summary>
public class FileService : IFileService
{
    /// <summary>
    /// Cache of line offset indices keyed by file path.
    /// Each list contains the byte offset of the start of each line.
    /// </summary>
    private readonly Dictionary<string, List<long>> _lineIndexCache = new();

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
    public async Task<FileOpenMetadata> OpenFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file could not be found.", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        var encoding = DetectEncoding(filePath);

        // Scan file to build line offset index by reading raw bytes.
        // We cannot use StreamReader for this because its internal buffer
        // causes stream.Position to be inaccurate after ReadLine().
        var lineOffsets = new List<long>();
        var bomLength = GetBomLength(encoding);

        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536))
        {
            // Skip BOM if present
            if (bomLength > 0)
            {
                stream.Seek(bomLength, SeekOrigin.Begin);
            }

            // First line starts after BOM (or at 0 if no BOM)
            lineOffsets.Add(stream.Position);

            var buffer = new byte[65536];
            int bytesRead;
            bool prevWasCR = false;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];

                    if (b == (byte)'\n')
                    {
                        // \n or the \n part of \r\n — new line starts after this byte
                        long nextLineOffset = stream.Position - bytesRead + i + 1;
                        lineOffsets.Add(nextLineOffset);
                        prevWasCR = false;
                    }
                    else if (prevWasCR)
                    {
                        // Previous byte was \r but this byte is not \n — standalone \r line ending
                        long nextLineOffset = stream.Position - bytesRead + i;
                        lineOffsets.Add(nextLineOffset);
                        prevWasCR = (b == (byte)'\r');
                    }
                    else
                    {
                        prevWasCR = (b == (byte)'\r');
                    }
                }
            }

            // Handle trailing \r at end of file
            if (prevWasCR)
            {
                lineOffsets.Add(stream.Length);
            }
        }

        // If the last offset equals the file length and the file doesn't end with a newline,
        // remove it (it's the EOF marker, not a real line start)
        if (lineOffsets.Count > 1 && lineOffsets[lineOffsets.Count - 1] == fileInfo.Length)
        {
            lineOffsets.RemoveAt(lineOffsets.Count - 1);
        }

        // Store index for later ReadLinesAsync calls
        _lineIndexCache[filePath] = lineOffsets;

        // Total lines = number of line start offsets
        var totalLines = lineOffsets.Count;

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
    public async Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount)
    {
        if (!_lineIndexCache.TryGetValue(filePath, out var lineOffsets))
        {
            throw new InvalidOperationException($"File has not been opened: {filePath}");
        }

        var totalLines = lineOffsets.Count;

        // Clamp startLine
        if (startLine < 0) startLine = 0;
        if (startLine >= totalLines)
        {
            return new LinesResult(startLine, Array.Empty<string>(), totalLines);
        }

        // Clamp lineCount
        var actualCount = Math.Min(lineCount, totalLines - startLine);
        if (actualCount <= 0)
        {
            return new LinesResult(startLine, Array.Empty<string>(), totalLines);
        }

        var encoding = DetectEncoding(filePath);
        var lines = new string[actualCount];

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(lineOffsets[startLine], SeekOrigin.Begin);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);

        for (int i = 0; i < actualCount; i++)
        {
            lines[i] = await reader.ReadLineAsync() ?? string.Empty;
        }

        return new LinesResult(startLine, lines, totalLines);
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
    /// </summary>
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
