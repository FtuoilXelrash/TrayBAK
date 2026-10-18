# TrayBAK

Version v0.0.22

A small Windows tray app that backs up folders to zip files on a schedule.

Author: Ftuoil Xelrash

TrayBAK sits in the system tray, runs your backup jobs when they are due, keeps the number of backups you ask for, and tells you if one fails. It is a single portable program: no installer, no service, no server.

## Features

- Up to 10 backup jobs. Each job has its own name, source folder, backup folder, schedule, retention, alerts, file selection and excludes.
- Backups are zip files.
- Schedules: manual, every X minutes, hourly, daily at a time, or weekly on chosen days at a time. New jobs default to daily at 12:00.
- Missed backups run when the app or PC is back, per job.
- Failed scheduled backups are retried. On or off, the number of retries and the minutes between tries are set in Options.
- Retention per job: keep X days and keep the newest X backups.
- Choose exactly which files and folders to back up with a check box tree. Anything unticked is skipped. Files added later are included.
- Exclude list per job by name or pattern.
- Local alerts only: popup and sound, per job for success and failure.
- Tray icon with a status badge (idle, running, error, paused), a tooltip with last and next run, a click popup with job status and Run buttons, and a right-click menu with Run backup now, Job status, pause, log, options, help and exit.
- Main window with job list, log pane, add, edit, delete, run now, stop and open backups.
- Log viewer with filters, Options, About, and a built-in Help page.
- Dark, light or follow the Windows theme.
- Single instance. Starting a second copy brings the first one forward.

## Requirements

- Windows 10 or 11, 64-bit.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). The runtime is not bundled with the app.

## Getting started

1. Build or download the app (see [Build and publish](#build-and-publish)) and put the folder anywhere you like.
2. Run `TrayBAK.exe`. The icon appears in the tray.
3. Choose **Add**, pick the source folder and the backup folder, set a schedule, and save.
4. Press **F1** in the app for the full Help.

## Backup files

- Zip names carry a date and time stamp and no version: `JobName_MM-dd-yyyy_HH-mm-ss.zip`.
- Zips are stored in a sub folder named after the job, inside the job's backup folder.
- Each job has one status log, `JobName.log`, in the root of the job's backup folder. It has one line per run.

For a backup folder of `D:\Backups` and a job called `Documents`:

```text
D:\Backups\Documents.log
D:\Backups\Documents\Documents_10-17-2026_12-00-00.zip
D:\Backups\Documents\Documents_10-18-2026_12-00-01.zip
```

## Portable

- Settings live in a `config` folder next to the exe (`config\settings.json`).
- The program log lives in a `logs` folder next to the exe (`logs\traybak.log`). It is plain text, one line per event. Unexpected errors are also written to `logs\errors.log`.
- Nothing is written to AppData. The optional start with Windows setting records the exe location in the current user Run key.
- Options and the View menu have an Open logs folder command.
- The app checks at start that `config` and `logs` can be written and says so if not.

## Build and publish

- Target: .NET 10, win-x64, WPF with WinForms for the tray. Solution: `source\TrayBAK.sln`.
- Publish profile: `source\TrayBAK\Properties\PublishProfiles\Portable-win-x64.pubxml`.

```powershell
dotnet publish source\TrayBAK\TrayBAK.csproj -p:PublishProfile=Portable-win-x64
```

- The output is 5 small files, about 340 KB: `TrayBAK.exe`, `TrayBAK.dll`, `TrayBAK.deps.json`, `TrayBAK.runtimeconfig.json` and `TrayBAK-Help.html`. `config` and `logs` are created next to the exe on first run.
- Close any running `TrayBAK.exe` before rebuilding, because a running copy locks the output files.

## Project layout

- `source`: all source code. `TrayBAK.sln` and the TrayBAK project.
- `resources`: the icon files in `images` and the Help page in `help`, which ships with the app.
