using System.Reflection;
using System.Text;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests.Properties;

/// <summary>
/// Property-based tests for FileService line index and reading.
/// **Validates: Requirements 9.1, 9.2, 9.4**
/// </summary>
public class FileServiceProperties
{
    /// <summary>
    /// Helper: write an array of lines to a temp file (LF-separated, no trailing newline).
    /// </summary>
    private static string WriteLinesToTempFile(string[] lines)
    {
        var content = string.Join("\n", lines);
        return TempFileHelper.CreateTempFile(content);
    }

    /// <summary>
    /// Filter: lines must not contain \r or \n (we test the index, not line-ending parsing).
    /// Also filter out null strings and surrogate chars.
    /// Lines must not be empty to avoid trailing-newline ambiguity.
    /// </summary>
    private static bool IsValidLineArray(string[] lines)
    {
        if (lines == null || lines.Length == 0 || lines.Length > 100)
            return false;
        foreach (var line in lines)
        {
            if (line == null || line.Length == 0) return false;
            foreach (var c in line)
            {
                if (c == '\r' || c == '\n' || char.IsSurrogate(c))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// **Validates: Requirements 9.1, 9.2**
    /// For each line N in a random array, ReadLinesAsync(N, 1)[0] == original[N].
    /// </summary>
    [Property(MaxTest = 2)]
    public bool LineIndexRoundTrip(string[] lines)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            sut.OpenFileAsync(path).GetAwaiter().GetResult();

            for (int i = 0; i < lines.Length; i++)
            {
                var result = sut.ReadLinesAsync(path, i, 1).GetAwaiter().GetResult();
                if (result.Lines.Length != 1 || result.Lines[0] != lines[i])
                    return false;
            }
            return true;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// **Validates: Requirements 9.1**
    /// OpenFileAsync.TotalLines == number of lines written.
    /// </summary>
    [Property(MaxTest = 2)]
    public bool TotalLinesAccuracy(string[] lines)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            var meta = sut.OpenFileAsync(path).GetAwaiter().GetResult();
            return meta.TotalLines == lines.Length;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// **Validates: Requirements 9.2**
    /// ReadLinesAsync(start, count) returns the exact slice of original lines.
    /// </summary>
    [Property(MaxTest = 2)]
    public bool ReadLinesRangeCorrectness(string[] lines, int startSeed, int countSeed)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        // Derive valid start and count from seeds
        var start = Math.Abs(startSeed % lines.Length);
        var maxCount = lines.Length - start;
        var count = maxCount > 0 ? Math.Abs(countSeed % (maxCount + 1)) : 0;

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            sut.OpenFileAsync(path).GetAwaiter().GetResult();

            var result = sut.ReadLinesAsync(path, start, count).GetAwaiter().GetResult();
            var expected = lines.Skip(start).Take(count).ToArray();

            if (result.Lines.Length != expected.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                if (result.Lines[i] != expected[i])
                    return false;
            }
            return true;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// **Validates: Requirements 9.4**
    /// OpenFileAsync.FileSizeBytes matches the actual file size on disk.
    /// </summary>
    [Property(MaxTest = 2)]
    public bool FileSizeAccuracy(string[] lines)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            var meta = sut.OpenFileAsync(path).GetAwaiter().GetResult();
            var actualSize = new FileInfo(path).Length;
            return meta.FileSizeBytes == actualSize;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// Feature: early-content-display, Property 2: Partial metadata callback fires exactly once for large files
    ///
    /// For any file with size > 256,000 bytes, calling OpenFileAsync with an onPartialMetadata callback
    /// SHALL invoke that callback exactly once. For any file with size ≤ 256,000 bytes, the callback
    /// SHALL never be invoked.
    ///
    /// **Validates: Requirements 1.3, 10.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PartialMetadataCallbackFiresExactlyOnceForLargeFiles(PositiveInt lineLengthSeed, PositiveInt lineCountSeed)
    {
        // Generate large file content > 256KB with varying line lengths
        // Line lengths between 20 and 200 chars to create variety
        var lineLength = (lineLengthSeed.Get % 181) + 20; // 20..200
        // Need enough lines to exceed 256KB: worst case 256001 / 20 = ~12800 lines
        // Best case 256001 / 200 = ~1281 lines. Use lineCountSeed to vary.
        var targetSize = FileService.SizeThresholdBytes + 1; // just over threshold
        var linesNeeded = (int)(targetSize / lineLength) + 100; // extra margin

        var sb = new StringBuilder();
        var rng = new Random(lineLengthSeed.Get ^ lineCountSeed.Get);
        for (int i = 0; i < linesNeeded; i++)
        {
            // Vary each line length slightly using seed
            var thisLineLen = lineLength + (rng.Next(21) - 10); // ±10 chars
            if (thisLineLen < 1) thisLineLen = 1;
            sb.Append(new string((char)('A' + (i % 26)), thisLineLen));
            if (i < linesNeeded - 1)
                sb.Append('\n');
        }

        var content = sb.ToString();
        // Verify content is actually > threshold
        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (contentBytes <= FileService.SizeThresholdBytes)
        {
            // Skip this case — shouldn't happen with our generation but be safe
            return true;
        }

        var path = TempFileHelper.CreateTempFile(content);
        try
        {
            var sut = new FileService();
            int callbackCount = 0;
            Action<FileOpenMetadata> onPartial = _ => Interlocked.Increment(ref callbackCount);

            sut.OpenFileAsync(path, onPartialMetadata: onPartial).GetAwaiter().GetResult();

            return callbackCount == 1;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// Feature: early-content-display, Property 2: Partial metadata callback fires exactly once for large files
    /// (Small file case)
    ///
    /// For any file with size ≤ 256,000 bytes, the onPartialMetadata callback SHALL never be invoked.
    ///
    /// **Validates: Requirements 1.3, 10.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PartialMetadataCallbackNeverFiresForSmallFiles(PositiveInt lineLengthSeed, PositiveInt lineCountSeed)
    {
        // Generate small file content ≤ 256KB
        // Line lengths between 10 and 100 chars
        var lineLength = (lineLengthSeed.Get % 91) + 10; // 10..100
        // Limit total size to at most SizeThresholdBytes
        var maxLines = (int)(FileService.SizeThresholdBytes / (lineLength + 1)); // +1 for newline
        var lineCount = (lineCountSeed.Get % Math.Max(maxLines, 1)) + 1; // at least 1 line

        var sb = new StringBuilder();
        for (int i = 0; i < lineCount; i++)
        {
            sb.Append(new string((char)('A' + (i % 26)), lineLength));
            if (i < lineCount - 1)
                sb.Append('\n');
        }

        var content = sb.ToString();
        // Verify content is ≤ threshold
        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (contentBytes > FileService.SizeThresholdBytes)
        {
            // Skip — shouldn't happen but be safe
            return true;
        }

        var path = TempFileHelper.CreateTempFile(content);
        try
        {
            var sut = new FileService();
            int callbackCount = 0;
            Action<FileOpenMetadata> onPartial = _ => Interlocked.Increment(ref callbackCount);

            sut.OpenFileAsync(path, onPartialMetadata: onPartial).GetAwaiter().GetResult();

            return callbackCount == 0;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// Feature: early-content-display, Property 3: Index available when partial callback fires
    ///
    /// For any large file, when the onPartialMetadata callback is invoked, ReadLinesAsync called
    /// from within that callback for line 0 SHALL succeed and return at least one line, proving
    /// the partial index is already stored and readable.
    ///
    /// **Validates: Requirements 10.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool IndexAvailableWhenPartialCallbackFires(PositiveInt lineLengthSeed, PositiveInt lineCountSeed)
    {
        // Generate large file content > 256KB with varying line lengths
        var lineLength = (lineLengthSeed.Get % 181) + 20; // 20..200
        var targetSize = FileService.SizeThresholdBytes + 1;
        var linesNeeded = (int)(targetSize / lineLength) + 100;

        var sb = new StringBuilder();
        var rng = new Random(lineLengthSeed.Get ^ lineCountSeed.Get);
        for (int i = 0; i < linesNeeded; i++)
        {
            var thisLineLen = lineLength + (rng.Next(21) - 10); // ±10 chars
            if (thisLineLen < 1) thisLineLen = 1;
            sb.Append(new string((char)('A' + (i % 26)), thisLineLen));
            if (i < linesNeeded - 1)
                sb.Append('\n');
        }

        var content = sb.ToString();
        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (contentBytes <= FileService.SizeThresholdBytes)
            return true; // skip — shouldn't happen

        var path = TempFileHelper.CreateTempFile(content);
        try
        {
            var sut = new FileService();
            bool callbackFired = false;
            bool readSucceeded = false;

            Action<FileOpenMetadata> onPartial = (meta) =>
            {
                callbackFired = true;
                // Call ReadLinesAsync from within callback — should not deadlock
                // because callback fires AFTER _indexLock is released
                var result = sut.ReadLinesAsync(path, 0, 1).GetAwaiter().GetResult();
                readSucceeded = result.Lines.Length >= 1;
            };

            sut.OpenFileAsync(path, onPartialMetadata: onPartial).GetAwaiter().GetResult();

            // Callback must have fired (large file) and read must have succeeded
            return callbackFired && readSucceeded;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// Feature: early-content-display, Property 4: Provisional totalLines matches indexed line count at threshold
    ///
    /// For any large file with varying line lengths, the totalLines value in the FileOpenMetadata
    /// passed to the onPartialMetadata callback SHALL equal the number of complete lines found
    /// in the first 256,000+ bytes of the file (the actual count of entries in the partial line
    /// offset index at callback time).
    ///
    /// Approach: In the callback, call ReadLinesAsync(path, 0, 0) to get TotalLines from the
    /// snapshot, and verify it matches the callback's totalLines.
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ProvisionalTotalLinesMatchesIndexedLineCountAtThreshold(PositiveInt lineLengthSeed, PositiveInt lineCountSeed)
    {
        // Generate large file content > 256KB with varying line lengths (20..200 chars per line)
        var baseLineLength = (lineLengthSeed.Get % 181) + 20; // 20..200
        var targetSize = FileService.SizeThresholdBytes + 1;
        var linesNeeded = (int)(targetSize / baseLineLength) + 100; // extra margin

        var sb = new StringBuilder();
        var rng = new Random(lineLengthSeed.Get ^ lineCountSeed.Get);
        for (int i = 0; i < linesNeeded; i++)
        {
            var thisLineLen = baseLineLength + (rng.Next(21) - 10); // ±10 chars
            if (thisLineLen < 1) thisLineLen = 1;
            sb.Append(new string((char)('A' + (i % 26)), thisLineLen));
            if (i < linesNeeded - 1)
                sb.Append('\n');
        }

        var content = sb.ToString();
        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (contentBytes <= FileService.SizeThresholdBytes)
            return true; // skip — shouldn't happen with our generation

        var path = TempFileHelper.CreateTempFile(content);
        try
        {
            var sut = new FileService();
            bool callbackFired = false;
            int callbackTotalLines = -1;
            int snapshotTotalLines = -1;

            Action<FileOpenMetadata> onPartial = (meta) =>
            {
                callbackFired = true;
                callbackTotalLines = meta.TotalLines;

                // Call ReadLinesAsync(path, 0, 0) to get TotalLines from the index snapshot
                var result = sut.ReadLinesAsync(path, 0, 0).GetAwaiter().GetResult();
                snapshotTotalLines = result.TotalLines;
            };

            sut.OpenFileAsync(path, onPartialMetadata: onPartial).GetAwaiter().GetResult();

            // Callback must have fired (large file)
            if (!callbackFired)
                return false;

            // Provisional totalLines from callback must match snapshot totalLines from ReadLinesAsync
            if (callbackTotalLines != snapshotTotalLines)
                return false;

            // totalLines must be positive (at least some lines indexed)
            if (callbackTotalLines <= 0)
                return false;

            // totalLines must be <= final totalLines (partial ≤ complete)
            var finalResult = sut.ReadLinesAsync(path, 0, 0).GetAwaiter().GetResult();
            if (callbackTotalLines > finalResult.TotalLines)
                return false;

            return true;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// Feature: early-content-display, Property 5: Thread-safe concurrent index access
    ///
    /// For any interleaving of concurrent append operations (simulating the scanning thread) and
    /// ReadLinesAsync calls (simulating the message handler thread) on the same file's line offset index,
    /// all ReadLinesAsync calls SHALL return consistent results without throwing exceptions, and the
    /// returned TotalLines SHALL be ≤ the final index size.
    ///
    /// Approach: Generate a large file (>256KB). Start OpenFileAsync (which scans and appends to index).
    /// In the onPartialMetadata callback (fires when index first available), spawn concurrent ReadLinesAsync
    /// calls. Verify all succeed without exceptions and TotalLines ≤ final metadata's TotalLines.
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ThreadSafeConcurrentIndexAccess(PositiveInt lineLengthSeed, PositiveInt lineCountSeed)
    {
        // Generate large file content > 256KB with varying line lengths
        var lineLength = (lineLengthSeed.Get % 181) + 20; // 20..200
        var targetSize = FileService.SizeThresholdBytes + 1;
        var linesNeeded = (int)(targetSize / lineLength) + 200; // extra margin for concurrent reads

        var sb = new StringBuilder();
        var rng = new Random(lineLengthSeed.Get ^ lineCountSeed.Get);
        for (int i = 0; i < linesNeeded; i++)
        {
            var thisLineLen = lineLength + (rng.Next(21) - 10); // ±10 chars
            if (thisLineLen < 1) thisLineLen = 1;
            sb.Append(new string((char)('A' + (i % 26)), thisLineLen));
            if (i < linesNeeded - 1)
                sb.Append('\n');
        }

        var content = sb.ToString();
        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (contentBytes <= FileService.SizeThresholdBytes)
            return true; // skip — shouldn't happen

        var path = TempFileHelper.CreateTempFile(content);
        try
        {
            var sut = new FileService();
            var concurrentResults = new System.Collections.Concurrent.ConcurrentBag<LinesResult>();
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var readTasks = new List<Task>();

            Action<FileOpenMetadata> onPartial = (meta) =>
            {
                // Spawn multiple concurrent ReadLinesAsync calls during scan
                for (int t = 0; t < 10; t++)
                {
                    var startLine = t * 5; // spread reads across different ranges
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            // Multiple reads per task to increase contention
                            for (int r = 0; r < 5; r++)
                            {
                                var result = await sut.ReadLinesAsync(path, startLine + r, 10);
                                concurrentResults.Add(result);
                                // Small delay to interleave with scanning
                                await Task.Yield();
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    });
                    readTasks.Add(task);
                }
            };

            // Run OpenFileAsync (scanning) — concurrent reads happen via callback
            var finalMeta = sut.OpenFileAsync(path, onPartialMetadata: onPartial).GetAwaiter().GetResult();

            // Wait for all concurrent read tasks to complete
            Task.WaitAll(readTasks.ToArray());

            // Verify: no exceptions thrown during concurrent access
            if (exceptions.Count > 0)
                return false;

            // Verify: all returned TotalLines ≤ final metadata's TotalLines
            foreach (var result in concurrentResults)
            {
                if (result.TotalLines > finalMeta.TotalLines)
                    return false;

                // TotalLines must be positive (index was available)
                if (result.TotalLines <= 0)
                    return false;

                // Returned lines count must be ≤ requested and consistent
                // StartLine + Lines.Length ≤ TotalLines
                if (result.StartLine + result.Lines.Length > result.TotalLines)
                    return false;
            }

            // Must have gotten some results (callback fired, reads happened)
            if (concurrentResults.Count == 0)
                return false;

            return true;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// Feature: early-content-display, Property 1: Partial index read clamping and totalLines accuracy
    ///
    /// For any partially built line offset index with N indexed lines, and for any ReadLinesAsync
    /// request with arbitrary startLine and lineCount, the result SHALL satisfy:
    /// (a) returned lines are a subset of the indexed range [0, N)
    /// (b) LinesResult.TotalLines equals N
    /// (c) if the request extends beyond N, the returned line count equals min(lineCount, N - startLine) with no error thrown
    ///
    /// **Validates: Requirements 1.1, 3.1, 3.2, 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool PartialIndexReadClampingAndTotalLinesAccuracy(PositiveInt lineCountGen, PositiveInt partialSizeGen, int startLine, PositiveInt requestCountGen)
    {
        // Generate a file with a known number of lines (between 5 and 200)
        var totalFileLines = (lineCountGen.Get % 196) + 5; // 5..200
        var lines = Enumerable.Range(0, totalFileLines)
            .Select(i => $"Line{i:D5}_content")
            .ToArray();

        // Partial index size N: between 1 and totalFileLines (simulate mid-scan)
        var n = (partialSizeGen.Get % totalFileLines) + 1; // 1..totalFileLines

        // Request lineCount: 1..50
        var requestCount = (requestCountGen.Get % 50) + 1;

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();

            // First, open file fully to build the complete index
            sut.OpenFileAsync(path).GetAwaiter().GetResult();

            // Use reflection to replace the cached index with a partial one (first N offsets)
            var cacheField = typeof(FileService).GetField("_lineIndexCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var cache = (Dictionary<string, List<long>>)cacheField.GetValue(sut)!;

            List<long> partialOffsets;
            var lockField = typeof(FileService).GetField("_indexLock", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var indexLock = lockField.GetValue(sut)!;

            lock (indexLock)
            {
                var fullOffsets = cache[path];
                partialOffsets = fullOffsets.Take(n).ToList();
                cache[path] = partialOffsets;
            }

            // Call ReadLinesAsync with arbitrary startLine and requestCount
            var result = sut.ReadLinesAsync(path, startLine, requestCount).GetAwaiter().GetResult();

            // (b) TotalLines equals N
            if (result.TotalLines != n)
                return false;

            // Handle startLine out of range
            if (startLine < 0)
            {
                // Implementation clamps startLine to 0
                startLine = 0;
            }

            if (startLine >= n)
            {
                // (a) no lines returned — empty is subset of [0, N)
                // (c) returned line count is 0
                return result.Lines.Length == 0;
            }

            // (c) returned line count equals min(requestCount, N - startLine)
            var expectedCount = Math.Min(requestCount, n - startLine);
            if (result.Lines.Length != expectedCount)
                return false;

            // (a) returned lines are a subset of indexed range [0, N)
            // Verify StartLine is within [0, N)
            if (result.StartLine < 0 || result.StartLine >= n)
                return false;

            // Verify returned lines match original content at those positions
            for (int i = 0; i < result.Lines.Length; i++)
            {
                var lineIdx = result.StartLine + i;
                if (lineIdx >= n)
                    return false;
                if (result.Lines[i] != lines[lineIdx])
                    return false;
            }

            return true;
        }
        finally { TempFileHelper.Cleanup(path); }
    }
}
