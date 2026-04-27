# Overview

The Editor App is a cross-platform desktop application for read-only file viewing, built using a hybrid architecture that combines C# .NET 10 with Photino.Blazor for native window hosting and React/TypeScript for the user interface layer. The application compiles into a single self-contained executable that embeds all resources, eliminating external dependencies.

## Key Design Goals

1. **Cross-Platform Native Experience**: Leverage Photino.Blazor to provide native window management across Windows, macOS, and Linux while maintaining a consistent UI through React
2. **Single Executable Deployment**: Embed all resources (React bundle, static assets) into the compiled binary for zero-installation deployment
3. **Clear Separation of Concerns**: Maintain distinct boundaries between native backend (file I/O, OS integration) and web-based UI (rendering, user interaction)
4. **Streamed Reading for Any File Size**: Use a line offset index and on-demand line reading so files of any size can be opened without loading the entire file into memory
5. **Reliable Interop**: Establish robust message-passing between C# backend and React frontend

## Technology Stack

- **Backend**: C# .NET 10, Photino.NET, Photino.Blazor 4.0.13
- **Frontend**: React 19+ (standalone UMD scripts), TypeScript (compiled by bundled `tsc.js`)
- **Build tooling**: No npm/node_modules. TypeScript is compiled by `node scripts/tsc.js`. React/ReactDOM are standalone JS files in `wwwroot/js/`. Only Node.js runtime is required.
- **Interop**: Photino's JavaScript interop bridge (`window.external.sendMessage` for JS→C#, `window.external.receiveMessage` for C#→JS)
- **Packaging**: .NET publish with `PublishSingleFile=true`, `SelfContained=true`, `GenerateEmbeddedFilesManifest=true`, `StaticWebAssetsEnabled=false`
- **Resource Embedding**: `Microsoft.Extensions.FileProviders.Embedded` with `ManifestEmbeddedFileProvider` — wwwroot files are `EmbeddedResource` items, not `Content`
- **Testing**: FsCheck + xUnit (C# backend)

## Project Structure

All frontend code lives inside the C# project — no separate `frontend/` directory:

```
src/EditorApp/
├── EditorApp.csproj
├── Program.cs
├── App.razor
├── _Imports.razor
├── tsconfig.json              ← TypeScript config
├── scripts/
│   ├── tsc.js                 ← Bundled TypeScript compiler
│   └── lib.*.d.ts             ← TypeScript lib definitions
├── src/                       ← TSX source files
│   ├── App.tsx
│   ├── ContentArea.tsx
│   ├── TitleBar.tsx
│   ├── StatusBar.tsx
│   └── InteropService.ts
├── Models/
├── Services/
└── wwwroot/
    ├── index.html
    └── js/
        ├── react.js           ← Standalone React UMD build
        ├── react-dom.js       ← Standalone ReactDOM UMD build
        ├── App.js             ← Compiled from src/App.tsx by tsc
        ├── ContentArea.js
        ├── TitleBar.js
        ├── StatusBar.js
        └── InteropService.js
```

## Critical Implementation Details

These were discovered during implementation and must be followed:

1. **Photino.Blazor requires `ManifestEmbeddedFileProvider`**: The default `PhysicalFileProvider` looks for a physical `wwwroot/` directory at runtime, which breaks single-file deployment. Register `ManifestEmbeddedFileProvider` in `Program.cs`:
   ```csharp
   builder.Services.AddSingleton<IFileProvider>(
       new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot"));
   ```

2. **wwwroot must be EmbeddedResource, not Content**: In the .csproj:
   ```xml
   <Content Remove="wwwroot\**" />
   <EmbeddedResource Include="wwwroot\**" />
   ```

3. **TypeScript compilation via .csproj target**: No npm needed. The .csproj runs `tsc.js` before build:
   ```xml
   <Target Name="CompileTypeScript" BeforeTargets="BeforeBuild">
     <Exec Command="node scripts/tsc.js -p tsconfig.json" />
   </Target>
   ```

4. **TSX files use `React.createElement`**: Since there's no JSX transform bundler, `tsconfig.json` must set `"jsx": "react"` so TSX compiles to `React.createElement(...)` calls. React is available as a global from the standalone script.

5. **React components exposed via `window`**: Each component file exposes its component to `window` (e.g. `window.renderApp = ...`) so `index.html` can mount them. This is the same pattern used by HyprConfig.

6. **Message receiving uses `window.external.receiveMessage`**: Photino does NOT deliver C#→JS messages via the standard DOM `message` event. The frontend must use:
   ```typescript
   window.external.receiveMessage((msg: string) => { /* handle JSON */ });
   ```

7. **Skip non-JSON messages in MessageRouter**: Blazor sends internal messages (starting with `_`) through the same channel. Guard with:
   ```csharp
   if (trimmed[0] != '{') return; // skip non-JSON
   ```

8. **Single keyboard shortcut handler**: Handle Ctrl+O/Cmd+O in the React `keydown` listener only. Do NOT add a duplicate handler in `index.html` — it causes two native file dialogs.

9. **Single InteropService instance**: Create one instance, register all callbacks on it, and use the same instance for `sendOpenFileRequest()`. Creating multiple instances causes responses to arrive on an instance with no callbacks registered.
