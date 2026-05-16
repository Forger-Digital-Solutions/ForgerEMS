# Kyra.Sdk reference (ForgerEMS.Kyra.HostAdapter only)

## Restore modes

| Mode | When | How |
|------|------|-----|
| **Project reference (default)** | Sibling `Kyra_Assistant` tree exists | Auto: `UseKyraSdkProjectReference=true` |
| **Package (CI)** | No sibling source; EMS CI | `/p:UseKyraSdkProjectReference=false` |

## Package feed (CI)

Build the Kyra SDK feed in the Kyra repo:

```powershell
# From Kyra_Assistant/repo
tools/Kyra-Sdk-Release.ps1
```

Feed path: `Kyra_Assistant/repo/release/sdk-current/feed/`  
Packages: `Kyra.Contracts`, `Kyra.Workers.Core`, `Kyra.Local.Core`, `Kyra.Combined.Core`, `Kyra.Sdk` (same version).

ForgerEMS `nuget.config` maps `kyra-sdk-local` → that `feed/` folder.

```powershell
# From ForgerEMS_App/repo
dotnet restore ForgerEMS.sln -p:UseKyraSdkProjectReference=false
dotnet build src/ForgerEMS.Kyra.HostAdapter/ForgerEMS.Kyra.HostAdapter.csproj -c Release -p:UseKyraSdkProjectReference=false
```

CI / artifact (no sibling tree):

```powershell
$env:KYRA_SDK_FEED_PATH = 'D:\artifacts\kyra-sdk-feed-1.1.4\feed'
.\tools\Validate-KyraSdkPackageFeed.ps1 -Configuration Release
# or: -KyraSdkFeedPath $env:KYRA_SDK_FEED_PATH
```

See `docs/KYRA_SDK_EMS_ADAPTER_PLAN.md` and Kyra `docs/KYRA_SDK_CI_ARTIFACTS.md`.

## Rules

Do not add `Kyra.Sdk` to `ForgerEMS.Wpf` or reference `Kyra.Core` for the SDK path.
