namespace EditorApp.Services;

/// <summary>
/// Result of a viewport content request containing the rectangular slice
/// of file content and associated metadata.
/// </summary>
public record ViewportResult(
    string[] Lines,
    int StartLine,
    int StartColumn,
    int TotalPhysicalLines,
    int[] LineLengths,
    int MaxLineLength,
    int? TotalVirtualLines,
    bool Truncated
);

/// <summary>
/// Service responsible for computing and delivering visible content slices
/// based on scroll position, viewport dimensions, and wrap state.
/// </summary>
public interface IViewportService
{
    /// <summary>
    /// Serve a viewport response for the given rectangular region.
    /// In no-wrap mode: startLine/lineCount are physical lines.
    /// In wrap mode: startLine is a virtual line offset, resolved internally.
    /// </summary>
    Task<ViewportResult> GetViewportAsync(
        string filePath,
        int startLine,
        int lineCount,
        int startColumn,
        int columnCount,
        bool wrapMode,
        int viewportColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute total virtual line count for wrap mode given a column width.
    /// Uses stored line lengths — O(N) where N = physical line count.
    /// </summary>
    int GetVirtualLineCount(string filePath, int columnWidth);

    /// <summary>
    /// Get the maximum character length across all lines in the file.
    /// Computed during file scan, stored in metadata.
    /// </summary>
    int GetMaxLineLength(string filePath);
}
