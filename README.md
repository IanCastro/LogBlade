# C# NativeAOT Win32 Log Viewer MVP

This repo contains a direct Win32 desktop log viewer written in C# and published as a NativeAOT executable when the toolchain allows it.

## What it does

- Opens a real log file from the command line: `LogViewer.exe <path>`
- Builds a sparse startup index with total line count and checkpoints every 4096 lines
- Decodes only requested visible lines on demand
- Keeps a maximum of 300 decoded lines in memory
- Uses raw Win32 + GDI for the window, painting, vertical scrollbar, mouse wheel, and keyboard navigation

## Build

```powershell
./build.ps1
```

The published executable is written to:

`artifacts/publish/LogViewer.exe`

## Run

```powershell
./run.ps1 .\artifacts\sample.log
```

## Encoding rules

- UTF-8 with BOM
- UTF-16 LE/BE with BOM
- UTF-16 without BOM only when the NUL-byte pattern is clear
- Otherwise Windows-1252

## Notes

- No WPF, WinForms, Avalonia, MAUI, or WebView are used.
- The viewer draws directly with GDI through Win32 P/Invoke.
