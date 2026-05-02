using System.Text;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based preservation tests for FileService.
/// These establish baseline behavior on UNFIXED code that must be preserved after fixes.
/// Feature: fileservice-fixes
/// </summary>
public class FileServicePreservationProperties : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
            TempFileHelper.Cleanup(path);
    }

    private string CreateTempFileWithContent(string content, Encoding? encoding = null)
    {
        var path = TempFileHelper.CreateTempFile(content, encoding);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempFileRaw(byte[] bytes)
    {
        var path = TempFileHelper.CreateTempFileRawBytes(bytes);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Property: Line round-trip preservation
    ///
    /// For any file with random content and mixed line endings (\n, \r\n, \r),
    /// OpenFileAsync + ReadLinesAsync returns exact original lines.
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool LineRoundTrip(PositiveInt lineCountSeed, int contentSeed)
    {
        // Generate 1-50 lines with random content and mixed line endings
        var lineCount = (lineCountSeed.Get % 50) + 1;
        var rng = new Random(contentSeed);

        var lines = new string[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            // All lines must be non-empty (min length 1) to avoid ambiguity:
            // 1. Empty last line + trailing newline → scanner removes trailing offset
            // 2. Empty intermediate line between \r and \n → scanner merges into \r\n
            var len = rng.Next(1, 80);
            var chars = new char[len];
            for (int c = 0; c < len; c++)
            {
                // Printable ASCII excluding \r and \n
                chars[c] = (char)rng.Next(32, 127);
            }
            lines[i] = new string(chars);
        }

        // Build file content with mixed line endings
        var sb = new StringBuilder();
        for (int i = 0; i < lineCount; i++)
        {
            sb.Append(lines[i]);
            if (i < lineCount - 1)
            {
                // Pick random line ending
                var endingType = rng.Next(3);
                sb.Append(endingType switch
                {
                    0 => "\n",
                    1 => "\r\n",
                    _ => "\r"
                });
            }
        }

        var content = sb.ToString();
        var path = CreateTempFileWithContent(content);

        var sut = new FileService();
        var metadata = sut.OpenFileAsync(path).GetAwaiter().GetResult();

        if (metadata.TotalLines != lineCount)
            return false;

        var result = sut.ReadLinesAsync(path, 0, lineCount).GetAwaiter().GetResult();

        if (result.Lines.Length != lineCount)
            return false;

        for (int i = 0; i < lineCount; i++)
        {
            if (result.Lines[i] != lines[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property: Encoding detection accuracy
    ///
    /// For files with various BOMs (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE, no BOM),
    /// DetectEncoding returns correct encoding and GetEncodingDisplayName returns
    /// same human-readable names.
    ///
    /// **Validates: Requirements 3.5, 3.8**
    /// </summary>
    [Property(MaxTest = 50)]
    public bool EncodingDetectionAccuracy(NonNegativeInt encodingSeed)
    {
        // Pick encoding variant
        var variant = encodingSeed.Get % 6;

        Encoding encoding;
        string expectedDisplayName;
        byte[] bom;
        string testContent = "Hello World";

        switch (variant)
        {
            case 0: // UTF-8 with BOM
                encoding = Encoding.UTF8;
                expectedDisplayName = "UTF-8";
                bom = new byte[] { 0xEF, 0xBB, 0xBF };
                break;
            case 1: // UTF-16 LE
                encoding = Encoding.Unicode;
                expectedDisplayName = "UTF-16 LE";
                bom = new byte[] { 0xFF, 0xFE };
                break;
            case 2: // UTF-16 BE
                encoding = Encoding.BigEndianUnicode;
                expectedDisplayName = "UTF-16 BE";
                bom = new byte[] { 0xFE, 0xFF };
                break;
            case 3: // UTF-32 LE
                encoding = Encoding.UTF32;
                expectedDisplayName = "UTF-32 LE";
                bom = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
                break;
            case 4: // UTF-32 BE
                encoding = Encoding.GetEncoding("utf-32BE");
                expectedDisplayName = "UTF-32 BE";
                bom = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
                break;
            default: // No BOM (UTF-8 without BOM)
                encoding = new UTF8Encoding(false);
                expectedDisplayName = "UTF-8";
                bom = Array.Empty<byte>();
                break;
        }

        // Create file with BOM + encoded content
        var contentBytes = encoding.GetBytes(testContent);
        var fileBytes = bom.Concat(contentBytes).ToArray();
        var path = CreateTempFileRaw(fileBytes);

        // Test DetectEncoding
        var detected = FileService.DetectEncoding(path);

        // Verify encoding matches expected WebName
        if (detected.WebName.ToUpperInvariant() != encoding.WebName.ToUpperInvariant())
            return false;

        // Verify display name via reflection (GetEncodingDisplayName is private static)
        var displayMethod = typeof(FileService).GetMethod(
            "GetEncodingDisplayName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (displayMethod == null)
            return false;

        var displayName = (string)displayMethod.Invoke(null, new object[] { detected })!;
        if (displayName != expectedDisplayName)
            return false;

        return true;
    }

    /// <summary>
    /// Property: Clamping correctness
    ///
    /// For any file with N lines and any (startLine, lineCount) pair including negatives
    /// and beyond-end values, ReadLinesAsync clamps without throwing and returns valid
    /// results with correct TotalLines.
    ///
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ClampingCorrectness(PositiveInt lineCountSeed, int contentSeed, int startLineSeed, int requestCountSeed)
    {
        // Generate file with 1-100 lines
        var n = (lineCountSeed.Get % 100) + 1;
        var rng = new Random(contentSeed);

        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            sb.Append($"Line{i}");
            if (i < n - 1)
                sb.Append('\n');
        }

        var path = CreateTempFileWithContent(sb.ToString());
        var sut = new FileService();
        sut.OpenFileAsync(path).GetAwaiter().GetResult();

        // Generate startLine in range [-100, n+100]
        var startLine = (startLineSeed % (n + 201)) - 100;
        // Generate lineCount in range [-50, 500]
        var lineCount = (requestCountSeed % 551) - 50;

        // ReadLinesAsync should never throw for any input
        try
        {
            var result = sut.ReadLinesAsync(path, startLine, lineCount).GetAwaiter().GetResult();

            // TotalLines must equal n
            if (result.TotalLines != n)
                return false;

            // Lines returned must be within bounds
            if (result.Lines.Length > n)
                return false;

            // If startLine >= n, should be empty
            if (startLine >= n && result.Lines.Length != 0)
                return false;

            return true;
        }
        catch
        {
            // Should never throw
            return false;
        }
    }

    /// <summary>
    /// Property: Partial metadata callback
    ///
    /// For files > 256KB, onPartialMetadata fires exactly once during OpenFileAsync.
    ///
    /// **Validates: Requirements 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 10)]
    public bool PartialMetadataCallback(PositiveInt sizeSeed)
    {
        // Generate file size between 256KB+1 and 512KB
        var size = FileService.SizeThresholdBytes + 1 + (sizeSeed.Get % 256_000);

        // Create file with newlines every ~100 bytes for realistic content
        var data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (i % 100 == 99) ? (byte)'\n' : (byte)'A';
        }

        var path = CreateTempFileRaw(data);

        var sut = new FileService();
        var callbackCount = 0;
        FileOpenMetadata? partialMetadata = null;

        var metadata = sut.OpenFileAsync(path, onPartialMetadata: m =>
        {
            callbackCount++;
            partialMetadata = m;
        }).GetAwaiter().GetResult();

        // Callback must fire exactly once
        if (callbackCount != 1)
            return false;

        // Partial metadata must have valid data
        if (partialMetadata == null)
            return false;

        if (partialMetadata.FilePath != path)
            return false;

        // Partial line count should be > 0 (some lines scanned)
        if (partialMetadata.TotalLines <= 0)
            return false;

        return true;
    }

    /// <summary>
    /// Property: Scan cancellation
    ///
    /// OpenFileAsync with cancelled token throws OperationCanceledException.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 10)]
    public bool ScanCancellation(PositiveInt sizeSeed)
    {
        // Need a large enough file that cancellation can trigger during scan
        var size = 1_000_000 + (sizeSeed.Get % 4_000_000);

        var data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (i % 100 == 99) ? (byte)'\n' : (byte)'A';
        }

        var path = CreateTempFileRaw(data);

        var sut = new FileService();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        try
        {
            sut.OpenFileAsync(path, cancellationToken: cts.Token).GetAwaiter().GetResult();
            // If it completes without throwing, that's a failure
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
