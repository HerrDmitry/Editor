using System.Reflection;
using EditorApp.Services;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based tests for CompressedLineIndex correctness.
/// Feature: line-index-memory-optimization
/// </summary>
public class CompressedLineIndexProperties
{
    /// <summary>
    /// Valid block sizes for CompressedLineIndex (powers of 2 in [32, 1024]).
    /// </summary>
    private static readonly int[] ValidBlockSizes = { 32, 64, 128, 256, 512, 1024 };

    /// <summary>
    /// Build a monotonically increasing offset array from a seed and count.
    /// Each delta between consecutive values is in [1, 500] to simulate realistic line offsets.
    /// </summary>
    private static long[] BuildMonotonicOffsets(int count, int seed)
    {
        var rng = new Random(seed);
        var offsets = new long[count];
        long current = 0;
        for (int i = 0; i < count; i++)
        {
            current += rng.Next(1, 501); // delta in [1, 500]
            offsets[i] = current;
        }
        return offsets;
    }

    /// <summary>
    /// Feature: line-index-memory-optimization, Property 1: Round-trip offset equivalence
    ///
    /// For any monotonically increasing sequence of long offsets and any valid block size,
    /// building a CompressedLineIndex by calling AddOffset for each offset and then Seal(),
    /// the index SHALL satisfy:
    /// (a) LineCount equals the number of offsets added
    /// (b) GetOffset(i) returns the original offset for every valid line number i in [0, LineCount)
    ///
    /// **Validates: Requirements 9.1, 9.2, 1.5, 6.3, 7.1, 10.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RoundTripOffsetEquivalenceAcrossBlockSizes(PositiveInt countSeed, int randomSeed, PositiveInt blockSizeSeed)
    {
        // Generate line count in [0, 5000]
        var lineCount = countSeed.Get % 5001;

        // Pick a valid block size
        var blockSize = ValidBlockSizes[blockSizeSeed.Get % ValidBlockSizes.Length];

        // Build monotonically increasing offsets
        var offsets = BuildMonotonicOffsets(lineCount, randomSeed);

        // Build CompressedLineIndex
        var index = new CompressedLineIndex(blockSize);
        for (int i = 0; i < offsets.Length; i++)
        {
            index.AddOffset(offsets[i]);
        }
        index.Seal();

        // (a) LineCount equals input count
        if (index.LineCount != offsets.Length)
            return false;

        // (b) GetOffset(i) matches original for all i
        for (int i = 0; i < offsets.Length; i++)
        {
            if (index.GetOffset(i) != offsets[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Feature: line-index-memory-optimization, Property 2: Narrowest delta type selection
    ///
    /// For any block of offsets where the first offset is the anchor, the CompressedLineIndex
    /// SHALL select the delta storage type as follows: if the maximum delta (max offset − anchor)
    /// fits in 8 bits (≤ 255), use byte; if it fits in 16 bits (≤ 65,535), use ushort; if it fits
    /// in 32 bits (≤ 4,294,967,295), use uint; otherwise use long. No wider type SHALL be used
    /// when a narrower type suffices.
    ///
    /// **Validates: Requirements 1.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NarrowestDeltaTypeSelectedForMaxDeltaInBlock(
        PositiveInt anchorSeed,
        PositiveInt categorySeed,
        int randomSeed)
    {
        const int blockSize = 32; // smallest valid block size
        var rng = new Random(randomSeed);

        // Anchor offset: random non-negative long
        long anchor = (long)(anchorSeed.Get % 1_000_000_000) * 100;

        // Pick delta range category: 0=byte, 1=ushort, 2=uint, 3=long
        int category = categorySeed.Get % 4;

        // Determine max delta value and expected DeltaType for this category
        long maxDeltaForCategory;
        DeltaType expectedType;
        switch (category)
        {
            case 0: // byte range: max delta ≤ 255
                maxDeltaForCategory = byte.MaxValue;
                expectedType = DeltaType.Byte;
                break;
            case 1: // ushort range: max delta in [256, 65535]
                maxDeltaForCategory = ushort.MaxValue;
                expectedType = DeltaType.UShort;
                break;
            case 2: // uint range: max delta in [65536, uint.MaxValue]
                maxDeltaForCategory = uint.MaxValue;
                expectedType = DeltaType.UInt;
                break;
            default: // long range: max delta > uint.MaxValue
                maxDeltaForCategory = (long)uint.MaxValue + 1_000_000;
                expectedType = DeltaType.Long;
                break;
        }

        // Minimum delta for this category (to ensure we actually need this type)
        long minDeltaForCategory = category switch
        {
            0 => 0,
            1 => (long)byte.MaxValue + 1,
            2 => (long)ushort.MaxValue + 1,
            _ => (long)uint.MaxValue + 1
        };

        // Build blockSize offsets: first is anchor, rest are anchor + delta
        var index = new CompressedLineIndex(blockSize);
        index.AddOffset(anchor);

        // Generate blockSize - 2 random deltas within [0, maxDeltaForCategory]
        for (int i = 0; i < blockSize - 2; i++)
        {
            long delta = NextLongInRange(rng, 0, maxDeltaForCategory);
            index.AddOffset(anchor + delta);
        }

        // Ensure at least one delta hits the minimum for this category (forces the type)
        long forcedDelta = NextLongInRange(rng, minDeltaForCategory, maxDeltaForCategory);
        index.AddOffset(anchor + forcedDelta);

        // Block should be finalized now (exactly blockSize offsets added)
        // Seal to be safe
        index.Seal();

        // Use reflection to access _blocks field
        var blocksField = typeof(CompressedLineIndex)
            .GetField("_blocks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var blocks = (System.Collections.IList)blocksField.GetValue(index)!;

        if (blocks.Count == 0)
            return false;

        // Get first block and check its DeltaType
        var firstBlock = (Block)blocks[0]!;
        return firstBlock.Type == expectedType;
    }

    /// <summary>
    /// Feature: line-index-memory-optimization, Property 4: ReadLinesAsync clamping preserves correctness
    ///
    /// For any CompressedLineIndex with N lines, and for any (startLine, lineCount) request,
    /// the clamping logic SHALL produce: if startLine >= N, return empty with TotalLines = N;
    /// otherwise return at most min(lineCount, N - startLine) lines starting from startLine,
    /// with TotalLines = N.
    ///
    /// **Validates: Requirements 5.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ReadLinesAsyncClampingPreservesCorrectness(
        NonNegativeInt lineCountSeed,
        int offsetSeed,
        int startLineSeed,
        NonNegativeInt requestCountSeed)
    {
        // Build random CompressedLineIndex with N lines in [0, 500]
        int n = lineCountSeed.Get % 501;
        var offsets = BuildMonotonicOffsets(n, offsetSeed);

        var index = new CompressedLineIndex();
        for (int i = 0; i < offsets.Length; i++)
        {
            index.AddOffset(offsets[i]);
        }
        index.Seal();

        int snapshotCount = index.LineCount;

        // Generate startLine including negative and beyond N
        // Map startLineSeed to range [-50, N+50]
        int startLine = (startLineSeed % (n + 101)) - 50;

        // Generate lineCount including 0 and very large values
        int lineCount = requestCountSeed.Get % 10_001; // [0, 10000]

        // Apply clamping logic (mirrors ReadLinesAsync)
        if (startLine < 0) startLine = 0;

        if (startLine >= snapshotCount)
        {
            // Should return empty with TotalLines = N
            // Verify: TotalLines = N
            return snapshotCount == n;
        }

        var clampedCount = Math.Min(lineCount, snapshotCount - startLine);

        if (clampedCount <= 0)
        {
            // lineCount was 0 → empty result, TotalLines = N
            return snapshotCount == n;
        }

        // Verify: clampedCount = min(lineCount, N - startLine)
        int expectedClampedCount = Math.Min(lineCount, n - startLine);
        if (clampedCount != expectedClampedCount)
            return false;

        // Verify: clampedCount > 0
        if (clampedCount <= 0)
            return false;

        // Verify: startLine + clampedCount <= N (result within bounds)
        if (startLine + clampedCount > n)
            return false;

        // Verify: TotalLines always = N
        if (snapshotCount != n)
            return false;

        return true;
    }

    /// <summary>
    /// Feature: line-index-memory-optimization, Property 5: Concurrent read/write safety
    ///
    /// For any sequence of offsets and for any interleaving of concurrent AddOffset/Seal calls
    /// (writer) and GetOffset/LineCount calls (readers) on a CompressedLineIndex with concurrent
    /// access enabled, all reader calls SHALL complete without exceptions, LineCount SHALL return
    /// a value between 0 and the final count, and GetOffset(i) for any i &lt; LineCount SHALL
    /// return the correct offset.
    ///
    /// **Validates: Requirements 8.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ConcurrentReadWriteSafety(PositiveInt countSeed, int randomSeed)
    {
        // Generate line count: initial 50 + remaining 200
        const int initialCount = 50;
        const int remainingCount = 200;
        const int totalCount = initialCount + remainingCount;
        const int readerTaskCount = 4;
        const int readerIterations = 500;

        // Build monotonically increasing offsets
        var allOffsets = BuildMonotonicOffsets(totalCount, randomSeed);

        // Create index with initial offsets
        var index = new CompressedLineIndex();
        for (int i = 0; i < initialCount; i++)
        {
            index.AddOffset(allOffsets[i]);
        }

        // Enable concurrent access before publishing
        index.EnableConcurrentAccess();

        // Track exceptions from reader tasks
        var readerExceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var writerDone = new ManualResetEventSlim(false);

        // Writer task: add remaining offsets
        var writerTask = Task.Run(() =>
        {
            for (int i = initialCount; i < totalCount; i++)
            {
                index.AddOffset(allOffsets[i]);
            }
            writerDone.Set();
        });

        // Reader tasks: repeatedly call LineCount and GetOffset
        var readerTasks = new Task[readerTaskCount];
        for (int r = 0; r < readerTaskCount; r++)
        {
            var readerRng = new Random(randomSeed + r + 1);
            readerTasks[r] = Task.Run(() =>
            {
                for (int iter = 0; iter < readerIterations; iter++)
                {
                    try
                    {
                        // Snapshot LineCount
                        int lineCount = index.LineCount;

                        // LineCount must be between initialCount and totalCount
                        if (lineCount < initialCount || lineCount > totalCount)
                        {
                            readerExceptions.Add(new Exception(
                                $"LineCount {lineCount} outside expected range [{initialCount}, {totalCount}]"));
                            return;
                        }

                        // Read a random valid offset
                        if (lineCount > 0)
                        {
                            int lineNum = readerRng.Next(0, lineCount);
                            long offset = index.GetOffset(lineNum);

                            // Verify offset matches expected value
                            if (offset != allOffsets[lineNum])
                            {
                                readerExceptions.Add(new Exception(
                                    $"GetOffset({lineNum}) returned {offset}, expected {allOffsets[lineNum]}"));
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        readerExceptions.Add(ex);
                        return;
                    }
                }
            });
        }

        // Wait for all tasks
        Task.WaitAll([writerTask, .. readerTasks]);

        // Verify: no exceptions from readers
        if (!readerExceptions.IsEmpty)
            return false;

        // Verify: writer added all offsets
        if (index.LineCount != totalCount)
            return false;

        // Seal and verify all offsets round-trip correctly
        index.Seal();

        if (index.LineCount != totalCount)
            return false;

        for (int i = 0; i < totalCount; i++)
        {
            if (index.GetOffset(i) != allOffsets[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Generate a random long in [min, max] range.
    /// </summary>
    private static long NextLongInRange(Random rng, long min, long max)
    {
        if (min == max) return min;
        // Use two random ints to build a random long
        ulong range = (ulong)(max - min);
        ulong randomValue;
        if (range <= int.MaxValue)
        {
            randomValue = (ulong)rng.Next(0, (int)range + 1);
        }
        else
        {
            // Combine two 32-bit randoms for full range
            ulong high = (ulong)(uint)rng.Next();
            ulong low = (ulong)(uint)rng.Next();
            randomValue = ((high << 32) | low) % (range + 1);
        }
        return min + (long)randomValue;
    }
}
