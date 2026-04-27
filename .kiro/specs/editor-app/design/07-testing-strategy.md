# Testing Strategy

## Overview

The testing strategy employs a dual approach combining property-based testing for universal correctness properties with example-based unit tests for specific scenarios, edge cases, and integration points.

## Property-Based Testing

**Framework**: Use **FsCheck** for C# backend property tests and **fast-check** for TypeScript frontend property tests.

**Configuration**:
- Minimum 100 iterations per property test
- Each property test must reference its design document property using a comment tag
- Tag format: `// Feature: editor-app, Property {number}: {property_text}`

### Backend Property Tests (C# with FsCheck)

1. **Line Offset Index Correctness** (Property 1):
   ```csharp
   // Feature: editor-app, Property 1: Line offset index correctness
   [Property]
   public Property LineOffsetIndexCorrectness()
   {
       return Prop.ForAll(
           Arb.Generate<NonEmptyArray<string>>(),
           linesArray =>
           {
               var content = string.Join("\n", linesArray.Get);
               var tempFile = WriteToTempFile(content);
               var metadata = fileService.OpenFileAsync(tempFile).Result;
               
               // For each line, seek using index and verify content
               for (int i = 0; i < linesArray.Get.Length; i++)
               {
                   var result = fileService.ReadLinesAsync(tempFile, i, 1).Result;
                   if (result.Lines[0] != linesArray.Get[i]) return false;
               }
               return true;
           }
       );
   }
   ```

2. **ReadLinesAsync Round-Trip Correctness** (Property 2):
   ```csharp
   // Feature: editor-app, Property 2: ReadLinesAsync round-trip correctness
   [Property]
   public Property ReadLinesRoundTrip()
   {
       return Prop.ForAll(
           Arb.Generate<NonEmptyArray<string>>(),
           Gen.Choose(0, 100).ToArbitrary(),
           Gen.Choose(1, 50).ToArbitrary(),
           (linesArray, startLine, lineCount) =>
           {
               var content = string.Join("\n", linesArray.Get);
               var tempFile = WriteToTempFile(content);
               fileService.OpenFileAsync(tempFile).Wait();
               
               var clampedStart = Math.Min(startLine, linesArray.Get.Length - 1);
               var clampedCount = Math.Min(lineCount, linesArray.Get.Length - clampedStart);
               
               var result = fileService.ReadLinesAsync(tempFile, clampedStart, clampedCount).Result;
               var expected = linesArray.Get.Skip(clampedStart).Take(clampedCount).ToArray();
               
               return result.Lines.SequenceEqual(expected);
           }
       );
   }
   ```

3. **File Metadata Accuracy** (Property 3):
   ```csharp
   // Feature: editor-app, Property 3: File metadata accuracy
   [Property]
   public Property FileMetadataAccuracy()
   {
       return Prop.ForAll(
           Arb.Generate<NonEmptyArray<string>>(),
           linesArray =>
           {
               var content = string.Join("\n", linesArray.Get);
               var tempFile = WriteToTempFile(content);
               var metadata = fileService.OpenFileAsync(tempFile).Result;
               
               var expectedLines = linesArray.Get.Length;
               var expectedSize = new FileInfo(tempFile).Length;
               
               return metadata.TotalLines == expectedLines &&
                      metadata.FileSizeBytes == expectedSize;
           }
       );
   }
   ```

4. **File Size Formatting** (Property 8):
   ```csharp
   // Feature: editor-app, Property 8: File size human-readable formatting
   [Property]
   public Property FileSizeFormatting()
   {
       return Prop.ForAll(
           Arb.Generate<long>().Where(size => size >= 0),
           fileSize =>
           {
               var formatted = FormatFileSize(fileSize);
               
               if (fileSize < 1024)
                   return formatted.EndsWith(" bytes");
               else if (fileSize < 1048576)
                   return formatted.EndsWith(" KB");
               else
                   return formatted.EndsWith(" MB");
           }
       );
   }
   ```

5. **LinesResponse Message Structure** (Property 10):
   ```csharp
   // Feature: editor-app, Property 10: LinesResponse message structure correctness
   [Property]
   public Property LinesResponseMessageStructure()
   {
       return Prop.ForAll(
           Arb.Generate<int>().Where(i => i >= 0),
           Arb.Generate<string[]>().Where(a => a != null),
           Arb.Generate<int>().Where(i => i >= 0),
           (startLine, lines, totalLines) =>
           {
               var linesResult = new LinesResult(startLine, lines, totalLines);
               var message = messageRouter.SerializeLinesResponse(linesResult);
               var envelope = JsonSerializer.Deserialize<MessageEnvelope>(message);
               
               return envelope.Type == "LinesResponse" &&
                      envelope.Payload != null &&
                      envelope.Timestamp != null;
           }
       );
   }
   ```

### Frontend Property Tests (TypeScript with fast-check)

1. **Title Bar Format** (Property 7):
   ```typescript
   // Feature: editor-app, Property 7: Title bar format consistency
   test('title bar format consistency', () => {
     fc.assert(
       fc.property(fc.string({ minLength: 1 }), (fileName) => {
         const title = formatTitleBar(fileName);
         return title === `${fileName} - Editor`;
       }),
       { numRuns: 100 }
     );
   });
   ```

2. **Line Number Generation** (Property 5):
   ```typescript
   // Feature: editor-app, Property 5: Line number sequential generation
   test('line numbers are sequential from startLine offset', () => {
     fc.assert(
       fc.property(
         fc.nat(10000),
         fc.integer({ min: 1, max: 200 }),
         (startLine, lineCount) => {
           const lineNumbers = generateLineNumbers(startLine, lineCount);
           return lineNumbers.every((num, idx) => num === startLine + idx + 1);
         }
       ),
       { numRuns: 100 }
     );
   });
   ```

3. **Virtual Scrollbar Height** (Property 6):
   ```typescript
   // Feature: editor-app, Property 6: Virtual scrollbar height
   test('scrollbar height equals totalLines * lineHeight', () => {
     fc.assert(
       fc.property(
         fc.integer({ min: 0, max: 10_000_000 }),
         fc.integer({ min: 10, max: 30 }),
         (totalLines, lineHeight) => {
           const height = calculateScrollHeight(totalLines, lineHeight);
           return height === totalLines * lineHeight;
         }
       ),
       { numRuns: 100 }
     );
   });
   ```

4. **Dialog Cancellation Idempotence** (Property 4):
   ```typescript
   // Feature: editor-app, Property 4: Dialog cancellation idempotence
   test('canceling dialog preserves state', () => {
     fc.assert(
       fc.property(fc.record({
         fileMeta: fc.option(fc.record({
           fileName: fc.string(),
           totalLines: fc.nat(),
           fileSizeBytes: fc.nat(),
           encoding: fc.string()
         }), { nil: null }),
         isLoading: fc.boolean(),
         error: fc.option(fc.string(), { nil: null })
       }), (initialState) => {
         const stateBefore = JSON.parse(JSON.stringify(initialState));
         handleDialogCancel(initialState);
         return JSON.stringify(initialState) === JSON.stringify(stateBefore);
       }),
       { numRuns: 100 }
     );
   });
   ```

5. **CustomScrollbar External Position Update** (Property 11):
   ```typescript
   // Feature: editor-app, Property 11: CustomScrollbar external position update correctness
   test('external position update moves thumb without calling onPositionChange', () => {
     fc.assert(
       fc.property(
         fc.integer({ min: 1, max: 100_000 }),   // range
         fc.integer({ min: 1, max: 1000 }),       // viewportSize
         (range, viewportSize) => {
           const position = fc.sample(fc.integer({ min: 0, max: range }), 1)[0];
           const onPositionChange = jest.fn();
           const { rerender } = render(
             <CustomScrollbar range={range} position={0} viewportSize={viewportSize}
                              onPositionChange={onPositionChange} />
           );
           rerender(
             <CustomScrollbar range={range} position={position} viewportSize={viewportSize}
                              onPositionChange={onPositionChange} />
           );
           // Thumb should be at correct position and callback should NOT fire
           return onPositionChange.mock.calls.length === 0;
         }
       ),
       { numRuns: 100 }
     );
   });
   ```

6. **CustomScrollbar Drag Position Calculation** (Property 12):
   ```typescript
   // Feature: editor-app, Property 12: CustomScrollbar drag position calculation
   test('drag position satisfies linear mapping: top=0, bottom=range, center=range/2', () => {
     fc.assert(
       fc.property(
         fc.integer({ min: 1, max: 100_000 }),   // range
         fc.integer({ min: 1, max: 1000 }),       // viewportSize
         fc.double({ min: 0, max: 1, noNaN: true }), // dragFraction (0=top, 1=bottom)
         (range, viewportSize, dragFraction) => {
           const trackHeight = 400;
           const thumbHeight = Math.max(20, (viewportSize / range) * trackHeight);
           const scrollableTrack = trackHeight - thumbHeight;
           const thumbTop = dragFraction * scrollableTrack;
           const expectedPosition = (thumbTop / scrollableTrack) * range;
           const calculatedPosition = calculateDragPosition(thumbTop, scrollableTrack, range);
           return Math.abs(calculatedPosition - expectedPosition) < 0.001;
         }
       ),
       { numRuns: 100 }
     );
   });
   ```

7. **CustomScrollbar Thumb Size Proportionality** (Property 13):
   ```typescript
   // Feature: editor-app, Property 13: CustomScrollbar thumb size proportionality
   test('thumb size is proportional to viewport/range ratio', () => {
     fc.assert(
       fc.property(
         fc.integer({ min: 1, max: 100_000 }),   // range
         fc.integer({ min: 1, max: 10_000 }),    // viewportSize
         (range, viewportSize) => {
           const trackHeight = 400;
           const MIN_THUMB_HEIGHT = 20;
           const expectedThumbHeight = Math.max(MIN_THUMB_HEIGHT, (viewportSize / range) * trackHeight);
           const actualThumbHeight = calculateThumbHeight(range, viewportSize, trackHeight);
           return Math.abs(actualThumbHeight - expectedThumbHeight) < 0.001;
         }
       ),
       { numRuns: 100 }
     );
   });
   ```

## Unit Testing

**Purpose**: Test specific examples, edge cases, error conditions, and integration points that are not suitable for property-based testing.

**Framework**: Use **xUnit** for C# backend tests and **Vitest** for TypeScript frontend tests.

### Backend Unit Tests

1. **Initial State Display** (Requirement 1.2):
   ```csharp
   [Fact]
   public void App_OnLaunch_ShowsEmptyContentArea()
   {
       var app = new App();
       var initialState = app.GetInitialState();
       
       Assert.Null(initialState.FileMeta);
       Assert.False(initialState.IsLoading);
       Assert.Null(initialState.Error);
   }
   ```

2. **Permission Error Handling** (Requirement 2.4):
   ```csharp
   [Fact]
   public async Task OpenFile_PermissionDenied_SendsErrorResponse()
   {
       var mockFileSystem = new Mock<IFileSystem>();
       mockFileSystem
           .Setup(fs => fs.OpenRead(It.IsAny<string>()))
           .Throws(new UnauthorizedAccessException());
       
       var fileService = new FileService(mockFileSystem.Object);
       
       await Assert.ThrowsAsync<UnauthorizedAccessException>(
           () => fileService.OpenFileAsync("test.txt")
       );
       
       mockMessageRouter.Verify(mr => mr.SendToUIAsync(
           It.Is<ErrorResponse>(er => er.ErrorCode == "PERMISSION_DENIED")
       ));
   }
   ```

3. **File Not Found Error Handling** (Requirement 2.5):
   ```csharp
   [Fact]
   public async Task OpenFile_FileNotFound_SendsErrorResponse()
   {
       var mockFileSystem = new Mock<IFileSystem>();
       mockFileSystem
           .Setup(fs => fs.Exists(It.IsAny<string>()))
           .Returns(false);
       
       var fileService = new FileService(mockFileSystem.Object);
       
       await Assert.ThrowsAsync<FileNotFoundException>(
           () => fileService.OpenFileAsync("missing.txt")
       );
       
       mockMessageRouter.Verify(mr => mr.SendToUIAsync(
           It.Is<ErrorResponse>(er => er.ErrorCode == "FILE_NOT_FOUND")
       ));
   }
   ```

4. **Empty State Title Bar** (Requirement 4.1):
   ```csharp
   [Fact]
   public void TitleBar_NoFileOpen_DisplaysAppName()
   {
       var title = FormatTitleBar(null);
       Assert.Equal("Editor", title);
   }
   ```

5. **Empty State Status Bar** (Requirement 5.4):
   ```csharp
   [Fact]
   public void StatusBar_NoFileOpen_DisplaysNoMetadata()
   {
       var statusBar = new StatusBar();
       var display = statusBar.GetDisplay(null);
       Assert.Empty(display);
   }
   ```

### Frontend Unit Tests

1. **Loading State Display** (Requirement 6.3):
   ```typescript
   test('displays loading indicator while file is being scanned', () => {
     const { getByText } = render(
       <ContentArea fileMeta={null} lines={null} linesStartLine={0}
                    isLoading={true} error={null} onRequestLines={() => {}} />
     );
     expect(getByText('Scanning file...')).toBeInTheDocument();
   });
   ```

2. **Error State Display**:
   ```typescript
   test('displays error message when error occurs', () => {
     const error = {
       errorCode: 'FILE_NOT_FOUND',
       message: 'The selected file could not be found.'
     };
     const { getByText } = render(
       <ContentArea fileMeta={null} lines={null} linesStartLine={0}
                    isLoading={false} error={error} onRequestLines={() => {}} />
     );
     expect(getByText(error.message)).toBeInTheDocument();
   });
   ```

3. **Monospaced Font Styling** (Requirement 3.3):
   ```typescript
   test('content area uses monospaced font', () => {
     const { container } = render(
       <ContentArea fileMeta={mockFileMeta} lines={['hello']} linesStartLine={0}
                    isLoading={false} error={null} onRequestLines={() => {}} />
     );
     const contentLine = container.querySelector('.content-line');
     const styles = window.getComputedStyle(contentLine);
     expect(styles.fontFamily).toMatch(/Consolas|Monaco|Courier New|monospace/);
   });
   ```

4. **Horizontal Scrolling** (Requirement 3.6):
   ```typescript
   test('content area provides horizontal scrolling for wide lines', () => {
     const longLine = 'x'.repeat(5000);
     const { container } = render(
       <ContentArea fileMeta={mockFileMeta} lines={[longLine]} linesStartLine={0}
                    isLoading={false} error={null} onRequestLines={() => {}} />
     );
     const contentArea = container.querySelector('.content-area');
     expect(contentArea.scrollWidth).toBeGreaterThan(contentArea.clientWidth);
   });
   ```

5. **Interop Communication Failure** (Requirement 7.4):
   ```typescript
   test('displays error when interop fails', () => {
     const mockInterop = {
       sendOpenFileRequest: jest.fn(() => { throw new Error('Interop failure'); }),
       sendRequestLines: jest.fn(),
       onFileOpened: jest.fn(),
       onLinesResponse: jest.fn(),
       onError: jest.fn(),
     };
     const { getByText } = render(<App interop={mockInterop} />);
     fireEvent.click(getByText('Open File'));
     expect(getByText(/communication failure/i)).toBeInTheDocument();
   });
   ```

## Integration Testing

**Purpose**: Test end-to-end flows and integration with external systems (OS file dialogs, Photino interop).

**Approach**: Use manual testing or UI automation tools for integration tests.

**Key Integration Tests**:

1. **Application Startup** (Requirement 1.1):
   - Launch application binary
   - Verify window appears within 3 seconds
   - Verify initial state is displayed

2. **Native File Picker** (Requirement 2.1):
   - Trigger open file action
   - Verify native OS file dialog appears
   - Verify dialog is modal and blocks application

3. **Streamed File Open** (Requirement 6.1, 6.2):
   - Open a large file (100MB+)
   - Verify the scan completes and metadata is sent
   - Verify memory usage stays bounded (not loading entire file)

4. **Virtual Scroll Line Requests** (Requirement 3.5, 7.3):
   - Open a file, scroll to various positions
   - Verify RequestLinesMessage is sent and LinesResponse is received
   - Verify correct lines are displayed at each scroll position

5. **Keyboard Shortcut** (Requirement 8.1):
   - Press Ctrl+O (or Cmd+O on macOS)
   - Verify file picker dialog appears

6. **Cross-Platform Compatibility** (Requirement 1.4):
   - Run application on Windows, macOS, and Linux
   - Verify all features work on each platform

## Test Coverage Goals

- **Backend**: 80%+ code coverage for FileService, MessageRouter, and core logic
- **Frontend**: 80%+ code coverage for React components and interop service
- **Property Tests**: 100% coverage of all correctness properties (13 properties)
- **Integration Tests**: Coverage of all cross-boundary interactions (interop, file system, OS dialogs)

## Test Organization

**Backend Tests**:
```
tests/
├── EditorApp.Tests/
│   ├── Unit/
│   │   ├── FileServiceTests.cs
│   │   ├── MessageRouterTests.cs
│   │   └── KeyboardShortcutHandlerTests.cs
│   ├── Properties/
│   │   ├── LineIndexProperties.cs
│   │   ├── ReadLinesProperties.cs
│   │   ├── MetadataProperties.cs
│   │   ├── FormattingProperties.cs
│   │   └── InteropProperties.cs
│   └── Integration/
│       ├── PhotinoHostTests.cs
│       └── EndToEndTests.cs
```

**Frontend Tests**:
```
src/
├── components/
│   ├── __tests__/
│   │   ├── App.test.tsx
│   │   ├── ContentArea.test.tsx
│   │   ├── TitleBar.test.tsx
│   │   └── StatusBar.test.tsx
│   └── __properties__/
│       ├── titleBar.properties.test.ts
│       ├── lineNumbers.properties.test.ts
│       ├── scrollbar.properties.test.ts
│       └── statePreservation.properties.test.ts
└── services/
    └── __tests__/
        └── InteropService.test.ts
```

## Continuous Integration

- Run all unit tests and property tests on every commit
- Run integration tests on pull requests
- Enforce minimum code coverage thresholds
- Run tests on all target platforms (Windows, macOS, Linux) in CI pipeline

## Detailed Test Design

For the complete test design including test matrices, test infrastructure (mocks, helpers), and property-based test specifications, see **[test-design.md](../test-design.md)**.
