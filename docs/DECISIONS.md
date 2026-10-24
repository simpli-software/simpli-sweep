# Decisions

Deviations from `simpli-contracts/STACK.md` and other notable choices. STACK.md targets web
services; this is a local Windows desktop app with no backend, tenants, database or money.

## 2026-10-24: C# / .NET 10 + WPF instead of the standard Python/TypeScript stack
- **Why:** native Windows APIs (Recycle Bin, shell, Task Scheduler, restore points) are direct from
  C#; a self-contained exe needs no runtime; fewer antivirus false positives than script-based tools.
- **Approved by:** Dale, 2026-10-24.
- **Gates instead of ruff/mypy/pytest:** `dotnet build` with warnings as errors (incl. nullable and
  analyzers), `dotnet test` (xUnit), `dotnet format --verify-no-changes`; gitleaks and Dependabot
  still apply. Postgres, RLS, tenancy and isolation tests do not apply (no server, no data store).

## 2026-10-24: MIT licence
- **Why:** free to give away and reuse with the fewest restrictions; matches winutil / Win11Debloat.
- **Approved by:** Dale, 2026-10-24. Code from GPL projects (BleachBit, Optimizer) is never copied.

## 2026-10-24: winapp2.ini import deferred to v1.1
- **Why:** v1 ships only our own rules, each with a test proving it removes only what it claims.
  winapp2 (CC-BY-SA-4.0) arrives behind an "advanced" toggle with attribution.
- **Approved by:** Dale, 2026-10-24.

## 2026-10-24: Placeholder product name
- Display name comes from `<Product>` in `Directory.Build.props`, never from code. Real name to be
  chosen before v1.0 / before publishing to github.com/simpli-software.

## 2026-10-24: No restore point for cache cleaning
- **Why:** SCOPE.md §7 asks for a restore point before system-level actions. Restore points do not
  cover temp/cache files, take minutes, and Windows allows one per 24 h, so they add delay without
  protecting anything here. They will be created before v2 system changes (DISM cleanup, tweaks).
- **Needs:** Dale's OK.

## 2026-10-24: Built-in WPF Fluent theme instead of WPF-UI
- **Why:** .NET 10 WPF ships a Windows 11 theme (`ThemeMode="System"`, light/dark aware); no extra
  dependency. It is flagged experimental (WPF0001, suppressed in the app project).
