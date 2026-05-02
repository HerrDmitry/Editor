# Bugfix Requirements Document

## Introduction

Multiple defects were identified during code review of `FileService.cs` and `CompressedLineIndex.cs`. These range from critical issues (redundant I/O, memory leaks, race conditions) to medium-severity resource leaks and missing cancellation support, plus low-priority consistency and simplification improvements. Together they degrade performance, leak memory under sustained use, and risk data corruption under concurrent access.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN `ReadLinesAsync` is called THEN the system redundantly calls `DetectEncoding(filePath)` which re-opens the file and re-reads BOM bytes, even though encoding was already detected during `OpenFileAsync`

1.2 WHEN multiple files are opened via `OpenFileAsync` over time THEN the system accumulates `CompressedLineIndex` entries in `_lineIndexCache` indefinitely with no eviction or removal mechanism, causing unbounded memory growth

1.3 WHEN `ReadLinesAsync` snapshots the offset inside the lock and then reads the file outside the lock, and the file is modified between `OpenFileAsync` and `ReadLinesAsync` THEN the system reads from a stale offset that may point to the wrong position, returning corrupted or incorrect line content with no file-change detection

1.4 WHEN `EnableConcurrentAccess()` is called on `CompressedLineIndex` creating a `ReaderWriterLockSlim`, and the index is later sealed or the cache entry is evicted THEN the system never disposes the `ReaderWriterLockSlim`, leaking an OS synchronization handle

1.5 WHEN `ReadLinesAsync` reads many lines from slow or network-attached storage THEN the system provides no way to cancel the operation because `ReadLinesAsync` does not accept a `CancellationToken`

1.6 WHEN `OpenFileAsync` creates a `FileStream` THEN the system manually calls `stream.DisposeAsync()` in a `finally` block instead of using `await using`, which is inconsistent with `ReadLinesAsync` that already uses `await using`

1.7 WHEN `_lineIndexCache` is accessed concurrently THEN the system uses a manual `lock` around a plain `Dictionary` instead of using `ConcurrentDictionary`, adding unnecessary complexity for a simple cache lookup pattern

1.8 WHEN the `CountLines(string content)` method exists in `FileService` THEN the system has a potentially unused method since `OpenFileAsync` counts lines via byte scanning, creating dead code that may confuse maintainers

### Expected Behavior (Correct)

2.1 WHEN `OpenFileAsync` detects the file encoding THEN the system SHALL cache the detected encoding alongside the line index, and `ReadLinesAsync` SHALL reuse the cached encoding without re-opening the file for BOM detection

2.2 WHEN a file's line index is no longer needed THEN the system SHALL provide a mechanism to remove entries from `_lineIndexCache` (e.g., a `CloseFile` method), and `CompressedLineIndex` SHALL implement `IDisposable` to clean up its resources when evicted

2.3 WHEN `ReadLinesAsync` reads file content THEN the system SHALL detect whether the file has been modified since `OpenFileAsync` was called (e.g., by comparing file size or last-write timestamp) and SHALL throw an informative exception if the file has changed, preventing reads from stale offsets

2.4 WHEN `CompressedLineIndex` owns a `ReaderWriterLockSlim` THEN the system SHALL dispose it when the index is disposed, by implementing `IDisposable` on `CompressedLineIndex`

2.5 WHEN `ReadLinesAsync` is called THEN the system SHALL accept an optional `CancellationToken` parameter and SHALL pass it through to all async I/O operations, allowing callers to cancel long-running reads

2.6 WHEN `OpenFileAsync` creates a `FileStream` THEN the system SHALL use `await using` for deterministic disposal, consistent with the pattern already used in `ReadLinesAsync`

2.7 WHEN `_lineIndexCache` is accessed THEN the system SHALL use `ConcurrentDictionary<string, ...>` instead of a plain `Dictionary` with manual locking, simplifying concurrent access code

2.8 WHEN `CountLines(string content)` is not used in production code THEN the system SHALL either remove it from `FileService` or document its purpose clearly with a comment indicating it is a test utility

### Unchanged Behavior (Regression Prevention)

3.1 WHEN `OpenFileAsync` is called with a valid file path THEN the system SHALL CONTINUE TO correctly build the `CompressedLineIndex` with accurate byte offsets for all line endings (`\n`, `\r\n`, `\r`)

3.2 WHEN `ReadLinesAsync` is called with a valid file path and line range that has not been modified since opening THEN the system SHALL CONTINUE TO return the correct lines with proper encoding

3.3 WHEN `OpenFileAsync` is called on a large file (>256KB) THEN the system SHALL CONTINUE TO emit partial metadata via `onPartialMetadata` callback and report progress via `IProgress<FileLoadProgress>`

3.4 WHEN `OpenFileAsync` is called with a `CancellationToken` THEN the system SHALL CONTINUE TO support cancellation during the file scanning phase

3.5 WHEN `DetectEncoding` is called on files with various BOMs (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE) or no BOM THEN the system SHALL CONTINUE TO correctly identify the encoding

3.6 WHEN `CompressedLineIndex` stores line offsets THEN the system SHALL CONTINUE TO use delta-encoded block compression with the same memory efficiency characteristics

3.7 WHEN `ReadLinesAsync` is called with out-of-range `startLine` or `lineCount` values THEN the system SHALL CONTINUE TO clamp them and return valid results without throwing exceptions

3.8 WHEN `GetEncodingDisplayName` is called THEN the system SHALL CONTINUE TO return the same human-readable encoding names for all supported encodings
