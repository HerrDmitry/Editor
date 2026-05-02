# Agent Rules

## Shell Commands

- Never chain multiple commands with `&&`, `||`, or `;` in a single `executeBash` call
- Run each command as a separate tool invocation
- This applies to all bash commands including build, test, and git operations
