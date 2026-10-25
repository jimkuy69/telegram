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

- Provenance/hash gate: PASS for TDLib 1.8.67, `win-x64`.
- Managed test suite: PASS, 142 tests locally.
- Native runtime gate: PASS locally for the checked-in TDLib package; the no-login POC loads `tdjson.dll` and executes `getOption(version)`.
- CI and clean-machine verification remain separate gates; live Telegram authentication is still not tested.
