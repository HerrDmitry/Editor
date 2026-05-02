# Implementation Plan: Block-Based Compressed Line Index

## Overview

Replace the flat `List<long>` line offset index in `FileService` with a memory-efficient `CompressedLineIndex` class that uses block-based delta encoding. Lines are grouped into fixed-size blocks (default 128). Each block stores one absolute anchor offset and delta-encoded offsets using the narrowest integer type that fits. This yields 70–90% memory savings for typical source files while preserving O(1) lookup and transparent integration with existing `IFileService` callers.

## Tasks

- [x] 1. Create CompressedLineIndex class with core types and construction API
  - [x] 1.1 Create `Services/CompressedLineIndex.cs` with `DeltaType` enum, `Block` struct, and `CompressedLineIndex` class skeleton
    - Define `DeltaType` enum: `Byte`, `UShort`, `UInt`, `Long`
    - Define `Block` readonly struct: `Anchor` (long), `Deltas` (Array), `Type` (DeltaType), `Count` (int)
    - Define `DefaultBlockSize = 128` constant
    - Implement constructor with block size validation (power of 2 in [32, 1024])
    - Implement `BlockSize` property
    - Initialize internal fields: `_blocks` (List\<Block\>), `_pendingBuffer` (long[]), `_pendingCount` (int), `_totalLineCount` (int)
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 10.1, 10.2, 10.3_

  - [x] 1.2 Implement `AddOffset` and block finalization logic
    - `AddOffset(long offset)`: append to `_pendingBuffer`, increment `_pendingCount` and `_totalLineCount`
    - When `_pendingCount == BlockSize`, call `FinalizeCurrentBlock()`
    - `FinalizeCurrentBlock()`: compute max delta from anchor, select narrowest type (`byte` if ≤255, `ushort` if ≤65535, `uint` if ≤uint.MaxValue, else `long`), allocate typed delta array, create `Block`, add to `_blocks`, reset `_pendingCount`
    - Throw `InvalidOperationException` if called after `Seal()`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 3.1, 3.2, 3.4_

  - [x] 1.3 Implement `Seal` and `GetOffset` methods
    - `Seal()`: finalize any remaining partial block (fewer than BlockSize entries), mark index as sealed
    - `GetOffset(int lineNumber)`: compute block index = `lineNumber / BlockSize`, intra-block position = `lineNumber % BlockSize`; if position 0 return anchor, else return anchor + delta; throw `ArgumentOutOfRangeException` for invalid line numbers
    - `LineCount` property: return `_totalLineCount`
    - Handle lookups into pending buffer (not yet finalized) for partial index support
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 3.3, 7.1, 7.2_

  - [x] 1.4 Implement `EnableConcurrentAccess` with `ReaderWriterLockSlim`
    - Add `_rwLock` field (ReaderWriterLockSlim, initially null)
    - `EnableConcurrentAccess()`: create and assign `_rwLock`
    - Wrap `AddOffset`/`FinalizeCurrentBlock`/`Seal` in write lock when `_rwLock` is non-null
    - Wrap `GetOffset`/`LineCount` in read lock when `_rwLock` is non-null
    - _Requirements: 8.1, 8.2, 8.3, 7.3_

  - [x] 1.5 Write unit tests for CompressedLineIndex construction and edge cases
    - Test empty index: `LineCount == 0`, `Seal()` is no-op
    - Test single offset: `LineCount == 1`, `GetOffset(0)` returns it
    - Test exactly `BlockSize` offsets: one full block, no partial
    - Test `BlockSize + 1` offsets: one full block + one partial
    - Test `DefaultBlockSize == 128`
    - Test invalid block sizes (31, 33, 2048, 0, -1) throw `ArgumentOutOfRangeException`
    - Test `GetOffset(-1)` and `GetOffset(LineCount)` throw `ArgumentOutOfRangeException`
    - Test `AddOffset` after `Seal()` throws `InvalidOperationException`
    - _Requirements: 1.1, 1.5, 2.1, 2.2, 2.3, 3.3, 10.1, 10.2, 10.3_

- [x] 2. Property-based tests for CompressedLineIndex correctness
  - [x] 2.1 Write property test for round-trip offset equivalence (Property 1)
    - **Property 1: Round-trip offset equivalence across block sizes**
    - Generate random monotonically increasing `long` sequences (0 to ~5000 lines)
    - Generate random valid block size from {32, 64, 128, 256, 512, 1024}
    - Build `CompressedLineIndex`, call `Seal()`
    - Verify `LineCount` equals input count and `GetOffset(i)` matches original for all i
    - **Validates: Requirements 9.1, 9.2, 1.5, 6.3, 7.1, 10.3**

  - [x] 2.2 Write property test for narrowest delta type selection (Property 2)
    - **Property 2: Narrowest delta type selection**
    - Generate random anchor offset and random deltas in controlled ranges (byte, ushort, uint, long)
    - Build a block's worth of offsets, seal index
    - Inspect internal `DeltaType` of finalized block via reflection or internal accessor
    - Verify narrowest type selected for max delta in block
    - **Validates: Requirements 1.4**

  - [x]* 2.3 Write property test for memory efficiency (Property 3)
    - **Property 3: Memory efficiency by delta range**
    - Generate monotonically increasing offsets with controlled max delta (<256 for byte, <65536 for ushort), at least 1000 lines
    - Build index, compute memory via `GetMemoryBytes()` method
    - Verify ≤30% of flat index for byte-range deltas, ≤40% for ushort-range deltas
    - **Validates: Requirements 4.1, 4.2**

  - [x]* 2.4 Write property test for clamping correctness (Property 4)
    - **Property 4: ReadLinesAsync clamping preserves correctness**
    - Generate random `CompressedLineIndex` (random offsets, sealed)
    - Generate random `(startLine, lineCount)` pairs including out-of-range values
    - Apply clamping logic, verify result bounds and `TotalLines`
    - **Validates: Requirements 5.3**

  - [x]* 2.5 Write property test for concurrent read/write safety (Property 5)
    - **Property 5: Concurrent read/write safety**
    - Generate random offset sequence, build index with `EnableConcurrentAccess()`
    - Spawn parallel reader tasks calling `GetOffset`/`LineCount` while writer calls `AddOffset`
    - Verify no exceptions, consistent `LineCount` snapshots, correct `GetOffset` values
    - **Validates: Requirements 8.1**

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Integrate CompressedLineIndex into FileService
  - [x] 4.1 Change `_lineIndexCache` type from `Dictionary<string, List<long>>` to `Dictionary<string, CompressedLineIndex>`
    - Update field declaration
    - _Requirements: 6.1_

  - [x] 4.2 Update `OpenFileAsync` to build CompressedLineIndex
    - Replace `var lineOffsets = new List<long>()` with `var index = new CompressedLineIndex()`
    - Replace all `lineOffsets.Add(offset)` with `index.AddOffset(offset)`
    - At partial threshold: call `index.EnableConcurrentAccess()`, store in cache, use `index.LineCount` for partial metadata
    - Remove `lock (_indexLock)` around individual `lineOffsets.Add()` calls — concurrency now handled inside `CompressedLineIndex`
    - After scan loop: call `index.Seal()`
    - Remove trailing-offset cleanup logic (`Seal()` handles partial blocks)
    - Set `totalLines = index.LineCount`
    - Store final index in cache
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 6.1, 6.2, 6.3, 7.3, 8.2, 8.3_

  - [x] 4.3 Update `ReadLinesAsync` to use CompressedLineIndex
    - Change `_lineIndexCache.TryGetValue` to retrieve `CompressedLineIndex` instead of `List<long>`
    - Replace `snapshotCount = lineOffsets.Count` with `snapshotCount = index.LineCount`
    - Replace `startOffset = lineOffsets[startLine]` with `startOffset = index.GetOffset(startLine)`
    - _Requirements: 5.1, 5.2, 5.3, 2.1, 2.2, 2.3_

  - [x] 4.4 Update existing FileService property tests to work with CompressedLineIndex
    - Update `FileServiceProperties.cs` reflection-based tests that access `_lineIndexCache` as `Dictionary<string, List<long>>` to use `CompressedLineIndex` API
    - Ensure existing round-trip, clamping, and concurrent access property tests still pass
    - _Requirements: 9.1, 9.2, 9.3_

- [x] 5. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Add memory measurement and integration validation
  - [x] 6.1 Implement `GetMemoryBytes()` method on CompressedLineIndex
    - Calculate total memory: block struct overhead + anchor storage + delta array sizes + pending buffer
    - Used by Property 3 tests and the 10M-line benchmark
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 6.2 Write integration test: OpenFileAsync produces CompressedLineIndex and ReadLinesAsync returns correct content
    - Create temp file, open via FileService, read lines at various positions
    - Verify identical content to expected lines
    - _Requirements: 6.1, 9.3, 5.1_

  - [x] 6.3 Write memory benchmark test: 10M-line file index uses < 25 MB
    - Generate 10M monotonically increasing offsets (avg line length 60 bytes)
    - Build CompressedLineIndex, call `GetMemoryBytes()`
    - Assert < 25 MB
    - _Requirements: 4.3_

- [x] 7. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (Properties 1–5)
- Unit tests validate specific examples and edge cases
- The design uses C# throughout — no language selection needed
- Test project: `tests/EditorApp.Tests/` (xUnit + FsCheck 3.1.0)
- Build: `dotnet build src/EditorApp`
- Test: `dotnet test tests/EditorApp.Tests`
