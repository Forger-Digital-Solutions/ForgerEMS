# Contributing to ForgerEMS

## Development Setup

Prerequisites:

- Windows 10/11
- .NET 8 SDK
- PowerShell 5.1+

Initial setup:

```powershell
dotnet restore .\ForgerEMS.sln
dotnet build .\ForgerEMS.sln -c Release
dotnet test .\ForgerEMS.sln -c Release
```

## Pull Request Guidelines

- Keep changes focused and reviewable.
- Avoid mixing refactors with behavior changes unless required.
- Add or update tests for logic changes.
- Do not commit generated output from `dist/`, `release/`, or `.verify/` unless explicitly intended by release process.

## Validation Before PR

Run these locally:

```powershell
dotnet restore .\ForgerEMS.sln
dotnet build .\ForgerEMS.sln -c Release
dotnet test .\ForgerEMS.sln -c Release
.\backend\Verify-VentoyCore.ps1
```

## Code Style

- Repo style is defined in `.editorconfig`.
- Nullable reference types are enabled.
- .NET analyzers are enabled through `Directory.Build.props`.
