# Simpli Sweep

A free, open-source Windows 10/11 cleaner. No ads, no upsell, no telemetry.
It shows you exactly what it will remove before it removes anything.

> Status: pre-release (v0.1.0). Builds are not code-signed yet, so Windows SmartScreen warns on first run.
> See the [code signing policy](#code-signing-policy).

## What it cleans (v1)

Temp files, Recycle Bin, thumbnail cache, Windows Update downloads, Delivery Optimization cache,
crash dumps, error reports, DirectX shader cache (off by default), and browser caches for
Edge, Chrome, Brave and Firefox. Cookies, history and saved logins are never touched.

## What it will never do

Registry "cleaning", RAM boosting, driver updates, disabling Defender or Windows Update,
or anything that phones home.

## Install

Download the installer or the portable zip from the latest release. The installer installs for
your account only unless you pick "all users" on its first page; it remembers that choice on
upgrades. Switching between the two leaves two copies, so uninstall the old one first.
To uninstall, use Settings → Apps → Installed apps (the portable zip: delete the folder).
Uninstalling keeps your settings and exclusions.

## Run it from source

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

To build the release files (installer, portable zip, checksums) into `artifacts/release/`:

```
./build/release.ps1 -Iscc "<path to Inno Setup's ISCC.exe>"
```

Without Inno Setup only the portable zip is built.

## Rules

Cleaning rules are JSON files in `src/Cleaner.Core/Rules/`. Every rule must have a test in
`tests/Cleaner.Core.Tests/BuiltInRuleTests.cs` proving it removes only what it claims;
the build fails otherwise. Safety guards (protected folders, no following junctions or symlinks,
re-checking files at delete time) live in `SafetyGuard.cs` and `CleanRunner.cs`.

See `docs/SCOPE.md` for the roadmap.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

Only the installer and executables built by this repository's GitHub Actions release workflow
from a tag on `main` are signed, and every signing request is approved by hand.

| Role | Members |
|---|---|
| Committers and reviewers | [Dale Kissell (@Dalekissell)](https://github.com/Dalekissell) |
| Approvers | [Owners](https://github.com/orgs/simpli-software/people?query=role%3Aowner) |

### Privacy

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it.

## License

MIT
