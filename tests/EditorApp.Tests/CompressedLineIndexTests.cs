using System.Reflection;
using System.Text;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests;

public class CompressedLineIndexTests
{
    [Fact]
    public void EmptyIndex_LineCountIsZero()
    {
        var index = new CompressedLineIndex();
        Assert.Equal(0, index.LineCount);
    }

    [Fact]
    public void EmptyIndex_SealIsNoOp()
    {
        var index = new CompressedLineIndex();
        index.Seal();
        Assert.Equal(0, index.LineCount);
    }

    [Fact]
    public void SingleOffset_LineCountIsOne()
    {
        var index = new CompressedLineIndex();
        index.AddOffset(42L);
        index.Seal();
        Assert.Equal(1, index.LineCount);
    }

    [Fact]
    public void SingleOffset_GetOffsetReturnsIt()
    {
        var index = new CompressedLineIndex();
        index.AddOffset(42L);
        index.Seal();
        Assert.Equal(42L, index.GetOffset(0));
    }

    [Fact]
    public void ExactlyBlockSize_OneFullBlockNoPartial()
    {
        const int blockSize = 32;
        var index = new CompressedLineIndex(blockSize);

        for (int i = 0; i < blockSize; i++)
        {
            index.AddOffset(i * 100L);
        }

        index.Seal();

        Assert.Equal(blockSize, index.LineCount);

        for (int i = 0; i < blockSize; i++)
        {
            Assert.Equal(i * 100L, index.GetOffset(i));
        }
    }

    [Fact]
    public void BlockSizePlusOne_OneFullBlockAndOnePartial()
    {
        const int blockSize = 32;
        var index = new CompressedLineIndex(blockSize);

        for (int i = 0; i < blockSize + 1; i++)
        {
            index.AddOffset(i * 50L);
        }

        index.Seal();

        Assert.Equal(blockSize + 1, index.LineCount);

        for (int i = 0; i < blockSize + 1; i++)
        {
            Assert.Equal(i * 50L, index.GetOffset(i));
        }
    }

    [Fact]
    public void DefaultBlockSize_Is128()
    {
        Assert.Equal(128, CompressedLineIndex.DefaultBlockSize);
    }

    [Fact]
    public void DefaultConstructor_UsesDefaultBlockSize()
    {
        var index = new CompressedLineIndex();
        Assert.Equal(128, index.BlockSize);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(2048)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidBlockSize_ThrowsArgumentOutOfRangeException(int blockSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompressedLineIndex(blockSize));
    }

    [Fact]
    public void GetOffset_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var index = new CompressedLineIndex();
        index.AddOffset(0L);
        index.Seal();

        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetOffset(-1));
    }

    [Fact]
    public void GetOffset_AtLineCount_ThrowsArgumentOutOfRangeException()
    {
        var index = new CompressedLineIndex();
        index.AddOffset(0L);
        index.AddOffset(10L);
        index.Seal();

        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetOffset(2));
    }

    [Fact]
    public void GetOffset_EmptyIndex_ThrowsArgumentOutOfRangeException()
    {
        var index = new CompressedLineIndex();
        index.Seal();

        Assert.Throws<ArgumentOutOfRangeException>(() => index.GetOffset(0));
    }

    [Fact]
    public void AddOffset_AfterSeal_ThrowsInvalidOperationException()
    {
        var index = new CompressedLineIndex();
        index.AddOffset(0L);
        index.Seal();

        Assert.Throws<InvalidOperationException>(() => index.AddOffset(100L));
    }

    [Fact]
    public void TenMillionLineIndex_MemoryUnder25MB()
    {
        // Arrange: 10M monotonically increasing offsets, avg line length ~60 bytes
        const int lineCount = 10_000_000;
        const long maxMemoryBytes = 25L * 1024 * 1024; // 25 MB

        var index = new CompressedLineIndex(); // default block size 128
        var rng = new Random(42); // fixed seed for determinism

        long offset = 0;
        for (int i = 0; i < lineCount; i++)
        {
            index.AddOffset(offset);
            offset += rng.Next(30, 91); // random line length in [30, 90], avg 60
        }

        index.Seal();

        // Act
        long memoryBytes = index.GetMemoryBytes();

        // Assert
        Assert.Equal(lineCount, index.LineCount);
        Assert.True(
            memoryBytes < maxMemoryBytes,
            $"CompressedLineIndex used {memoryBytes / (1024.0 * 1024.0):F2} MB, expected < 25 MB");
    }
}

/// <summary>
/// Integration tests verifying OpenFileAsync produces CompressedLineIndex
/// and ReadLinesAsync returns correct content at various positions.
/// Validates: Requirements 6.1, 9.3, 5.1
/// </summary>
public class CompressedLineIndexIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            TempFileHelper.Cleanup(path);
        }
    }

    /// <summary>
    /// Creates a temp file with numbered lines like "Line 000", "Line 001", etc.
    /// </summary>
    private string CreateNumberedFile(int lineCount)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lineCount; i++)
        {
            sb.AppendLine($"Line {i:D3}");
        }
        // Remove trailing newline to avoid ambiguity with trailing empty line
        var content = sb.ToString().TrimEnd('\n').TrimEnd('\r');
        var path = TempFileHelper.CreateTempFile(content);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task OpenFileAsync_ProducesCompressedLineIndex_InCache()
    {
        // Arrange
        var path = CreateNumberedFile(500);
        var fileService = new FileService();

        // Act
        var metadata = await fileService.OpenFileAsync(path);

        // Assert: metadata reports correct total lines
        Assert.Equal(500, metadata.TotalLines);

        // Verify via reflection that _lineIndexCache contains a CompressedLineIndex
        var cacheField = typeof(FileService).GetField(
            "_lineIndexCache",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(cacheField);

        var cache = cacheField!.GetValue(fileService) as Dictionary<string, CompressedLineIndex>;
        Assert.NotNull(cache);
        Assert.True(cache!.ContainsKey(path));

        var index = cache[path];
        Assert.IsType<CompressedLineIndex>(index);
        Assert.Equal(500, index.LineCount);
    }

    [Fact]
    public async Task ReadLinesAsync_StartOfFile_ReturnsCorrectContent()
    {
        // Arrange
        var path = CreateNumberedFile(500);
        var fileService = new FileService();
        await fileService.OpenFileAsync(path);

        // Act: read first 10 lines
        var result = await fileService.ReadLinesAsync(path, 0, 10);

        // Assert
        Assert.Equal(10, result.Lines.Length);
        Assert.Equal(500, result.TotalLines);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal($"Line {i:D3}", result.Lines[i]);
        }
    }

    [Fact]
    public async Task ReadLinesAsync_MiddleOfFile_ReturnsCorrectContent()
    {
        // Arrange
        var path = CreateNumberedFile(500);
        var fileService = new FileService();
        await fileService.OpenFileAsync(path);

        // Act: read 20 lines from middle (line 250)
        var result = await fileService.ReadLinesAsync(path, 250, 20);

        // Assert
        Assert.Equal(20, result.Lines.Length);
        Assert.Equal(500, result.TotalLines);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal($"Line {(250 + i):D3}", result.Lines[i]);
        }
    }

    [Fact]
    public async Task ReadLinesAsync_EndOfFile_ReturnsCorrectContent()
    {
        // Arrange
        var path = CreateNumberedFile(500);
        var fileService = new FileService();
        await fileService.OpenFileAsync(path);

        // Act: read last 5 lines
        var result = await fileService.ReadLinesAsync(path, 495, 5);

        // Assert
        Assert.Equal(5, result.Lines.Length);
        Assert.Equal(500, result.TotalLines);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"Line {(495 + i):D3}", result.Lines[i]);
        }
    }

    [Fact]
    public async Task ReadLinesAsync_BeyondEnd_ClampedToAvailable()
    {
        // Arrange
        var path = CreateNumberedFile(500);
        var fileService = new FileService();
        await fileService.OpenFileAsync(path);

        // Act: request lines beyond end
        var result = await fileService.ReadLinesAsync(path, 498, 100);

        // Assert: clamped to 2 lines (498, 499)
        Assert.Equal(2, result.Lines.Length);
        Assert.Equal(500, result.TotalLines);
        Assert.Equal("Line 498", result.Lines[0]);
        Assert.Equal("Line 499", result.Lines[1]);
    }

    [Fact]
    public async Task ReadLinesAsync_StartBeyondTotal_ReturnsEmpty()
    {
        // Arrange
        var path = CreateNumberedFile(500);
        var fileService = new FileService();
        await fileService.OpenFileAsync(path);

        // Act: start beyond total lines
        var result = await fileService.ReadLinesAsync(path, 600, 10);

        // Assert
        Assert.Empty(result.Lines);
        Assert.Equal(500, result.TotalLines);
    }

    [Fact]
    public async Task ReadLinesAsync_BlockBoundary_ReturnsCorrectContent()
    {
        // Arrange: 500 lines, default block size 128
        // Block boundaries at 0, 128, 256, 384
        var path = CreateNumberedFile(500);
        var fileService = new FileService();
        await fileService.OpenFileAsync(path);

        // Act: read across block boundary (lines 126-130 spans block 0→1)
        var result = await fileService.ReadLinesAsync(path, 126, 5);

        // Assert
        Assert.Equal(5, result.Lines.Length);
        Assert.Equal("Line 126", result.Lines[0]);
        Assert.Equal("Line 127", result.Lines[1]);
        Assert.Equal("Line 128", result.Lines[2]);
        Assert.Equal("Line 129", result.Lines[3]);
        Assert.Equal("Line 130", result.Lines[4]);
    }
}
