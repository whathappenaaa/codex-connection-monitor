# Codex Connection Monitor

A Windows desktop companion showing recent Codex connection evidence, a colored tray summary, and shared account quota.

Author: **B站那年松江** · MIT · [中文](README.md)

[Download portable release](https://github.com/whathappenaaa/codex-connection-monitor/releases/latest)

Extract the ZIP and run `CodexConnectionMonitor.exe`. No Python, Node.js or API key is needed. The target platform is Windows 10/11 x64 with .NET Framework 4.6+ and the Windows SQLite component. Windows 11 build 26200 and Codex CLI 0.158.0-alpha.2.1 were tested; Windows 10 has not been independently verified.

Close hides to the tray; double-click restores; right-click offers check, executable selection and exit. Monitoring continues while hidden. Three window sizes, automatic height fitting, pinning and instant Chinese/English switching are included.

## Tray colors

Priority: red → amber → green → blue → gray.

- **Red ×:** no local network reported by Windows, or a recent explicit interruption/retry in the selected task.
- **Amber !:** a fresh endpoint transport failure, or an earlier task failure without confirmed recovery.
- **Green pulse:** recent model output or connection success.
- **Blue check:** the turn completed normally.
- **Gray dash:** insufficient evidence, unreadable logs or an unidentified task.

HTTP 401/403 are endpoint responses, not transport disconnections. Quota depletion and quota refresh errors never alter tray connection colors. The task card remains task-specific, while the tray aggregates network and task evidence. Green is not a continuous-online guarantee; silence is not proof of a disconnect.

## Quota and privacy

Quota uses the official local `codex app-server` account interface and existing Codex sign-in. A short-lived hidden process communicates over stdio; no model turns are started. Quota refreshes every 60 seconds, with a 10-second manual-refresh cooldown. Windows system proxy settings are forwarded only to this child when no explicit proxy environment setting exists. Select `codex.exe` through the tray menu if discovery fails.

Missing values stay unavailable. Failed refreshes retain a clearly marked old snapshot; reset times do not automatically refill a quota. Quotas are shared across tasks, and account changes clear the preceding snapshot. No raw account response or credentials are persisted by this app. Official Codex behavior remains subject to its own settings.

Connection evidence and task names are read locally from Codex SQLite databases and desktop logs. Two unauthenticated HEAD probes access Microsoft and ChatGPT public endpoints every 15 seconds. There is no custom backend, chat upload, or application telemetry. The endpoints still receive ordinary connection metadata. Settings are stored beside the executable. Classified error history and quota snapshots remain in memory.

Version 1.3.1 checks both `%LOCALAPPDATA%\Codex\Logs` and the explicit `%LOCALAPPDATA%\Packages\OpenAI.Codex_*\LocalCache\Local\Codex\Logs` cache. This fixes Store installations that leave an empty ordinary-path placeholder when the monitor is launched independently from Explorer. Missing current-task evidence still stays unknown instead of borrowing another task's status.

This is an independent, unofficial tool. Local Codex log formats can change. It does not repair network issues. The release is unsigned and does not include an installer or updater.

## Development

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\test.ps1 -IncludeUi
.\scripts\release.ps1
```

The Windows .NET Framework C# compiler is used, without NuGet dependencies. Tests and releases go to `artifacts`. Screenshots in documentation use synthetic data. `--usage-check <path>` writes only quota data; `--diagnose <path>` can include task names/IDs and must be redacted before sharing. Never upload raw logs, credentials or private chats to Issues.

[MIT license](LICENSE) · [Contributing](CONTRIBUTING.md) · [Video materials (Chinese)](docs/video/README.md)
