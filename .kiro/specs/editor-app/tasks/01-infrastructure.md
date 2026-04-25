# Infrastructure Setup

## Overview
Set up the project structure, dependencies, and build configuration for the cross-platform desktop editor application.

## Tasks

- [ ] 1. Set up project structure and infrastructure
  - Create C# .NET 10 solution with Photino.Blazor project
  - Create React/TypeScript frontend project with Vite
  - Configure single-file publishing (`PublishSingleFile=true`, `SelfContained=true`)
  - Set up embedded resource configuration for React bundle
  - Configure build pipeline to bundle React app into C# resources
  - Install dependencies: Photino.NET, Photino.Blazor, FsCheck (for property tests), xUnit
  - Install frontend dependencies: React 18+, TypeScript 5+, Vitest, fast-check
  - _Requirements: 1.3, 1.4_
