# osu! Background Replacer

**osu! Background Replacer** is a modern Windows 11 / WinUI 3 desktop application built on .NET 8 that allows you to safely and rapidly replace background images across all beatmaps in your osu! Songs library in a single batch operation.

---

## Features

- **Batch Background Replacement**: Scans all beatmap sets and updates background images across thousands of beatmaps in seconds.
- **Accurate osu! Beatmap Parser**: Reads `.osu` beatmap script files, extracting exact background filenames from `[Events]` sections while ignoring video and storyboard assets.
- **Drag & Drop Integration**: Drag and drop your osu! Songs folder or target image directly into the app with real-time file type validation and active visual states.
- **Automatic Folder Detection**: One-click detection scans default osu! installation locations across drives.
- **Non-Destructive Backups & Undo**: Safely archive original backgrounds before replacement and restore them with 1-click Undo.
- **Detailed Activity Logging**: Real-time event log with success/failure statuses and export to file.
- **Modern Windows 11 UI**: Fluent 2 design language with Mica material, dark/light theme support, and responsive layouts.

---

## Usage

1. **Select osu! Songs Directory**:
   - Click **Auto-detect** to find your osu! Songs directory automatically.
   - Or click **Browse...** / drag & drop your osu! `Songs` folder into the drop zone.
   - An indicator displays the total number of beatmap folders discovered.
2. **Choose Replacement Image**:
   - Click **Browse Image...** or drag & drop a `.jpg`, `.jpeg`, or `.png` file.
   - The preview card shows the image dimensions, file size, and thumbnail.
3. **Configure Backup (Recommended)**:
   - Toggle **Backup original backgrounds** to save originals before replacing.
4. **Run Replacement**:
   - Click **Replace Backgrounds**.
   - Review the confirmation dialog. If the chosen directory is outside the standard `osu!\Songs` path, a unified warning dialog appears allowing you to verify or proceed with custom installations.
   - Progress bar and status indicators update live during execution.
5. **View Results or Undo**:
   - Check the **Activity Log** for detailed per-file replacement outcomes.
   - Click **Undo last replacement** at any time to restore original backgrounds from the most recent backup session.

---

## Backup & Undo Behavior

- **Session-Based Backups**: Every replacement run with backups enabled creates a timestamped session under the `Backups/` directory (e.g. `2026-10-14_17-00-00/`).
- **Integrity Verification**: Each backup session includes a `manifest.json` indexing original file paths, backup copies, and SHA-256 cryptographic hashes.
- **Safe Restoration**: The **Undo** function verifies file integrity and restores original images to their respective beatmap folders. Missing files or modified beatmaps are reported gracefully without aborting the restore.
- **Session Retention Management**: Configure how many backup sessions to keep in **Settings** (default: 5 sessions; set to 0 for unlimited). Older sessions are automatically purged after successful runs to manage disk space.

---

## Settings & Backup Locations

All user configurations and backup files reside under a single unified root directory:

```text
%LocalAppData%\OsuBackgroundReplacer\
├── settings.json       # User preferences, saved paths, theme, and retention limits
└── Backups\            # Timestamped backup sessions and file manifests
    └── YYYY-MM-DD_HH-mm-ss\
        ├── manifest.json
        └── ...
```

### Automatic Migration
On first launch, the application checks for legacy data previously stored under `%LocalAppData%\yakimi27\OsuBackgroundReplacer\`. Any existing backup sessions and settings are migrated to `%LocalAppData%\OsuBackgroundReplacer\` automatically without data loss.

---

## Build Requirements

- **Operating System**: Windows 10 version 1809 (Build 17763) or higher, or Windows 11.
- **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`net8.0-windows10.0.19041.0`).
- **Platform Package**: Windows App SDK 1.8 (`Microsoft.WindowsAppSDK`).
- **IDE**: Visual Studio 2022 (v17.8 or higher) with:
  - *.NET Desktop Development* workload
  - *Windows App SDK C# Templates*
- **Command Line Build**:
  ```powershell
  # Build application (x64)
  dotnet build OsuBackgroundReplacerMain\OsuBackgroundReplacerMain.csproj -p:Platform=x64

  # Run test suite
  dotnet test
  ```

---

## Trimming Note

- `PublishTrimmed` is enabled by default for **Release** configurations (`Condition="'$(Configuration)' != 'Debug'"`) in `OsuBackgroundReplacerMain.csproj`. Trimming removes unused framework code, significantly reducing the packaged application size and startup time.
- Trimming is explicitly **disabled** for **Debug** configurations to maintain maximum build speed and full runtime diagnostic fidelity during development.
