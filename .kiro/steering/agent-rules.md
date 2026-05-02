# Agent Rules

## Shell Commands

- Never chain multiple commands with `&&`, `||`, or `;` in a single `executeBash` call
- Run each command as a separate tool invocation
- This applies to all bash commands including build, test, and git operations

## .NET Testing

- Never use `dotnet-script` or `dotnet script` commands
- All test/exploration code must go into proper `.cs` files in the test project (`tests/EditorApp.Tests/`)
- `dotnet test` hangs in this environment. Always use `dotnet build` to compile tests, then run the test DLL directly with the xunit console runner or use `timeout 120 dotnet test ...` with a timeout to prevent hanging.
- Preferred approach: `dotnet build tests/EditorApp.Tests` then `dotnet test tests/EditorApp.Tests --filter "..." --no-build` with a timeout of 120 seconds.
