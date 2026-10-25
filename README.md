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
