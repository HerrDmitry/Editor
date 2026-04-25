# Build and Packaging

## Overview
Configure build settings and create packaging scripts for cross-platform single-file executable deployment.

## Tasks

- [x] 14. Configure build and packaging
  - [x] 14.1 Configure .NET publish settings
    - Set `PublishSingleFile=true` in .csproj
    - Set `SelfContained=true` in .csproj
    - Set `RuntimeIdentifier` for target platforms (win-x64, osx-x64, linux-x64)
    - Configure embedded resources to include React bundle
    - _Requirements: 1.3, 1.4_

  - [x] 14.2 Configure React build for embedding
    - Configure Vite to output bundle to C# wwwroot directory
    - Set base path for embedded resources
    - Optimize bundle size (minification, tree-shaking)
    - _Requirements: 1.3_

  - [x] 14.3 Create build scripts for cross-platform packaging
    - Create script to build React bundle
    - Create script to publish C# application for Windows, macOS, Linux
    - Create script to package executables
    - Test build output on each platform
    - _Requirements: 1.3, 1.4_

- [x] 15. Final checkpoint - Build and test complete application
  - Build single-file executable for all platforms
  - Test application launch within 3 seconds (Requirement 1.1)
  - Test all features end-to-end on each platform
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 1.1, 1.3, 1.4_
