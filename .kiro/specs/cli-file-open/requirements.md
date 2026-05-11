# Requirements: CLI File Open

## Introduction

Enable the EditorApp to accept a file path as a command-line argument. When provided, the app opens that file automatically on startup instead of requiring the user to use the file picker dialog.

## Glossary

- **CLI argument**: A positional command-line argument passed when launching the application (e.g., `./EditorApp /path/to/file.txt`).
- **CWD**: Current working directory from which the application is launched.

---

### Requirement 1: Accept a Single File Path Argument

**User Story:** As a user, I want to launch the editor with a file path argument so that the file opens immediately without manual interaction.

#### Acceptance Criteria

1. WHEN the application is launched with exactly one positional argument that is not a framework flag, THEN the app SHALL treat it as a file path to open.
2. WHEN the application is launched with no positional arguments, THEN the app SHALL start normally with no file open (existing behavior).
3. WHEN the argument is a relative path, THEN the app SHALL resolve it against the current working directory before opening.
4. WHEN the argument is an absolute path, THEN the app SHALL use it as-is.

---

### Requirement 2: Auto-Open File on Startup

**User Story:** As a user, I want the specified file to be opened automatically once the window is ready so that I see content immediately.

#### Acceptance Criteria

1. WHEN a valid file path argument is provided, THEN the app SHALL call the existing `OpenFileByPathAsync` logic after the Photino window is initialized and the message router is listening.
2. The file open SHALL use the same code path as the file picker dialog (scan, index, send metadata to UI).
3. IF the file does not exist, THEN the app SHALL send a `FILE_NOT_FOUND` error to the UI (same as existing behavior for missing files).
4. IF the file cannot be read (permission denied), THEN the app SHALL send a `PERMISSION_DENIED` error to the UI.

---

### Requirement 3: Window Title Reflects Opened File

**User Story:** As a user, I want the window title to show the file name when a file is opened via CLI argument so that I know which file is loaded.

#### Acceptance Criteria

1. WHEN a file is successfully opened via CLI argument, THEN the window title SHALL be updated to include the file name (consistent with file-picker behavior).

---

## Correctness Properties

1. **Path Resolution Correctness**: For any relative path `r` and CWD `c`, the resolved path SHALL equal `Path.GetFullPath(r, c)`.
2. **Argument Isolation**: Framework arguments (e.g., `--urls`, `--environment`) SHALL NOT be treated as file paths.
3. **Startup Equivalence**: Opening a file via CLI argument SHALL produce the same UI state as opening the same file via the file picker dialog.
