# Implementation Plan: File Load Progress Bar

## Overview

Add progress reporting to `FileService.OpenFileAsync` scanning phase and render a progress bar in the `StatusBar` component for large files (>256,000 bytes). Backend first (models → service → host), then frontend (interop → app → statusbar), then wiring/testing.

## Tasks

- [x] 1. Add backend data models and message types
  - [x] 1.1 Add `FileLoadProgress` record to `Models/FileModels.cs`
    - Add `public record FileLoadProgress(string FileName, int Percent, long FileSizeBytes);`
    - Used as `IProgress<T>` type parameter
    - _Requirements: 3.2, 3.3, 3.4_
  - [x] 1.2 Add `FileLoadProgressMessage` to `Models/Messages.cs`
    - Implement `IMessage` interface
    - Fields: `fileName` (string), `percent` (int 0–100), `fileSizeBytes` (long)
    - Use `[JsonPropertyName]` attributes matching existing pattern
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 2. Modify `IFileService` and `FileService` for progress and cancellation
  - [x] 2.1 Update `IFileService.OpenFileAsync` signature
    - Add `IProgress<FileLoadProgress>? progress = null` parameter
    - Add `CancellationToken cancellationToken = default` parameter
    - Default params preserve backward compat
    - _Requirements: 1.1, 9.1_
  - [x] 2.2 Add constants to `FileService`
    - Add `public const long SizeThresholdBytes = 256_000;`
    - Add `private const int ProgressThrottleMs = 50;`
    - _Requirements: 8.1, 8.2, 1.5_
  - [x] 2.3 Implement progress reporting in `FileService.OpenFileAsync` scanning loop
    - Check `fileInfo.Length > SizeThresholdBytes` before reporting
    - Report `percent = 0` at start for large files
    - Calculate `percent = (int)Math.Round((double)totalBytesRead / fileSize * 100)` after each buffer read
    - Throttle: only report if ≥50ms since last report OR percent == 100
    - Report `percent = 100` at end for large files
    - Small files (≤256,000 bytes): zero progress messages
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 8.1, 8.2_
  - [x] 2.4 Implement cancellation support in `FileService.OpenFileAsync`
    - Check `cancellationToken.ThrowIfCancellationRequested()` each loop iteration
    - Wrap stream in `try/finally` to ensure disposal on cancellation
    - No progress messages after cancellation
    - _Requirements: 9.1, 9.2, 9.4_
  - [x] 2.5 Write property test: percentage calculation correctness (Property 1)
    - **Property 1: Progress percentage calculation correctness**
    - Generate random (bytesScanned, totalFileSize) pairs where 0 ≤ bytesScanned ≤ totalFileSize, totalFileSize > 0
    - Verify formula `(int)Math.Round((double)bytesScanned / totalFileSize * 100)` and result in [0, 100]
    - **Validates: Requirements 1.4, 3.3**
  - [x] 2.6 Write property test: small file suppression at threshold boundary (Property 4)
    - **Property 4: Small file suppression at threshold boundary**
    - Generate file sizes around 256,000 (±1)
    - Files ≤256,000 → zero progress messages; files >256,000 → ≥2 messages (0% and 100%)
    - **Validates: Requirements 2.1, 8.1, 8.2**
  - [x] 2.7 Write property test: large file progress message sequence integrity (Property 2)
    - **Property 2: Large file progress message sequence integrity**
    - Generate random large file sizes, mock stream reads
    - Verify first=0, last=100, monotonically non-decreasing
    - **Validates: Requirements 1.1, 1.2, 1.3**
  - [x] 2.8 Write property test: progress message throttle (Property 3)
    - **Property 3: Progress message throttle**
    - Generate random byte-read sequences with timestamps
    - Verify ≥50ms between consecutive messages (except final 100% message)
    - **Validates: Requirements 1.5**
  - [x] 2.9 Write unit tests for `FileService` progress and cancellation
    - Test: small file (100 bytes) produces no progress messages
    - Test: file exactly at threshold (256,000 bytes) produces no progress
    - Test: file at threshold+1 (256,001 bytes) produces progress
    - Test: cancellation stops progress and releases file handle
    - Test: after cancellation, new file opens normally
    - _Requirements: 2.1, 8.1, 9.1, 9.2, 9.4, 9.5_

- [x] 3. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Modify `PhotinoHostService` for cancellation and progress forwarding
  - [x] 4.1 Add cancellation field and progress forwarding to `PhotinoHostService`
    - Add `CancellationTokenSource? _scanCts` field
    - In `HandleOpenFileRequestAsync`: cancel existing `_scanCts` if not null → `_scanCts.Cancel(); _scanCts.Dispose();`
    - Create new `CancellationTokenSource`
    - Create `Progress<FileLoadProgress>` that sends `FileLoadProgressMessage` via `_messageRouter.SendToUIAsync`
    - Call `_fileService.OpenFileAsync(filePath, progress, _scanCts.Token)`
    - Catch `OperationCanceledException` — log, no error sent to UI
    - _Requirements: 9.1, 9.2, 9.3, 9.5, 1.1_
  - [x] 4.2 Write property test: cancellation stops progress and releases resources (Property 8)
    - **Property 8: Cancellation stops progress and releases resources**
    - Cancel at random byte offsets during scan
    - Verify zero post-cancel messages, no leaked handles, no unhandled exceptions
    - **Validates: Requirements 9.1, 9.2, 9.4**
  - [x] 4.3 Write property test: error halts progress messages (Property 7)
    - **Property 7: Error halts progress messages**
    - Inject errors at random points during scan
    - Verify zero post-error progress messages, ErrorResponse sent
    - **Validates: Requirements 7.1, 7.3**
  - [x] 4.4 Write unit tests for `PhotinoHostService` cancellation logic
    - Test: new file open cancels previous scan
    - Test: cancelled scan does not send ErrorResponse to UI
    - _Requirements: 9.1, 9.3_

- [x] 5. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Update frontend `InteropService` for progress callbacks
  - [x] 6.1 Add `FileLoadProgressMessage` handling to `InteropService.ts`
    - Add `FileLoadProgressMessage` to `MessageTypes` const
    - Add `FileLoadProgressPayload` interface: `{ fileName: string; percent: number; fileSizeBytes: number; }`
    - Add `fileLoadProgressCallbacks` array
    - Add `onFileLoadProgress(callback)` method to public API and `InteropService` interface
    - Add case in `handleMessage` switch for `FileLoadProgressMessage` → invoke callbacks
    - Clear `fileLoadProgressCallbacks` in `dispose()`
    - _Requirements: 6.1, 6.2, 6.3_
  - [ ]* 6.2 Write property test: InteropService dispatches progress to all registered callbacks (Property 6)
    - **Property 6: InteropService dispatches progress to all registered callbacks**
    - Generate random payloads, register N callbacks
    - Verify all invoked with correct data, no other callback types invoked
    - **Validates: Requirements 6.2**
  - [ ]* 6.3 Write unit tests for `InteropService` progress handling
    - Test: `dispose()` clears progress callbacks
    - Test: `FileLoadProgressMessage` JSON field names correct
    - _Requirements: 6.3, 3.1, 3.2, 3.4_

- [x] 7. Update `App.tsx` for progress state management
  - [x] 7.1 Add progress state and callbacks to `App.tsx`
    - Add state: `const [loadProgress, setLoadProgress] = React.useState<FileLoadProgressPayload | null>(null)`
    - Register `interop.onFileLoadProgress` callback → `setLoadProgress(data)`
    - When `data.percent === 100` → `setLoadProgress(null)` (hide bar)
    - On `onFileOpened` → `setLoadProgress(null)` (clear lingering progress)
    - On `onError` → `setLoadProgress(null)` (hide on error)
    - Pass `loadProgress` to `StatusBar` as new prop
    - _Requirements: 4.1, 4.4, 7.2_

- [x] 8. Update `StatusBar` for progress bar rendering
  - [x] 8.1 Add progress bar to `StatusBar.tsx`
    - Add `loadProgress` prop to `StatusBarProps`
    - When `loadProgress` is non-null and `loadProgress.percent < 100`: render progress bar instead of metadata items
    - Progress bar: `<div role="progressbar" aria-valuenow={percent} aria-valuemin={0} aria-valuemax={100}>`
    - Inner fill div with `width: ${percent}%`
    - Text: `Loading: {percent}%`
    - When `loadProgress` is null or `percent === 100`: render normal metadata
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_
  - [x] 8.2 Add progress bar CSS styles to `css/app.css`
    - Background color consistent with status bar (`#007acc`)
    - Fill portion uses lighter/contrasting color visible against status bar
    - Text color white (`#ffffff`)
    - Height ≤ status bar height (22px min)
    - _Requirements: 5.1, 5.2, 5.3, 5.4_
  - [ ]* 8.3 Write property test: progress bar rendering correctness (Property 5)
    - **Property 5: Progress bar rendering correctness**
    - Generate random percent [0, 99]
    - Verify: fill width = `percent%`, text = `Loading: {percent}%`, `role="progressbar"`, `aria-valuenow`, `aria-valuemin="0"`, `aria-valuemax="100"`
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.5**
  - [ ]* 8.4 Write unit tests for `StatusBar` progress bar
    - Test: progress bar hidden when percent=100 received
    - Test: progress bar hidden on error (null progress)
    - Test: styling matches spec (background, fill, text color, height)
    - _Requirements: 4.4, 7.2, 5.1, 5.2, 5.3, 5.4_

- [x] 9. Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Integration wiring and end-to-end validation
  - [ ]* 10.1 Write integration tests for full progress pipeline
    - Test: open large file → progress messages → FileOpenedResponse (full pipeline)
    - Test: open large file, open another mid-scan → cancellation → new file loads
    - _Requirements: 1.1, 1.2, 1.3, 9.1, 9.5, 9.6_

- [x] 11. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from design doc (Properties 1–8)
- Unit tests validate specific examples and edge cases
- Backend (C#) uses xunit + FsCheck for property-based tests
- Frontend (TypeScript) tests may use DOM assertions or equivalent
- `InternalsVisibleTo` already configured for `EditorApp.Tests`
