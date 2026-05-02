# Requirements Document

## Introduction

The editor's `FileService` maintains a `_lineIndexCache` that stores a `List<long>` of byte offsets — one entry per line in the file. Each `long` consumes 8 bytes, so a 10-million-line file requires ~80 MB just for the line index. This memory cost is disproportionate to the actual information content, since consecutive line offsets are highly correlated (most lines are similar length, so deltas between offsets are small and compressible).

This feature replaces the flat `List<long>` with a **block-based sparse index using delta encoding**. The index stores absolute offsets only for every Nth line (block anchors), and within each block stores delta-encoded offsets using the smallest integer type that fits. This reduces memory usage by 70–90% for typical files while preserving O(1)-like random access to any line offset.

The optimization is transparent to callers — `ReadLinesAsync` and `OpenFileAsync` continue to work identically. Only the internal storage representation changes.

## Glossary

- **File_Service**: The C# backend service (`FileService`) responsible for opening files, building line-offset indices, and reading line ranges.
- **Line_Offset_Index**: The in-memory data structure mapping line numbers to byte offsets, built during the Scanning_Phase of `OpenFileAsync`.
- **Flat_Index**: The current implementation — a `List<long>` storing one absolute byte offset per line.
- **Compressed_Index**: The new implementation — a block-based structure storing absolute anchor offsets and delta-encoded intra-block offsets.
- **Block**: A fixed-size group of consecutive line offsets (e.g., 128 or 256 lines). Each block stores one absolute anchor offset and delta-encoded offsets for the remaining lines in the block.
- **Block_Size**: The number of lines per block. A compile-time or configuration constant (e.g., 128).
- **Anchor_Offset**: The absolute byte offset of the first line in a Block, stored as a `long`.
- **Delta**: The difference between a line's byte offset and the Anchor_Offset of its containing Block. Deltas are always non-negative.
- **Delta_Encoding**: Storing each line offset within a block as the difference from the block's Anchor_Offset, rather than as an absolute value.
- **Scanning_Phase**: The portion of `FileService.OpenFileAsync` that reads raw bytes to build the Line_Offset_Index.
- **Large_File**: A file whose size in bytes is strictly greater than 256,000 (the existing `SizeThresholdBytes` constant).
- **Small_File**: A file whose size in bytes is less than or equal to 256,000.

## Requirements

### Requirement 1: Compressed Line Index Data Structure

**User Story:** As a developer, I want the line offset index to use a memory-efficient block-based structure with delta encoding, so that files with millions of lines consume significantly less memory for the index.

#### Acceptance Criteria

1. THE Compressed_Index SHALL organize line offsets into fixed-size Blocks of Block_Size consecutive lines.
2. THE Compressed_Index SHALL store one Anchor_Offset (absolute `long` byte offset) per Block for the first line in that Block.
3. THE Compressed_Index SHALL store the remaining line offsets within each Block as Deltas relative to the Block's Anchor_Offset.
4. THE Compressed_Index SHALL select the narrowest integer type for Deltas within each Block: `byte` if all Deltas fit in 8 bits, `ushort` if all fit in 16 bits, `uint` if all fit in 32 bits, and `long` only if a Delta exceeds 32 bits.
5. THE Compressed_Index SHALL store the total line count so that callers can query it without iterating the structure.

### Requirement 2: Line Offset Lookup from Compressed Index

**User Story:** As a developer, I want to retrieve the byte offset for any line number from the compressed index, so that `ReadLinesAsync` can seek to arbitrary lines efficiently.

#### Acceptance Criteria

1. WHEN a line offset is requested for line number N, THE Compressed_Index SHALL compute the block index as `N / Block_Size` and the intra-block position as `N % Block_Size`.
2. WHEN the intra-block position is 0, THE Compressed_Index SHALL return the Anchor_Offset directly.
3. WHEN the intra-block position is greater than 0, THE Compressed_Index SHALL return the Anchor_Offset plus the stored Delta for that position.
4. THE Compressed_Index lookup operation SHALL complete in O(1) time relative to the total number of lines.

### Requirement 3: Index Construction During Scanning

**User Story:** As a developer, I want the scanning phase to build the compressed index incrementally, so that the index is constructed in a single pass without requiring a second pass over the data.

#### Acceptance Criteria

1. WHILE the Scanning_Phase processes bytes, THE File_Service SHALL accumulate line offsets into a temporary buffer for the current Block.
2. WHEN the temporary buffer reaches Block_Size entries, THE File_Service SHALL finalize the Block by computing Deltas relative to the first offset and selecting the narrowest integer type.
3. WHEN the Scanning_Phase completes, THE File_Service SHALL finalize any remaining partial Block (fewer than Block_Size entries) using the same delta-encoding logic.
4. THE File_Service SHALL not require the entire Flat_Index to be held in memory at any point — Blocks SHALL be finalized incrementally as they fill.

### Requirement 4: Memory Reduction Target

**User Story:** As a user, I want the line index to use substantially less memory for large files, so that opening files with millions of lines does not cause excessive memory consumption.

#### Acceptance Criteria

1. FOR a file where all lines are shorter than 256 bytes, THE Compressed_Index SHALL use no more than 30% of the memory that the equivalent Flat_Index would use.
2. FOR a file where all lines are shorter than 65,536 bytes, THE Compressed_Index SHALL use no more than 40% of the memory that the equivalent Flat_Index would use.
3. FOR a file with 10 million lines of typical source code (average line length 40–80 bytes), THE Compressed_Index SHALL use less than 25 MB of memory for the index (compared to ~80 MB for the Flat_Index).

### Requirement 5: Transparent Integration with ReadLinesAsync

**User Story:** As a developer, I want `ReadLinesAsync` to work identically after the index format change, so that no callers need modification.

#### Acceptance Criteria

1. WHEN `ReadLinesAsync` is called with a start line and line count, THE File_Service SHALL resolve the byte offset for the start line from the Compressed_Index and seek to that position.
2. THE `ReadLinesAsync` method signature and return type (`LinesResult`) SHALL remain unchanged.
3. THE `ReadLinesAsync` method SHALL continue to clamp requested ranges to the available line count and return partial results when the request exceeds the indexed range.

### Requirement 6: Transparent Integration with OpenFileAsync

**User Story:** As a developer, I want `OpenFileAsync` to produce the compressed index instead of the flat index, so that the optimization is applied automatically when files are opened.

#### Acceptance Criteria

1. WHEN `OpenFileAsync` completes scanning, THE File_Service SHALL store a Compressed_Index in the cache instead of a Flat_Index.
2. THE `OpenFileAsync` method signature and return type (`FileOpenMetadata`) SHALL remain unchanged.
3. THE `FileOpenMetadata.TotalLines` value SHALL be identical whether produced by the Compressed_Index or the former Flat_Index for the same file.

### Requirement 7: Partial Index Compatibility

**User Story:** As a developer, I want the compressed index to support partial reads during scanning (for the early-content-display feature), so that the UI can display content before the full scan completes.

#### Acceptance Criteria

1. WHILE the Scanning_Phase is in progress, THE Compressed_Index SHALL report the count of lines indexed so far (including lines in finalized Blocks and lines in the in-progress temporary buffer).
2. WHILE the Scanning_Phase is in progress, THE Compressed_Index SHALL allow offset lookups for any line that has been indexed, whether it resides in a finalized Block or the in-progress temporary buffer.
3. WHEN the partial metadata threshold is crossed (256,000 bytes scanned), THE File_Service SHALL make the partially built Compressed_Index available for `ReadLinesAsync` calls, consistent with the existing early-content-display behavior.

### Requirement 8: Thread Safety for Concurrent Access

**User Story:** As a developer, I want concurrent reads and writes to the compressed index to be safe, so that `ReadLinesAsync` calls do not corrupt or crash while the scanning thread appends new blocks.

#### Acceptance Criteria

1. WHILE the Scanning_Phase is appending entries to the Compressed_Index, THE File_Service SHALL ensure that concurrent `ReadLinesAsync` calls read a consistent snapshot without data corruption.
2. THE File_Service SHALL use a thread-safe synchronization mechanism to protect the Compressed_Index during concurrent read and write operations.
3. THE synchronization mechanism SHALL not block the scanning thread for longer than the time to append a single Block.

### Requirement 9: Correctness — Round-Trip Offset Equivalence

**User Story:** As a developer, I want to verify that the compressed index produces identical byte offsets to the flat index for every line, so that the optimization does not introduce seek errors.

#### Acceptance Criteria

1. FOR ALL valid line numbers in a file, THE Compressed_Index SHALL return the same byte offset that the Flat_Index would return for that line number.
2. FOR ALL valid files, THE Compressed_Index total line count SHALL equal the Flat_Index total line count.
3. FOR ALL valid files, reading lines via `ReadLinesAsync` using the Compressed_Index SHALL produce identical line content to reading via the Flat_Index.

### Requirement 10: Block Size Configuration

**User Story:** As a developer, I want the block size to be a named constant, so that it can be tuned for different performance/memory trade-offs without code changes throughout the codebase.

#### Acceptance Criteria

1. THE Block_Size SHALL be defined as a named constant in the Compressed_Index implementation.
2. THE Block_Size constant SHALL have a default value of 128 lines.
3. WHEN the Block_Size value is changed, THE Compressed_Index SHALL function correctly for any Block_Size value that is a power of 2 between 32 and 1024 inclusive.
