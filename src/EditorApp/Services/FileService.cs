using System.Text;
using EditorApp.Models;

namespace EditorApp.Services;

/// <summary>
/// Handles all file system operations including reading files,
/// validating sizes, and detecting encoding.
/// </summary>
public class FileService : IFileService
{
    /// <summary>
    /// Warning threshold: 10 MB in bytes.
    /// </summary>
    internal const long WarningSizeThreshold = 10L * 1024 * 1024;

    /// <summary>
    /// Maximum file size: 50 MB in bytes.
    /// </summary>
    internal const long MaximumFileSize = 50L * 1024 * 1024;

    /// <inheritdoc />
    public Task<FileOpenResult> OpenFileDialogAsync()
    {
        try
        {
            // Use Photino's native file dialog.
            // PhotinoWindow.ShowOpenFile returns an array of selected paths.
            // This will be wired up when the PhotinoWindow instance is available.
            // For now, provide a working implementation that can be called
            // from the host once the window reference is injected.
            //
            // The actual native dialog integration is handled by the host layer
            // (PhotinoHostService) which owns the window reference. This method
            // serves as the service-layer entry point.
            return Task.FromResult(new FileOpenResult(false, null, "File dialog not available outside of window context."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new FileOpenResult(false, null, ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<FileContent> ReadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file could not be found.", filePath);
        }

        var fileInfo = new FileInfo(filePath);

        if (!ValidateFileSize(fileInfo.Length, out _))
        {
            throw new InvalidOperationException(
                $"File exceeds maximum size of {MaximumFileSize / (1024 * 1024)} MB: {fileInfo.Length} bytes");
        }

        var encoding = DetectEncoding(filePath);
        var content = await File.ReadAllTextAsync(filePath, encoding);
        var metadata = BuildMetadata(fileInfo, content, encoding);

        return new FileContent(content, filePath, fileInfo.Name, metadata);
    }

    /// <inheritdoc />
    public Task<FileMetadata> GetFileMetadataAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file could not be found.", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        var encoding = DetectEncoding(filePath);

        // Read content to count lines accurately
        var content = File.ReadAllText(filePath, encoding);
        var metadata = BuildMetadata(fileInfo, content, encoding);

        return Task.FromResult(metadata);
    }

    /// <inheritdoc />
    public bool ValidateFileSize(long fileSize, out string? warningMessage)
    {
        if (fileSize > MaximumFileSize)
        {
            warningMessage = $"This file is too large to open (maximum {MaximumFileSize / (1024 * 1024)} MB).";
            return false;
        }

        if (fileSize > WarningSizeThreshold)
        {
            var sizeMb = fileSize / (1024.0 * 1024.0);
            warningMessage = $"This file is {sizeMb:F1} MB. Loading may take a moment.";
            return true;
        }

        warningMessage = null;
        return true;
    }

    /// <summary>
    /// Detect the encoding of a file using BOM (Byte Order Mark) detection.
    /// Falls back to UTF-8 if no BOM is found.
    /// </summary>
    internal static Encoding DetectEncoding(string filePath)
    {
        // Read the first few bytes to check for a BOM
        var bom = new byte[4];
        int bytesRead;

        using (var stream = File.OpenRead(filePath))
        {
            bytesRead = stream.Read(bom, 0, 4);
        }

        if (bytesRead < 2)
        {
            return Encoding.UTF8;
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

        // No BOM detected — fall back to UTF-8
        return Encoding.UTF8;
    }

    /// <summary>
    /// Build a FileMetadata record from file info, content, and detected encoding.
    /// </summary>
    private static FileMetadata BuildMetadata(FileInfo fileInfo, string content, Encoding encoding)
    {
        var lineCount = CountLines(content);
        var encodingName = GetEncodingDisplayName(encoding);

        return new FileMetadata(
            fileInfo.Length,
            lineCount,
            encodingName,
            fileInfo.LastWriteTimeUtc
        );
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
                // Skip the \n in a \r\n pair
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
