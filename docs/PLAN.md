# Learning Lab (Sauce Edition): the plan

Decided with Andrew on 2026-11-10: App Hub's learning apps become one app, **Learning Lab (Sauce Edition)** ("Learning
Lab SE" for short), on SauceStore for **Windows and Android**. The apps move over **one at a time**, and each ships on
both Windows and Android before the next one starts. Once an app's Learning Lab version is released, it is **retired
from App Hub** (the Media Player way: `$RetiredApps` in App Hub's `Install-AppHub.ps1`; App Hub's saved data stays and
Learning Lab reads it on its first start).

## Why Avalonia

The apps' simulators and generators are about 120,000 lines of C# (Cert Lab ~90k, Network Lab ~21k, Emulator Lab and
Learning Games ~5k each). Only the windows are WPF and PowerShell. Avalonia 12 (MIT, WPF-like XAML and controls) runs
the same C# on Windows and Android, so the engines are shared as they are and the screens are written once. A Kotlin
rewrite (App Hub Mobile's way) would mean 120k lines twice.

## Order

| # | App | Engine (C#) | Screens to write | Phone notes |
|---|---|---|---|---|
| 1 | **Learning Games** | ~3.5k lines, pure logic (problems, long division, rewards plans, family network) | the PowerShell UI (LearningGames.ps1, Subjects.ps1, MathPlus.ps1, ~2.7k lines), pictures (`Visuals.cs`), the Workshop (`LongDivisionView.cs`) | made for touch already (big buttons, number pad) |
| 2 | **Emulator Lab** (moved 2026-11-10, not released yet) | ~4k lines (RISC-V machine, assembler, LabOS) | EmulatorLab.ps1 (~700 lines), terminal, editor, inspector | soft keyboard + a key strip (Ctrl, Esc, arrows, F keys) |
| 3 | **Network Lab** (engine 2026-11-13, main screens 2026-11-18; physical view, drawings, desktops next) | ~15k lines of simulator | the canvas, CLI, device windows, floor plans, drawings (WPF, ~6k lines) | pinch zoom, long-press for right-click menus, device windows as full pages |
| 4 | **Cert Lab + Cyber Range** (engine, courses, range and checks 2026-11-19; screens next) | ~71k lines of simulators (`src\LearningLab.Engine\CertLab`, groups `certlab` / `certlab-full`) | ~20k lines of WPF pages, terminals, GRUB, vim, the MD-102 / AZ-800 consoles, PBQs | the biggest port; terminals as in Emulator Lab; tablets first |

## Rules carried over from App Hub

- Plain words for families; the same kids' safety and copyright rules each app had (they are in each app's section of
  `CLAUDE.md`).
- **Saved data persists** until the person deletes it; every format change loads all older versions; never overwrite
  a save that failed to load (`Engine\Saves\SafeFile.cs`, App Hub's helper ported).
- Windows: the first start reads App Hub's data for that app (`%APPDATA%\LearningGames\profiles.json`, ...), leaving it
  where it is.
- Simulated only: nothing in Cert Lab, Cyber Range or Network Lab reaches a real network or machine.
- Every engine check that App Hub ran (`-Test`) runs in `tests\LearningLab.Tests`.

## Releases

The GitHub Actions `Release` workflow (as Bored Games, since 0.3.0): checks, Windows zip (self-contained win-x64), Android
APK signed with Learning Lab's key from the Actions cache, GitHub release with both, `saucestore.json` naming both
assets. Never release unless Andrew asks.
