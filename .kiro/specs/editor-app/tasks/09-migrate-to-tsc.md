# Migrate Frontend to Bundled tsc (No npm)

## Overview
Migrate the frontend from a separate Vite/npm project (`frontend/`) to TSX files compiled by a bundled `tsc.js` inside the C# project. React and ReactDOM become standalone JS files. No npm, no node_modules, no separate frontend project.

## Tasks

- [x] 1. Set up TypeScript compilation infrastructure
  - [x] 1.1 Download tsc.js and TypeScript lib definitions into src/EditorApp/scripts/
    - Download `tsc.js` from the TypeScript npm package on unpkg/jsdelivr (the standalone compiler bundle)
    - Download all required `lib.*.d.ts` files from the TypeScript GitHub repo (lib/ directory)
    - Download React type definitions (`react.d.ts`, `react-dom.d.ts`, `react-global.d.ts`) from DefinitelyTyped
  - [x] 1.2 Create tsconfig.json in src/EditorApp/
    - Set `"jsx": "react"` so TSX compiles to `React.createElement(...)` calls
    - Set `"outDir": "wwwroot/js"` so compiled JS goes directly to the embedded resources directory
    - Set `"rootDir": "src"` to point at the TSX source directory
    - Set `"module": "none"` — no module bundler, scripts load via `<script>` tags
    - Set `"target": "es2020"` for modern browser compatibility
    - Set `"strict": true` for type safety
    - Include React type definitions from `scripts/`
  - [x] 1.3 Add standalone React and ReactDOM JS files to wwwroot/js/
    - Download React 19 UMD development builds from unpkg.com or cdnjs
    - Save as `react.js` and `react-dom.js` in `wwwroot/js/`
    - These expose `React` and `ReactDOM` as globals when loaded via `<script>` tags
  - [x] 1.4 Update .csproj to compile TypeScript before build
    - Replace the `BuildReactApp` target with a `CompileTypeScript` target
    - Command: `node scripts/tsc.js -p tsconfig.json`
    - Must run `BeforeTargets="BeforeBuild"`
    - Remove the npm ci / npm run build / copy commands

- [x] 2. Rewrite frontend components as tsc-compatible TSX
  - [x] 2.1 Create src/EditorApp/src/InteropService.ts
    - Port the InteropService from `frontend/src/services/interopService.ts`
    - Remove all ES module imports/exports — use global `window` assignments instead
    - Use `window.external.sendMessage` for sending and `window.external.receiveMessage` for receiving
    - Expose `createInteropService` on `window` for use by other scripts
    - Keep the 5-second timeout logic
  - [x] 2.2 Create src/EditorApp/src/StatusBar.tsx
    - Port from `frontend/src/components/StatusBar.tsx`
    - Use `React.createElement` (tsc handles JSX → React.createElement with `"jsx": "react"`)
    - Export `formatFileSize` function on `window` for reuse
    - Expose the StatusBar component on `window`
  - [x] 2.3 Create src/EditorApp/src/TitleBar.tsx
    - Port from `frontend/src/components/TitleBar.tsx`
    - Expose the TitleBar component on `window`
  - [x] 2.4 Create src/EditorApp/src/ContentArea.tsx
    - Port from `frontend/src/components/ContentArea.tsx`
    - Expose the ContentArea component on `window`
  - [x] 2.5 Create src/EditorApp/src/App.tsx
    - Port from `frontend/src/App.tsx`
    - Use a single InteropService instance (from `window.createInteropService()`)
    - Handle Ctrl+O / Cmd+O keyboard shortcut
    - Manage state: fileContent, isLoading, error, warning, titleBarText
    - Render TitleBar, warning banner, ContentArea, StatusBar
    - Mount the app using `ReactDOM.createRoot(document.getElementById('root')).render(...)`
    - This file should be the entry point that bootstraps the React app

- [x] 3. Update wwwroot/index.html and CSS
  - Load scripts in correct order: react.js → react-dom.js → InteropService.js → StatusBar.js → TitleBar.js → ContentArea.js → App.js
  - Remove the old `assets/index.js` and `assets/index.css` references
  - Create a `wwwroot/css/app.css` with all component styles (or inline in index.html)
  - Keep `_framework/blazor.webview.js` for Photino.Blazor
  - Keep `<div id="app"></div>` for Blazor and `<div id="root"></div>` for React

- [x] 4. Update .csproj and clean up
  - [x] 4.1 Update EditorApp.csproj
    - Replace `BuildReactApp` target with `CompileTypeScript` target
    - Ensure `<Content Remove="wwwroot\**" />` and `<EmbeddedResource Include="wwwroot\**" />` still present
    - Ensure `GenerateEmbeddedFilesManifest=true` and `StaticWebAssetsEnabled=false` still present
    - Remove any npm-related targets or references
  - [x] 4.2 Remove the frontend/ directory
    - Delete `frontend/` entirely (package.json, node_modules, vite.config.ts, etc.)
    - Update .gitignore to remove frontend-specific entries
  - [x] 4.3 Remove old wwwroot/assets/ directory
    - Delete `wwwroot/assets/index.js` and `wwwroot/assets/index.css`
    - The new JS files are in `wwwroot/js/`
  - [x] 4.4 Update build.sh and run.sh
    - Remove npm-related steps from build.sh
    - build.sh should just run `dotnet publish` (tsc runs automatically via .csproj target)
    - run.sh stays the same (dotnet run)

- [x] 5. Verify the migration
  - Run `dotnet build` and verify TypeScript compiles without errors
  - Run `dotnet run` and verify the app launches with the React UI
  - Test Ctrl+O opens a single file dialog
  - Test selecting a file loads and displays content correctly
  - Test title bar, status bar, and content area all render properly
  - Test error handling (try opening a non-existent file path)
  - Verify no npm/node_modules artifacts remain

## Notes

- TSX files can still use JSX syntax — `tsc` with `"jsx": "react"` compiles `<div>` to `React.createElement("div")`
- CSS can be inline styles in the components or a single CSS file loaded in index.html
- The only external dependency is Node.js runtime (for running tsc.js)
- Download sources: TypeScript from unpkg.com/typescript, React from unpkg.com/react, type definitions from DefinitelyTyped GitHub
