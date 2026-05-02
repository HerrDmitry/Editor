namespace EditorApp.Services;

/// <summary>
/// Discriminated union tag for the delta array type within a block.
/// </summary>
internal enum DeltaType : byte
{
    Byte,
    UShort,
    UInt,
    Long
}

/// <summary>
/// A finalized block of line offsets. Stores one anchor and a typed delta array.
/// </summary>
internal readonly struct Block
{
    public readonly long Anchor;
    public readonly Array Deltas;
    public readonly DeltaType Type;
    public readonly int Count;

    public Block(long anchor, Array deltas, DeltaType type, int count)
    {
        Anchor = anchor;
        Deltas = deltas;
        Type = type;
        Count = count;
    }
}

/// <summary>
/// Memory-efficient line offset index using block-based delta encoding.
/// Groups line offsets into fixed-size blocks. Each block stores one absolute
/// anchor offset and delta-encoded offsets for remaining lines using the
/// narrowest integer type that fits.
/// </summary>
public sealed class CompressedLineIndex
{
    /// <summary>
    /// Default number of lines per block.
    /// </summary>
    public const int DefaultBlockSize = 128;

    private readonly int _blockSize;
    private readonly List<Block> _blocks;
    private readonly long[] _pendingBuffer;
    private int _pendingCount;
    private int _totalLineCount;
    private bool _sealed;
    private ReaderWriterLockSlim? _rwLock;

    /// <summary>
    /// Creates a new CompressedLineIndex with the specified block size.
    /// </summary>
    /// <param name="blockSize">
    /// Number of lines per block. Must be a power of 2 in [32, 1024].
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when blockSize is not a power of 2 or outside [32, 1024].
    /// </exception>
    public CompressedLineIndex(int blockSize = DefaultBlockSize)
    {
        if (blockSize < 32 || blockSize > 1024 || (blockSize & (blockSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockSize),
                blockSize,
                "Block size must be a power of 2 between 32 and 1024 inclusive.");
        }

        _blockSize = blockSize;
        _blocks = new List<Block>();
        _pendingBuffer = new long[blockSize];
        _pendingCount = 0;
        _totalLineCount = 0;
        _sealed = false;
    }

    /// <summary>
    /// Gets the block size used by this index.
    /// </summary>
    public int BlockSize => _blockSize;

    /// <summary>
    /// Enables thread-safe concurrent access by creating an internal
    /// ReaderWriterLockSlim. Call this before publishing the index to
    /// shared state where concurrent reads/writes may occur.
    /// </summary>
    public void EnableConcurrentAccess()
    {
        _rwLock = new ReaderWriterLockSlim();
    }

    /// <summary>
    /// Gets the total number of line offsets stored (finalized + pending).
    /// </summary>
    public int LineCount
    {
        get
        {
            if (_rwLock is null)
            {
                return _totalLineCount;
            }

            _rwLock.EnterReadLock();
            try
            {
                return _totalLineCount;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Appends a line offset to the index. When the pending buffer fills,
    /// the block is finalized with delta encoding.
    /// </summary>
    /// <param name="offset">The byte offset of the line start.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if called after <see cref="Seal"/> has been invoked.
    /// </exception>
    public void AddOffset(long offset)
    {
        if (_rwLock is null)
        {
            AddOffsetCore(offset);
            return;
        }

        _rwLock.EnterWriteLock();
        try
        {
            AddOffsetCore(offset);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    private void AddOffsetCore(long offset)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("Cannot add offsets after the index has been sealed.");
        }

        _pendingBuffer[_pendingCount] = offset;
        _pendingCount++;
        _totalLineCount++;

        if (_pendingCount == _blockSize)
        {
            FinalizeCurrentBlock();
        }
    }

    /// <summary>
    /// Finalizes the current pending buffer into a compressed block.
    /// Computes deltas from the anchor (first offset) and selects the
    /// narrowest integer type that can represent all deltas.
    /// </summary>
    private void FinalizeCurrentBlock()
    {
        long anchor = _pendingBuffer[0];
        int deltaCount = _pendingCount - 1;

        // Compute max delta to determine narrowest type
        long maxDelta = 0;
        for (int i = 1; i < _pendingCount; i++)
        {
            long delta = _pendingBuffer[i] - anchor;
            if (delta > maxDelta)
            {
                maxDelta = delta;
            }
        }

        Array deltas;
        DeltaType type;

        if (maxDelta <= byte.MaxValue)
        {
            var arr = new byte[deltaCount];
            for (int i = 0; i < deltaCount; i++)
            {
                arr[i] = (byte)(_pendingBuffer[i + 1] - anchor);
            }
            deltas = arr;
            type = DeltaType.Byte;
        }
        else if (maxDelta <= ushort.MaxValue)
        {
            var arr = new ushort[deltaCount];
            for (int i = 0; i < deltaCount; i++)
            {
                arr[i] = (ushort)(_pendingBuffer[i + 1] - anchor);
            }
            deltas = arr;
            type = DeltaType.UShort;
        }
        else if (maxDelta <= uint.MaxValue)
        {
            var arr = new uint[deltaCount];
            for (int i = 0; i < deltaCount; i++)
            {
                arr[i] = (uint)(_pendingBuffer[i + 1] - anchor);
            }
            deltas = arr;
            type = DeltaType.UInt;
        }
        else
        {
            var arr = new long[deltaCount];
            for (int i = 0; i < deltaCount; i++)
            {
                arr[i] = _pendingBuffer[i + 1] - anchor;
            }
            deltas = arr;
            type = DeltaType.Long;
        }

        _blocks.Add(new Block(anchor, deltas, type, _pendingCount));
        _pendingCount = 0;
    }

    /// <summary>
    /// Removes the last added offset from the index. Used to undo a trailing
    /// offset that points to EOF when a file ends with a newline character.
    /// Can only be called when the last offset is still in the pending buffer
    /// (not yet finalized into a block). No-op if the index is empty.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if called after <see cref="Seal"/> has been invoked.
    /// </exception>
    public void RemoveLastOffset()
    {
        if (_rwLock is null)
        {
            RemoveLastOffsetCore();
            return;
        }

        _rwLock.EnterWriteLock();
        try
        {
            RemoveLastOffsetCore();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    private void RemoveLastOffsetCore()
    {
        if (_sealed)
        {
            throw new InvalidOperationException("Cannot remove offsets after the index has been sealed.");
        }

        if (_totalLineCount == 0)
        {
            return;
        }

        if (_pendingCount > 0)
        {
            _pendingCount--;
            _totalLineCount--;
        }
        // If pendingCount is 0, the last offset was finalized into a block.
        // This case shouldn't occur in normal usage (trailing offset is always
        // the most recent add), but we silently no-op to be safe.
    }

    /// <summary>
    /// Finalizes any remaining partial block and marks the index as sealed.
    /// After sealing, no more offsets can be added.
    /// Calling Seal() on an already-sealed index is a no-op.
    /// </summary>
    public void Seal()
    {
        if (_rwLock is null)
        {
            SealCore();
            return;
        }

        _rwLock.EnterWriteLock();
        try
        {
            SealCore();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    private void SealCore()
    {
        if (_sealed)
        {
            return;
        }

        if (_pendingCount > 0)
        {
            FinalizeCurrentBlock();
        }

        _sealed = true;
    }

    /// <summary>
    /// Retrieves the byte offset for the given line number.
    /// Supports lookups into both finalized blocks and the pending buffer.
    /// </summary>
    /// <param name="lineNumber">Zero-based line number.</param>
    /// <returns>The byte offset of the line start.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when lineNumber is negative or >= LineCount.
    /// </exception>
    public long GetOffset(int lineNumber)
    {
        if (_rwLock is null)
        {
            return GetOffsetCore(lineNumber);
        }

        _rwLock.EnterReadLock();
        try
        {
            return GetOffsetCore(lineNumber);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    private long GetOffsetCore(int lineNumber)
    {
        if (lineNumber < 0 || lineNumber >= _totalLineCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineNumber),
                lineNumber,
                $"Line number must be in [0, {_totalLineCount}).");
        }

        int blockIndex = lineNumber / _blockSize;
        int position = lineNumber % _blockSize;

        // Lookup from finalized block
        if (blockIndex < _blocks.Count)
        {
            var block = _blocks[blockIndex];

            if (position == 0)
            {
                return block.Anchor;
            }

            return block.Anchor + GetDelta(block, position - 1);
        }

        // Lookup from pending buffer (not yet finalized)
        int pendingIndex = lineNumber - (_blocks.Count * _blockSize);
        return _pendingBuffer[pendingIndex];
    }

    /// <summary>
    /// Estimates the total memory consumed by this index in bytes.
    /// Includes block struct overhead, anchor storage, delta array sizes,
    /// pending buffer, and list overhead.
    /// </summary>
    public long GetMemoryBytes()
    {
        if (_rwLock is null)
        {
            return GetMemoryBytesCore();
        }

        _rwLock.EnterReadLock();
        try
        {
            return GetMemoryBytesCore();
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    private long GetMemoryBytesCore()
    {
        // Object overhead for CompressedLineIndex itself + fields
        // _blockSize(4) + _pendingCount(4) + _totalLineCount(4) + _sealed(1) + _rwLock ref(8) + _blocks ref(8) + _pendingBuffer ref(8) + padding
        long total = 40;

        // Pending buffer: always allocated as long[_blockSize]
        // Array object overhead (16) + elements
        total += 16 + (_pendingBuffer.Length * 8L);

        // List<Block> overhead: object header(16) + internal array ref(8) + count(4) + version(4)
        // + backing array overhead(16) + blocks.Count * sizeof(Block)
        long blockStructSize = 32; // Anchor(8) + Deltas ref(8) + Type(1) + Count(4) + padding(~11)
        total += 32 + 16 + (_blocks.Count * blockStructSize);

        // Each finalized block's delta array
        for (int i = 0; i < _blocks.Count; i++)
        {
            var block = _blocks[i];
            int deltaCount = block.Count - 1;
            if (deltaCount <= 0) continue;

            // Array object overhead
            total += 16;

            // Element storage
            total += block.Type switch
            {
                DeltaType.Byte => deltaCount * 1L,
                DeltaType.UShort => deltaCount * 2L,
                DeltaType.UInt => deltaCount * 4L,
                DeltaType.Long => deltaCount * 8L,
                _ => deltaCount * 8L
            };
        }

        return total;
    }

    /// <summary>
    /// Reads a delta value from a block's typed delta array at the given index.
    /// </summary>
    private static long GetDelta(Block block, int index)
    {
        return block.Type switch
        {
            DeltaType.Byte => ((byte[])block.Deltas)[index],
            DeltaType.UShort => ((ushort[])block.Deltas)[index],
            DeltaType.UInt => ((uint[])block.Deltas)[index],
            DeltaType.Long => ((long[])block.Deltas)[index],
            _ => throw new InvalidOperationException($"Unknown DeltaType: {block.Type}")
        };
    }
}
