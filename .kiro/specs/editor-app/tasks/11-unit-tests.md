# Unit Tests Implementation

## Overview
Implement unit tests and property-based tests for the Editor App backend services. See [test-design.md](../test-design.md) for the full test design.

## Tasks

- [x] 1. Create test infrastructure
  - [x] 1.1 Create MockPhotinoWindowMessaging
    - Implement IPhotinoWindowMessaging
    - Capture sent messages in a List<string>
    - Store registered handler and expose SimulateReceive(string) method
    - Place in tests/EditorApp.Tests/Fixtures/
  - [x] 1.2 Create TempFileHelper
    - CreateTempFile(string content, Encoding? encoding) — writes content with specified encoding
    - CreateTempFileWithBom(byte[] bom, byte[] content) — writes raw BOM + content bytes
    - Cleanup method to delete temp files after tests
    - Place in tests/EditorApp.Tests/Fixtures/

- [x] 2. FileService — OpenFileAsync tests
  - [x] 2.1 Line ending tests
    - SingleLine_NoNewline: "hello" → TotalLines=1
    - SingleLine_TrailingLF: "hello\n" → TotalLines=1
    - MultipleLines_LF: "a\nb\nc" → TotalLines=3
    - MultipleLines_CRLF: "a\r\nb\r\nc" → TotalLines=3
    - MultipleLines_CR: "a\rb\rc" → TotalLines=3
    - MixedLineEndings: "a\nb\r\nc\rd" → TotalLines=4
    - EmptyFile: "" → TotalLines=1
    - OnlyNewlines: "\n\n\n" → TotalLines=3
    - _Requirements: 9.1_
  - [x] 2.2 Error handling tests
    - FileNotFound: non-existent path → throws FileNotFoundException
    - _Requirements: 9.1_
  - [x] 2.3 Encoding tests
    - UTF8_WithBOM: file with UTF-8 BOM → Encoding="UTF-8"
    - UTF16LE_WithBOM: file with UTF-16 LE BOM → Encoding="UTF-16 LE"
    - NoBOM_DefaultsToUTF8: file without BOM → Encoding="UTF-8"
    - _Requirements: 9.3_

- [x] 3. FileService — ReadLinesAsync tests
  - [x] 3.1 Boundary condition tests
    - ReadFirstLines: 100-line file, startLine=0, count=5 → lines 1-5
    - ReadMiddleLines: 100-line file, startLine=50, count=5 → lines 51-55
    - ReadLastLines: 100-line file, startLine=95, count=10 → clamped to 5 lines
    - ReadBeyondEnd: 100-line file, startLine=200 → empty array
    - NegativeStartLine: startLine=-5 → clamped to 0
    - ZeroLineCount: count=0 → empty array
    - _Requirements: 9.2_
  - [x] 3.2 Error and edge case tests
    - FileNotOpened: ReadLinesAsync without prior OpenFileAsync → throws InvalidOperationException
    - ContentPreservation: file with tabs, spaces, special chars → exact content match
    - _Requirements: 9.2_

- [x] 4. FileService — DetectEncoding tests
  - NoBOM → UTF-8
  - UTF8_BOM (EF BB BF) → UTF-8
  - UTF16LE_BOM (FF FE) → UTF-16 LE
  - UTF16BE_BOM (FE FF) → UTF-16 BE
  - UTF32LE_BOM (FF FE 00 00) → UTF-32 LE
  - UTF32BE_BOM (00 00 FE FF) → UTF-32 BE
  - SingleByte file → UTF-8
  - EmptyFile → UTF-8
  - _Requirements: 9.3_

- [x] 5. FileService — Property-based tests (FsCheck)
  - LineIndexRoundTrip: for random string arrays, each line N read via ReadLinesAsync(N,1) matches original
  - TotalLinesAccuracy: OpenFileAsync.TotalLines matches number of lines written
  - ReadLinesRangeCorrectness: ReadLinesAsync returns exact slice of original lines
  - FileSizeAccuracy: OpenFileAsync.FileSizeBytes matches actual file size
  - _Requirements: 9.4_

- [x] 6. MessageRouter — SendToUIAsync tests
  - SendsValidEnvelope: FileOpenedResponse → JSON with correct type, payload, timestamp
  - SendsLinesResponse: LinesResponse → JSON with lines array
  - SendsErrorResponse: ErrorResponse → JSON with errorCode
  - NullMessage: null → throws ArgumentNullException
  - PayloadCamelCase: property names are camelCase in JSON
  - _Requirements: 9.5_

- [x] 7. MessageRouter — HandleMessageAsync tests
  - RoutesToRegisteredHandler: valid envelope → handler invoked
  - RoutesToCorrectHandler: two handlers, send one type → only matching handler invoked
  - IgnoresEmptyString: "" → no handler, no exception
  - IgnoresNonJsonString: "_blazor:init" → no handler
  - IgnoresMalformedJson: "{broken" → no handler, no exception
  - IgnoresUnknownType: valid JSON, unknown type → no handler
  - IgnoresMissingType: JSON without type field → no handler
  - HandlerExceptionSwallowed: handler throws → no exception propagated
  - NullPayloadCreatesDefault: null payload → handler receives default instance
  - DeserializesPayload: RequestLinesMessage with startLine=5 → handler gets StartLine=5
  - _Requirements: 9.5, 9.6, 9.7_

- [x] 8. MessageRouter — RegisterHandler tests
  - NullHandler: null → throws ArgumentNullException
  - OverwritesPreviousHandler: register twice → second handler used
  - _Requirements: 9.7_

- [x] 9. Message round-trip integration tests
  - RoundTrip_FileOpenedResponse: SendToUI → capture JSON → HandleMessage → handler receives equivalent
  - RoundTrip_LinesResponse: same pattern
  - RoundTrip_ErrorResponse: same pattern
  - _Requirements: 9.5_

- [x] 10. Verify all tests pass
  - Run `dotnet test` and verify all tests pass
  - Check for any compilation errors
  - Verify test count matches expected (approximately 40+ tests)

## Notes
- All FileService tests use real temp files (no file system mocking needed)
- MessageRouter tests use MockPhotinoWindowMessaging
- Property-based tests use FsCheck with minimum 100 iterations
- Temp files should be cleaned up after each test (use IDisposable or xUnit fixtures)
