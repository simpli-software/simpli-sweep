# Simpli Sweep — Scope

Status: draft, 2026-10-23. Name: Simpli Sweep (decided 2026-10-24); it lives in build config, not in code.

## 1. Goal

A free, open-source Windows 10/11 cleaner that frees disk space and tidies startup **safely**, with no ads, no upsell, no telemetry, and no scare tactics. Runs locally as a portable exe; published on GitHub with signed releases.

## 2. What already exists (GitHub, checked 2026-10-23)

| Project | Stars | License | Stack | What to borrow |
|---|---|---|---|---|
| [ChrisTitusTech/winutil](https://github.com/ChrisTitusTech/winutil) | 64.9k | MIT | PowerShell + WPF | Tabbed UI, one-click presets, community-friendly repo layout |
| [Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat) | 60.6k | MIT | PowerShell | Reversible tweaks, clear "what this does" per option |
| [qarmin/czkawka](https://github.com/qarmin/czkawka) | 34.5k | MIT (mixed) | Rust | Duplicate / large / empty file finders |
| [BCUninstaller/Bulk-Crap-Uninstaller](https://github.com/BCUninstaller/Bulk-Crap-Uninstaller) | 22.0k | Apache-2.0 | C# | Uninstaller + leftover detection |
| [hellzerg/optimizer](https://github.com/hellzerg/optimizer) | 18.2k | GPL-3.0 | C# | **Archived** Jan 2026 — gap in the market for a maintained C# tool |
| [farag2/Sophia-Script](https://github.com/farag2/Sophia-Script-for-Windows) | 9.8k | MIT | PowerShell | Well-documented tweak catalog |
| [bleachbit/bleachbit](https://github.com/bleachbit/bleachbit) | 7.2k | GPL-3.0 | Python/GTK | **Closest model**: data-driven cleaner definitions (CleanerML), preview-before-delete, CLI |
| [MoscaDotTo/Winapp2](https://github.com/MoscaDotTo/Winapp2) | 1.1k | CC-BY-SA-4.0 | data file | ~thousands of community cleaning rules (`winapp2.ini`) |

**Model it after:** BleachBit's engine design (rules as data, preview first) + winutil's modern UI and repo style. Nobody ships a maintained, native, signed, BleachBit-quality cleaner with a good Windows UI — that's the niche.

**Licensing rule:** write our own code under MIT. Do not copy code from GPL projects (BleachBit, Optimizer) — read for ideas only. `winapp2.ini` may be bundled as a separate data file with CC-BY-SA attribution (ShareAlike applies to that file only).

## 3. Recommended stack

- **C# / .NET 10 (LTS) + WPF**, published as a self-contained single-file exe (~60–80 MB; trimmed smaller).
  - Native Windows APIs (Recycle Bin, Shell, Task Scheduler, restore points) are easy from C#.
  - Fewer antivirus false positives than PowerShell scripts.
- Alternatives considered:
  - *Tauri + Rust*: tiny binary, but Windows-API work is slower to write.
  - *PowerShell + WPF (winutil-style)*: no build step, but AV-flag prone and harder to test.
- Optional UI library: WPF-UI (MIT) for a Windows 11 Fluent look.

## 4. MVP features (v1.0)

### Cleaner (core)
- Scan → **preview** (per-category size + file count) → user ticks categories → clean.
- Default categories:
  - User and system temp folders
  - Recycle Bin
  - Thumbnail and icon caches
  - Windows Update download cache, Delivery Optimization cache
  - Crash dumps, Windows Error Reporting files, old logs
  - Browser **caches** for Edge, Chrome, Firefox and Brave (cookies, history and saved sessions are opt-in, off by default)
  - Common app caches (Discord, Teams, Spotify, Steam shader cache, …) via rule files
- Rules live in JSON files (our own format). `winapp2.ini` import comes in v1.1.
- Skip locked or in-use files silently; report the count of skipped files.

### Startup manager
- List Run/RunOnce registry keys, Startup folders, and scheduled tasks that run at logon.
- **Disable / enable only** (no delete in v1); show publisher and signature status.

### Disk usage
- "Largest folders / files" view for a chosen drive.
- Open the selected item in Explorer.

### CLI
- `cleaner.exe --preset safe --dry-run` / `--clean` for Task Scheduler automation.
- Exit codes and a JSON summary output.

## 5. v1.x / v2 candidates

- v1.1: `winapp2.ini` import (advanced toggle, attribution)
- Uninstaller with leftover scan (study BCU)
- Duplicate file finder (study czkawka)
- Advanced: DISM component store cleanup, `Windows.old` removal, hibernation file toggle
- Scheduled clean from the UI
- Privacy tweaks (telemetry, ads in Start) — only reversible ones, each with an "undo"
- Localisation

## 6. Explicitly out of scope (and why)

- **Registry cleaner** — no measurable benefit, real breakage risk; Microsoft does not support it.
- RAM "boosters", driver updaters, "PC health score" — snake oil / scareware patterns.
- Prefetch cleaning — slows boot, a myth.
- Disabling Defender, Windows Update, or SmartScreen.
- Telemetry, accounts, ads, bundled offers, auto-updates that install without asking.

## 7. Safety requirements (non-negotiable)

1. Preview / dry run is the default; nothing is deleted without an explicit click.
2. Never follow symlinks or junctions; never touch paths outside the rule's allow-list.
3. Hard-coded deny-list: `C:\Windows\System32`, `Program Files`, user Documents/Desktop/Pictures, OneDrive, etc.
4. Create a System Restore point before any system-level action (admin tasks).
5. Run as a normal user; elevate (UAC) only for categories that need it.
6. User exclusions list (folders / file patterns).
7. Local log records categories, counts and bytes only — **no file names or paths** containing user names.
8. Every rule has a test proving it targets only what it claims.

## 8. Architecture (sketch)

```
src/
  Cleaner.Core/       rule engine, scanners, deleters, safety guards (no UI); built-in rules in Rules/*.json
  Cleaner.App/        WPF UI (MVVM)
  Cleaner.Cli/        command-line front end over Core
tests/
  Cleaner.Core.Tests/ rule tests run against a fake file tree
```
Core is UI-free so the CLI, UI and tests all share it.

## 9. Distribution

- **GitHub Releases** built by GitHub Actions from tags: portable `.zip` + installer (Inno Setup).
- **Code signing:** unsigned exes trigger SmartScreen and AV warnings. Free options: [SignPath Foundation](https://signpath.org) (free for OSS); paid fallback: Azure Trusted Signing (~$10/mo).
- Publish to **winget**; later Scoop / Chocolatey.
- Submit each release to Microsoft Defender's false-positive portal if flagged.
- Repo extras: README with screenshots, LICENSE (MIT), CONTRIBUTING, SECURITY.md, issue templates, "How to write a rule" doc.

## 10. Milestones (rough, one developer + Claude)

| # | Milestone | Size |
|---|---|---|
| M1 | Core engine + 14 built-in rules + safety guards + tests — **done 2026-10-24** (47 tests passing) | 1–2 weeks |
| M2 | WPF UI: scan / preview / clean — **done 2026-10-24** | 1 week |
| M3 | Startup manager (**done 2026-10-24**) + disk usage view | 1 week |
| M4 | CLI, settings/exclusions | 3–5 days |
| M5 | CI, signing, installer, README, v1.0 release | 3–5 days |

## 11. Open decisions

1. ~~Stack~~ — **Decided 2026-10-24: C# / .NET 10 + WPF.**
2. ~~Name~~ — **Decided 2026-10-24: Simpli Sweep** (repo `simpli-software/simpli-sweep`).
3. ~~License~~ — **Decided 2026-10-24: MIT.**
4. ~~winapp2~~ — **Decided 2026-10-24: not in v1; import ships in v1.1 behind an "advanced" toggle with CC-BY-SA attribution.**
