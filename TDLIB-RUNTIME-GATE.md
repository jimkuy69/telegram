# TDLib Native Runtime Gate

The TDLib native package is not considered usable when `tdjson.dll` only passes a hash check. Windows must also be able to resolve every native dependency beside the binary.

## Required artifact contents

The artifact must contain:

- `tdjson.dll`
- `tdjson.manifest.json`
- all matching runtime DLLs produced by the pinned vcpkg toolchain, including OpenSSL/Zlib dependencies when the build links them dynamically

The manifest records the TDLib source commit, version, license, architecture, the primary binary hash, and (for new artifacts) every packaged DLL hash.

## Verification commands

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-tdlib-native.ps1
powershell -ExecutionPolicy Bypass -File scripts/verify-tdlib-runtime.ps1
```

`verify-tdlib-native.ps1` proves provenance and file integrity. `verify-tdlib-runtime.ps1` loads the native library and executes the no-login runtime POC (`getOption(version)`). Both gates are required before packaging an installer.

## Current evidence

- Workflow provenance definition: PASS. The pinned TDLib source, version, architecture, license, manifest generation, and SHA-256 verification are defined in `.github/workflows/build-tdlib-native.yml`.
- Current repository tree: the native DLL and manifest are not checked into `main`; they are produced as a GitHub Actions artifact. Artifact availability and hash must be verified from a successful workflow run before each release.
- Managed test suite: local evidence is 148/148 tests, but this is not live Telegram E2E evidence.
- Native runtime gate: NOT VERIFIED from the current source tree alone. It becomes PASS only when the downloaded workflow artifact contains `tdjson.dll` plus its manifest and `scripts/verify-tdlib-runtime.ps1` passes on Windows x64.
- CI, artifact retention, and clean-machine verification remain separate gates; live Telegram authentication is still not tested.
