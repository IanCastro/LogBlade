# RESULT

## Distribution status

- `PARTIALLY_PROVED`

## Artifacts

- Official launcher artifact: `artifacts\build\LogBlade.exe`
- Distribution gate artifact: `artifacts\publish\LogBlade.exe`

## Build contract

- Normal build: `.\build.ps1`
- Distribution build: `.\package.ps1`
- The root launcher and the official comparisons use the normal build artifact.
- The distribution build remains a separate packaging gate and does not need to run on every iteration.

## Memory measurement

- Smoke snapshot on a sample log:
  - Working set: `27.31 MB`
  - Private bytes: `6.16 MB`

## PASS / FAIL

- Total process memory < 10 MB: FAIL
- The measured working set was above 10 MB.

## NativeAOT

- The final NativeAOT path is still not fully proved offline in this environment.
- The self-contained distribution path is proved and produces `artifacts\publish\LogBlade.exe`.

## Validation

- Normal build: PASS
- Distribution publish: PASS with self-contained fallback
- Smoke test: PASS
