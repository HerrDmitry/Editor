# Test Design Document: Editor App

## Overview

This document describes the unit testing strategy for the Editor App backend (C# .NET 10). Tests use xUnit for example-based tests and FsCheck for property-based tests. The test project is at `tests/EditorApp.Tests/`.

## Testable Components

### 1. FileService

The core service for streamed file reading. All methods operate on real temp files — no file system abstraction needed since the service's purpose is file I/O.

#### 1.1 OpenFileAsync — Line Offset Index Building

Tests verify that the line offset index is built correctly for various file contents and line endings.

| Test | Input | Expected |
|------|-------|----------|
| SingleLine_NoNewline | `"hello"` | TotalLines=1, ReadLines(0,1)=["hello"] |
| SingleLine_TrailingLF | `"hello\n"` | TotalLines=1, ReadLines(0,1)=["hello"] |
| MultipleLines_LF | `"a\nb\nc"` | TotalLines=3 |
| MultipleLines_CRLF | `"a\r\nb\r\nc"` | TotalLines=3 |
| MultipleLines_CR | `"a\rb\rc"` | TotalLines=3 |
| MixedLineEndings | `"a\nb\r\nc\rd"` | TotalLines=4 |
| EmptyFile | `""` | TotalLines=1 (one empty line) |
| OnlyNewlines | `"\n\n\n"` | TotalLines=3 |
| FileNotFound | non-existent path | throws FileNotFoundException |
| PermissionDenied | unreadable file | throws UnauthorizedAccessException |
| UTF8_WithBOM | UTF-8 BOM + content | Encoding="UTF-8", content readable |
| UTF16LE_WithBOM | UTF-16 LE BOM + content | Encoding="UTF-16 LE" |

#### 1.2 ReadLinesAsync — Seeking and Reading

Tests verify that seeking to a line offset and reading N lines returns correct content.

| Test | Setup | Input | Expected |
|------|-------|-------|----------|
| ReadFirstLines | 100-line file | startLine=0, count=5 | lines 1-5 |
| ReadMiddleLines | 100-line file | startLine=50, count=5 | lines 51-55 |
| ReadLastLines | 100-line file | startLine=95, count=10 | lines 96-100 (clamped to 5) |
| ReadBeyondEnd | 100-line file | startLine=200, count=5 | empty array |
| NegativeStartLine | 100-line file | startLine=-5, count=5 | lines 1-5 (clamped to 0) |
| ZeroLineCount | 100-line file | startLine=0, count=0 | empty array |
| FileNotOpened | no prior OpenFileAsync | any | throws InvalidOperationException |
| ContentPreservation | file with tabs, spaces, special chars | any range | exact content match |

#### 1.3 DetectEncoding — BOM Detection

Tests verify encoding detection from file BOM bytes.

| Test | File BOM | Expected Encoding |
|------|----------|-------------------|
| NoBOM | no BOM | UTF-8 (fallback) |
| UTF8_BOM | EF BB BF | UTF-8 |
| UTF16LE_BOM | FF FE | UTF-16 LE |
| UTF16BE_BOM | FE FF | UTF-16 BE |
| UTF32LE_BOM | FF FE 00 00 | UTF-32 LE |
| UTF32BE_BOM | 00 00 FE FF | UTF-32 BE |
| SingleByte | 1 byte file | UTF-8 (fallback) |
| EmptyFile | 0 bytes | UTF-8 (fallback) |

#### 1.4 Property-Based Tests (FsCheck)

| Property | Generator | Assertion |
|----------|-----------|-----------|
| LineIndexRoundTrip | random string arrays (1-500 lines) | For each line N, ReadLinesAsync(N, 1) returns the Nth line from the original array |
| TotalLinesAccuracy | random string arrays | OpenFileAsync.TotalLines == number of lines written |
| ReadLinesRangeCorrectness | random startLine + lineCount within bounds | ReadLinesAsync returns exact slice of original lines |
| FileSizeAccuracy | random content | OpenFileAsync.FileSizeBytes == actual file size on disk |

### 2. MessageRouter

Tested with a mock `IPhotinoWindowMessaging` that captures sent messages and allows simulating received messages.

#### 2.1 SendToUIAsync — Outgoing Messages

| Test | Input | Expected |
|------|-------|----------|
| SendsValidEnvelope | FileOpenedResponse | JSON with type="FileOpenedResponse", payload, ISO 8601 timestamp |
| SendsLinesResponse | LinesResponse with lines | JSON with type="LinesResponse", payload.lines array |
| SendsErrorResponse | ErrorResponse | JSON with type="ErrorResponse", payload.errorCode |
| NullMessage | null | throws ArgumentNullException |
| PayloadCamelCase | any message | JSON property names are camelCase |

#### 2.2 HandleMessageAsync — Incoming Messages

| Test | Input | Expected |
|------|-------|----------|
| RoutesToRegisteredHandler | valid OpenFileRequest envelope | handler invoked |
| RoutesToCorrectHandler | two handlers registered, send one type | only matching handler invoked |
| IgnoresEmptyString | `""` | no handler invoked, no exception |
| IgnoresNullString | `null` | no handler invoked, no exception |
| IgnoresNonJsonString | `"_blazor:init"` | no handler invoked (starts with `_`) |
| IgnoresMalformedJson | `"{broken"` | no handler invoked, no exception |
| IgnoresUnknownType | valid JSON with unknown type | no handler invoked |
| IgnoresMissingType | `{"payload":{}}` | no handler invoked |
| HandlerExceptionSwallowed | handler throws | no exception propagated |
| NullPayloadCreatesDefault | envelope with null payload | handler receives default instance |
| DeserializesPayload | RequestLinesMessage with startLine=5 | handler receives object with StartLine=5 |

#### 2.3 RegisterHandler

| Test | Input | Expected |
|------|-------|----------|
| NullHandler | null | throws ArgumentNullException |
| OverwritesPreviousHandler | register twice for same type | second handler used |

### 3. MessageRouter Integration (SendToUI → HandleMessage round-trip)

| Test | Flow | Expected |
|------|------|----------|
| RoundTrip_FileOpenedResponse | SendToUI → capture JSON → HandleMessage | handler receives equivalent object |
| RoundTrip_LinesResponse | SendToUI → capture JSON → HandleMessage | handler receives equivalent object |
| RoundTrip_ErrorResponse | SendToUI → capture JSON → HandleMessage | handler receives equivalent object |

## Test Infrastructure

### MockPhotinoWindowMessaging

A simple mock implementing `IPhotinoWindowMessaging`:

```csharp
public class MockPhotinoWindowMessaging : IPhotinoWindowMessaging
{
    public List<string> SentMessages { get; } = new();
    private Action<string>? _handler;

    public void SendWebMessage(string message) => SentMessages.Add(message);
    public void RegisterWebMessageReceivedHandler(Action<string> handler) => _handler = handler;
    public void SimulateReceive(string message) => _handler?.Invoke(message);
}
```

### TempFileHelper

Utility for creating temp files with specific content and encoding:

```csharp
public static class TempFileHelper
{
    public static string CreateTempFile(string content, Encoding? encoding = null)
    {
        var path = Path.GetTempFileName();
        var enc = encoding ?? Encoding.UTF8;
        File.WriteAllText(path, content, enc);
        return path;
    }

    public static string CreateTempFileWithBom(byte[] bom, byte[] content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bom.Concat(content).ToArray());
        return path;
    }
}
```

## Test Organization

```
tests/EditorApp.Tests/
├── Unit/
│   ├── FileService/
│   │   ├── OpenFileAsyncTests.cs
│   │   ├── ReadLinesAsyncTests.cs
│   │   └── DetectEncodingTests.cs
│   ├── MessageRouter/
│   │   ├── SendToUIAsyncTests.cs
│   │   ├── HandleMessageAsyncTests.cs
│   │   └── RegisterHandlerTests.cs
│   └── Integration/
│       └── MessageRoundTripTests.cs
├── Properties/
│   ├── FileServiceProperties.cs
│   └── MessageRouterProperties.cs
├── Fixtures/
│   ├── MockPhotinoWindowMessaging.cs
│   └── TempFileHelper.cs
└── EditorApp.Tests.csproj
```

## Coverage Goals

- FileService: 90%+ (core business logic)
- MessageRouter: 90%+ (interop layer)
- Overall: 80%+
