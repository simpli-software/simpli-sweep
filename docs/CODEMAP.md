# Code map

## Layout
- `src/Cleaner.Core/` — engine, no UI. Shared by the CLI, the app and the tests.
  - `CleanRule.cs` — rule/target records (loaded from JSON).
  - `Rules/*.json` — built-in rules, embedded in the assembly.
  - `RuleLoader.cs` — loads embedded rules; `Validate` rejects unsafe rule files.
  - `PathTokens.cs` — the fixed token set rules may use (%TEMP%, %LOCALAPPDATA%, …).
  - `SafetyGuard.cs` — protected folders, allowed exceptions, root checks. Every path goes through it.
  - `Scanner.cs` — read-only scan; expands `*` profile folders; never follows junctions/symlinks.
  - `CleanRunner.cs` — deletes scanned files after re-checking each one (guard, timestamp, junctions).
  - `RecycleBin.cs` — `IRecycleBin` + shell implementation.
  - `Reports.cs` — scan/clean results. Clean reports hold counts and bytes only, never paths.
  - `SafeDelete.cs` — handle-based delete (checks the opened object's real path) + process hardening.
  - `Startup/` — startup manager: lists Run keys and Startup folders; on/off via the Explorer
    `StartupApproved` flag (same as Task Manager), never edits or deletes entries.
    `FileTrust.cs` reads publisher + Authenticode status (embedded, then catalog; offline) and resolves .lnk targets.
  - `Settings/` — `ScanOptions` (minimum age, excluded folders/patterns; only ever stricter than the
    rules, re-checked at delete time) and `SettingsStore` (settings.json in %LOCALAPPDATA%\<Product>,
    atomic save, damaged file → defaults).
  - `Jobs/CleanJob.cs` — unattended scan + clean for the CLI: presets (safe = defaults minus Recycle Bin,
    selected = app ticks), saved settings applied, refuses while settings need review.
  - `DiskUsage/DiskUsageScanner.cs` — read-only folder-size tree + top-N largest files; parallel to depth 4;
    skips junctions and cloud-only placeholders.
- `src/Cleaner.App/` — WPF app (built-in Fluent theme, MVVM, no UI library).
  - `App.xaml.cs` — composition root; product name read from the `<Product>` build setting.
  - `ViewModels/MainViewModel.cs` — scan, selection, confirm-then-clean, restart as admin.
  - `ViewModels/RuleItemViewModel.cs` — one category row; only rows with something to clean can be ticked.
  - `Services/Services.cs` — dialogs and elevation behind interfaces (faked in tests).
  - `ViewModels/StartupViewModel.cs` — Startup tab: list, on/off switches, admin-only items locked.
  - `ViewModels/SettingsViewModel.cs` — Settings tab; remembers Clean-tab ticks; option changes clear the scan.
  - `ViewModels/DiskUsageViewModel.cs` — Disk tab: drive picker, drill-down folder list, largest files, Show in Explorer.
- `src/Cleaner.Cli/` — `cleaner scan`, `cleaner clean [--preset safe|selected] [--yes] [--json]`, `cleaner disk <path>`.
  Exit codes: 0 done, 1 error, 2 bad arguments, 3 settings need checking, 4 no settings / running as SYSTEM.
  A dry run with damaged settings still exits 0 but warns (and `settingsNeedReview` in `--json`).
- `tests/Cleaner.Core.Tests/` — `FakeMachine` builds a fake Windows tree per test.
  - `SafetyTests.cs` — guard, junction, locking and timing tests.
  - `BuiltInRuleTests.cs` — one `Rule_<id>` test per built-in rule (enforced by a test).
- `tests/Cleaner.App.Tests/` — view-model tests (confirmation, selection, admin flow, startup switches) on `FakeMachine` / `FakeRegistry`.
- `build/release.ps1` — self-contained single-file app + CLI, portable zip, installer, SHA256SUMS.txt → `artifacts/release/`.
- `installer/SimpliSweep.iss` — Inno Setup script; names and version are passed in by `release.ps1`.
- `.github/workflows/ci.yml` — the gate on `windows-latest` + gitleaks (full history).
  `release.yml` — a `v*` tag builds the files and makes a **draft** release; PRs touching packaging build only.
  Every `uses:` is pinned by commit SHA; downloaded tools (gitleaks, Inno Setup) by SHA-256.

## Adding a rule
Add it to a `Rules/*.json` file, then add `Rule_<id-with-underscores>` to `BuiltInRuleTests.cs`
proving it removes its targets and keeps the files next to them.

## Gate
```
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
```

## Releasing
1. Bump `<Version>` in `Directory.Build.props`, merge.
2. Tag `vX.Y.Z` on `main` and push the tag (needs Dale's OK). The tag must match `<Version>`.
3. Check the draft release's files, then publish it by hand.
