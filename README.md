# Simpli Sweep

A free, open-source Windows 10/11 cleaner. No ads, no upsell, no telemetry.
It shows you exactly what it will remove before it removes anything.

> Status: early development (M1 engine and M2 window done). Not ready for general use.

## What it cleans (v1)

Temp files, Recycle Bin, thumbnail cache, Windows Update downloads, Delivery Optimization cache,
crash dumps, error reports, DirectX shader cache (off by default), and browser caches for
Edge, Chrome, Brave and Firefox. Cookies, history and saved logins are never touched.

## What it will never do

Registry "cleaning", RAM boosting, driver updates, disabling Defender or Windows Update,
or anything that phones home.

## Run it

```
dotnet run --project src/Cleaner.App
```

Scans on start; nothing is deleted until you press Clean and confirm.

## Try the dry run

```
dotnet run --project src/Cleaner.Cli -- scan
```

Lists what would be freed per category. Deletes nothing.

```
dotnet run --project src/Cleaner.Cli -- disk C:\n```

Shows the largest folders and files on a drive. Read-only.

## Clean from the command line or on a schedule

```
cleaner clean                    # dry run: shows what would be freed
cleaner clean --yes              # cleans the default categories
cleaner clean --preset selected --yes   # cleans the categories ticked in the app
cleaner clean --yes --json       # machine-readable summary (sizes and counts only)
```

The Recycle Bin is never emptied from the command line unless you add `--include-recycle-bin`.

Your saved settings (excluded folders and file names, minimum age) always apply. Nothing is
deleted if the settings file was damaged (exit code 3) or if this account has no settings yet
(exit code 4; open the app once as this user, or add `--no-settings`).

**Run the task as your own account**, not SYSTEM: settings are per user, and a SYSTEM run
refuses to clean (exit code 4) rather than ignore your exclusions.

Weekly with Task Scheduler (adjust the path to where you put `cleaner.exe`):

```
schtasks /create /tn "Simpli Sweep weekly clean" /sc weekly /d SUN /st 10:00 /tr "\"C:\Tools\SimpliSweep\cleaner.exe\" clean --yes"
```

## Build and test

Requires the .NET 10 SDK.

```
dotnet test
```

## Rules

Cleaning rules are JSON files in `src/Cleaner.Core/Rules/`. Every rule must have a test in
`tests/Cleaner.Core.Tests/BuiltInRuleTests.cs` proving it removes only what it claims;
the build fails otherwise. Safety guards (protected folders, no following junctions or symlinks,
re-checking files at delete time) live in `SafetyGuard.cs` and `CleanRunner.cs`.

See `docs/SCOPE.md` for the roadmap.

## License

MIT
