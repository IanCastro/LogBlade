# RESULT

## Artifact

- `artifacts/publish/LogViewer.exe`

## Memory Measurement

- Smoke snapshot on a running sample-log session:
- Working set: `27.31 MB`
- Private bytes: `6.16 MB`
- The status bar also shows live working set and private bytes in the app itself.

## PASS / FAIL

- Total process memory < 10 MB: FAIL
- The measured working set was above 10 MB during smoke testing.

## NativeAOT

- NativeAOT blocker: offline restore cannot resolve `Microsoft.DotNet.ILCompiler` and related packs from `nuget.org` in this environment.
- The build script falls back to a runnable self-contained direct-Win32 publish and still produces `artifacts/publish/LogViewer.exe`.

## Validation

- Build: PASS
- Smoke test: PASS
