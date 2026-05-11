# Design: CLI File Open

## Overview

Minimal change to `Program.cs` to extract a file path from `args`, resolve it, and pass it to `PhotinoHostService` which triggers `OpenFileByPathAsync` after the window is ready.

## Architecture

```
Program.Main(args)
  ├── Parse args → extract filePath (first non-framework arg)
  ├── Resolve relative path against Environment.CurrentDirectory
  └── Pass filePath to PhotinoHostService

PhotinoHostService
  ├── New: accepts optional `initialFilePath` parameter
  └── Run() → after StartListening(), if initialFilePath set:
        schedule OpenFileByPathAsync on UI thread
```

## Detailed Design

### 1. Argument Parsing (Program.cs)

No external library needed. Simple positional arg extraction:

```csharp
// After builder.Build(), before hostService creation:
string? initialFilePath = null;
if (args.Length > 0 && !args[0].StartsWith('-'))
{
    var raw = args[0];
    initialFilePath = Path.IsPathRooted(raw)
        ? raw
        : Path.GetFullPath(raw, Environment.CurrentDirectory);
}
```

Logic:
- Skip args starting with `-` (framework flags like `--urls`)
- Take first positional arg as file path
- Resolve relative → absolute using `Path.GetFullPath(raw, CWD)`

### 2. PhotinoHostService Changes

Add optional `initialFilePath` parameter:

```csharp
public PhotinoHostService(PhotinoBlazorApp app, IMessageRouter messageRouter,
    IFileService fileService, IViewportService? viewportService = null,
    string? initialFilePath = null)
```

Store in field `_initialFilePath`.

Modify `Run()`:

```csharp
public void Run()
{
    _messageRouter.StartListening();

    if (_initialFilePath is not null)
    {
        // Schedule file open after window is ready.
        // Use app.MainWindow.Invoke to ensure it runs on the UI thread
        // after the event loop starts processing.
        _app.MainWindow.RegisterWindowCreatedHandler(() =>
        {
            _ = OpenFileByPathAsync(_initialFilePath);
        });
    }

    _app.Run();
}
```

Alternative if `RegisterWindowCreatedHandler` not available: use a short `Task.Delay` or `SynchronizationContext.Post` after `_app.Run()` starts. But since Photino's `WindowCreated` event fires before the event loop blocks, we can hook into it.

### 3. Window Title Update

`OpenFileByPathAsync` already sends `FileOpenedResponse` to UI. The window title update should happen in the same method after successful scan:

```csharp
// After sending FileOpenedResponse (non-partial):
_app.MainWindow.SetTitle($"{metadata.FileName} — Editor");
```

This applies to both CLI-opened and dialog-opened files (consistent behavior).

## Files Modified

| File | Change |
|------|--------|
| `src/EditorApp/Program.cs` | Extract file path from args, pass to PhotinoHostService |
| `src/EditorApp/Services/PhotinoHostService.cs` | Add `initialFilePath` param, trigger open in Run() |

## Testing Strategy

- **Unit test**: Argument parsing logic (relative/absolute/flag filtering)
- **Unit test**: PhotinoHostService with initialFilePath triggers OpenFileByPathAsync
- **Property test**: Path resolution correctness — for any valid relative path, resolved path equals `Path.GetFullPath`
