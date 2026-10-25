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
- `src/Cleaner.Cli/` — `cleaner scan` dry run, `cleaner disk <path>` sizes (cleaning from the CLI in M4).
- `tests/Cleaner.Core.Tests/` — `FakeMachine` builds a fake Windows tree per test.
  - `SafetyTests.cs` — guard, junction, locking and timing tests.
  - `BuiltInRuleTests.cs` — one `Rule_<id>` test per built-in rule (enforced by a test).
- `tests/Cleaner.App.Tests/` — view-model tests (confirmation, selection, admin flow, startup switches) on `FakeMachine` / `FakeRegistry`.

## Adding a rule
Add it to a `Rules/*.json` file, then add `Rule_<id-with-underscores>` to `BuiltInRuleTests.cs`
proving it removes its targets and keeps the files next to them.

## Gate
```
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
```
