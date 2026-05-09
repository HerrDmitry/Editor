namespace EditorApp.Services;

/// <summary>
/// Computes and delivers visible content slices based on scroll position,
/// viewport dimensions, and wrap state. Composes from IFileService primitives.
/// </summary>
public class ViewportService : IViewportService
{
    private readonly IFileService _fileService;

    // Cache virtual line count per column width to avoid recomputation
    private int _cachedColumnWidth;
    private int _cachedVirtualLineCount;
    private string? _cachedFilePath;

    public ViewportService(IFileService fileService)
    {
        _fileService = fileService;
    }

    /// <inheritdoc />
    public async Task<ViewportResult> GetViewportAsync(
        string filePath,
        int startLine,
        int lineCount,
        int startColumn,
        int columnCount,
        bool wrapMode,
        int viewportColumns,
        CancellationToken cancellationToken = default)
    {
        if (wrapMode)
        {
            return await GetViewportWrapAsync(
                filePath, startLine, lineCount, viewportColumns, cancellationToken);
        }

        return await GetViewportNoWrapAsync(
            filePath, startLine, lineCount, startColumn, columnCount, cancellationToken);
    }

    /// <inheritdoc />
    public int GetVirtualLineCount(string filePath, int columnWidth)
    {
        if (columnWidth <= 0) throw new ArgumentOutOfRangeException(nameof(columnWidth));

        // Return cached result if same file + column width
        if (_cachedFilePath == filePath && _cachedColumnWidth == columnWidth)
        {
            return _cachedVirtualLineCount;
        }

        int totalLines = _fileService.GetTotalLines(filePath);
        int virtualLineCount = 0;

        for (int i = 0; i < totalLines; i++)
        {
            int lineCharLen = _fileService.GetLineCharLength(filePath, i);
            int effectiveLen = Math.Max(1, lineCharLen);
            virtualLineCount += (effectiveLen + columnWidth - 1) / columnWidth; // ceil division
        }

        // Cache result
        _cachedFilePath = filePath;
        _cachedColumnWidth = columnWidth;
        _cachedVirtualLineCount = virtualLineCount;

        return virtualLineCount;
    }

    /// <inheritdoc />
    public int GetMaxLineLength(string filePath)
    {
        return _fileService.GetMaxLineLength(filePath);
    }

    /// <summary>
    /// Resolve a virtual line offset to (physicalLine, charOffset) using linear scan.
    /// </summary>
    private (int physicalLine, int charOffset) ResolveVirtualLine(string filePath, int virtualLineOffset, int columnWidth)
    {
        int totalLines = _fileService.GetTotalLines(filePath);
        int virtualLinesConsumed = 0;

        for (int i = 0; i < totalLines; i++)
        {
            int lineCharLen = _fileService.GetLineCharLength(filePath, i);
            int effectiveLen = Math.Max(1, lineCharLen);
            int virtualLinesForLine = (effectiveLen + columnWidth - 1) / columnWidth;

            if (virtualLinesConsumed + virtualLinesForLine > virtualLineOffset)
            {
                int segmentIndex = virtualLineOffset - virtualLinesConsumed;
                int charOffset = segmentIndex * columnWidth;
                return (i, charOffset);
            }
            virtualLinesConsumed += virtualLinesForLine;
        }

        // Beyond end — return last line
        return (Math.Max(0, totalLines - 1), 0);
    }

    /// <summary>
    /// Wrap mode: resolve virtual line offsets to physical lines and read character slices.
    /// </summary>
    private async Task<ViewportResult> GetViewportWrapAsync(
        string filePath,
        int startVirtualLine,
        int lineCount,
        int viewportColumns,
        CancellationToken cancellationToken)
    {
        var totalPhysicalLines = _fileService.GetTotalLines(filePath);
        var maxLineLength = _fileService.GetMaxLineLength(filePath);
        int totalVirtualLines = GetVirtualLineCount(filePath, viewportColumns);

        // Clamp startVirtualLine
        if (startVirtualLine < 0) startVirtualLine = 0;

        if (startVirtualLine >= totalVirtualLines)
        {
            return new ViewportResult(
                Lines: Array.Empty<string>(),
                StartLine: startVirtualLine,
                StartColumn: 0,
                TotalPhysicalLines: totalPhysicalLines,
                LineLengths: Array.Empty<int>(),
                MaxLineLength: maxLineLength,
                TotalVirtualLines: totalVirtualLines,
                Truncated: false
            );
        }

        // Clamp lineCount to not exceed remaining virtual lines
        lineCount = Math.Min(lineCount, totalVirtualLines - startVirtualLine);
        if (lineCount <= 0)
        {
            return new ViewportResult(
                Lines: Array.Empty<string>(),
                StartLine: startVirtualLine,
                StartColumn: 0,
                TotalPhysicalLines: totalPhysicalLines,
                LineLengths: Array.Empty<int>(),
                MaxLineLength: maxLineLength,
                TotalVirtualLines: totalVirtualLines,
                Truncated: false
            );
        }

        // Response size enforcement (≤4MB)
        const int MaxPayloadBytes = 4_000_000;
        const int EstimatedBytesPerChar = 2;
        int maxCharsPerResponse = MaxPayloadBytes / EstimatedBytesPerChar;
        bool truncated = false;

        if ((long)lineCount * viewportColumns > maxCharsPerResponse)
        {
            lineCount = Math.Max(1, maxCharsPerResponse / viewportColumns);
            truncated = true;
        }

        var lines = new string[lineCount];
        var lineLengths = new int[lineCount];

        // Resolve starting position
        var (physLine, charOffset) = ResolveVirtualLine(filePath, startVirtualLine, viewportColumns);

        // Track current position as we iterate through virtual lines
        int currentPhysLine = physLine;
        int currentCharOffset = charOffset;

        for (int i = 0; i < lineCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int lineCharLen = _fileService.GetLineCharLength(filePath, currentPhysLine);
            lineLengths[i] = lineCharLen;

            // Read the character slice for this virtual line
            if (currentCharOffset >= lineCharLen)
            {
                // charOffset beyond line length — empty string
                lines[i] = string.Empty;
            }
            else
            {
                var chunk = await _fileService.ReadLineChunkAsync(
                    filePath, currentPhysLine, currentCharOffset, viewportColumns, cancellationToken);
                lines[i] = chunk.Text;
            }

            // Advance to next virtual line
            currentCharOffset += viewportColumns;
            int effectiveLen = Math.Max(1, lineCharLen);
            int virtualLinesForCurrentPhysLine = (effectiveLen + viewportColumns - 1) / viewportColumns;

            // Check if we've exhausted all virtual lines for this physical line
            if (currentCharOffset >= virtualLinesForCurrentPhysLine * viewportColumns)
            {
                currentPhysLine++;
                currentCharOffset = 0;
            }
        }

        return new ViewportResult(
            Lines: lines,
            StartLine: startVirtualLine,
            StartColumn: 0,
            TotalPhysicalLines: totalPhysicalLines,
            LineLengths: lineLengths,
            MaxLineLength: maxLineLength,
            TotalVirtualLines: totalVirtualLines,
            Truncated: truncated
        );
    }

    /// <summary>
    /// No-wrap mode: iterate physical lines, read character slices via ReadLineChunkAsync.
    /// </summary>
    private async Task<ViewportResult> GetViewportNoWrapAsync(
        string filePath,
        int startLine,
        int lineCount,
        int startColumn,
        int columnCount,
        CancellationToken cancellationToken)
    {
        var totalPhysicalLines = _fileService.GetTotalLines(filePath);
        var maxLineLength = _fileService.GetMaxLineLength(filePath);

        // Clamp startLine to [0, totalLines-1]
        if (startLine < 0) startLine = 0;

        // If startLine >= totalLines, return empty result
        if (startLine >= totalPhysicalLines)
        {
            return new ViewportResult(
                Lines: Array.Empty<string>(),
                StartLine: startLine,
                StartColumn: startColumn,
                TotalPhysicalLines: totalPhysicalLines,
                LineLengths: Array.Empty<int>(),
                MaxLineLength: maxLineLength,
                TotalVirtualLines: null,
                Truncated: false
            );
        }

        // Clamp lineCount so we don't exceed totalLines - startLine
        lineCount = Math.Min(lineCount, totalPhysicalLines - startLine);
        if (lineCount <= 0)
        {
            return new ViewportResult(
                Lines: Array.Empty<string>(),
                StartLine: startLine,
                StartColumn: startColumn,
                TotalPhysicalLines: totalPhysicalLines,
                LineLengths: Array.Empty<int>(),
                MaxLineLength: maxLineLength,
                TotalVirtualLines: null,
                Truncated: false
            );
        }

        // Response size enforcement (≤4MB)
        const int MaxPayloadBytes = 4_000_000;
        const int EstimatedBytesPerChar = 2; // JSON string encoding overhead
        int maxCharsPerResponse = MaxPayloadBytes / EstimatedBytesPerChar;
        bool truncated = false;

        if ((long)lineCount * columnCount > maxCharsPerResponse)
        {
            lineCount = Math.Max(1, maxCharsPerResponse / columnCount);
            truncated = true;
        }

        var lines = new string[lineCount];
        var lineLengths = new int[lineCount];

        for (int i = 0; i < lineCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int currentLine = startLine + i;
            int lineCharLen = _fileService.GetLineCharLength(filePath, currentLine);
            lineLengths[i] = lineCharLen;

            // If line is shorter than startColumn, return empty string
            if (lineCharLen <= startColumn)
            {
                lines[i] = string.Empty;
                continue;
            }

            var chunk = await _fileService.ReadLineChunkAsync(
                filePath, currentLine, startColumn, columnCount, cancellationToken);
            lines[i] = chunk.Text;
        }

        return new ViewportResult(
            Lines: lines,
            StartLine: startLine,
            StartColumn: startColumn,
            TotalPhysicalLines: totalPhysicalLines,
            LineLengths: lineLengths,
            MaxLineLength: maxLineLength,
            TotalVirtualLines: null,
            Truncated: truncated
        );
    }
}
