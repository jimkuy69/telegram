# TDLib Windows x64 Native Build Gate

This repository does not commit `tdjson.dll` until the binary has been built from the official TDLib source, reviewed, and verified with the repository hash gate.

## Official build basis

TDLib's official build guidance uses CMake and builds the JSON target with `cmake --build . --target tdjson`. TDLib supports MSVC on Windows and the JSON interface is the intended C# P/Invoke boundary.

Source: https://github.com/tdlib/td

## Required build environment

- Windows x64
- Visual Studio with C++ workload
- CMake
- Git
- vcpkg
- OpenSSL, zlib, and gperf for x64

## Build outline

Run from a disposable build directory and pin the TDLib source to a reviewed commit or release tag:

```powershell
git clone --recursive https://github.com/tdlib/td.git td-src
git -C td-src checkout <reviewed-tdlib-ref>
git -C td-src submodule update --init --recursive
git clone https://github.com/microsoft/vcpkg.git vcpkg
.\vcpkg\bootstrap-vcpkg.bat
.\vcpkg\vcpkg.exe install gperf:x64-windows openssl:x64-windows zlib:x64-windows
New-Item -ItemType Directory -Force td-src\build | Out-Null
Set-Location td-src\build
cmake -A x64 -DCMAKE_BUILD_TYPE=Release -DCMAKE_TOOLCHAIN_FILE=..\..\vcpkg\scripts\buildsystems\vcpkg.cmake ..
cmake --build . --config Release --target tdjson
```

The exact output path is generator-dependent. Locate the resulting `tdjson.dll`, copy it to `tdlib\tdjson.dll`, and do not overwrite an existing reviewed binary.

## Verification and manifest

Record the source URL, exact reviewed ref, TDLib version, Boost license, `win-x64` architecture, and SHA-256 in `tdlib\tdjson.manifest.json`. Then run:

```powershell
pwsh ./scripts/verify-tdlib-native.ps1
```

The verifier must report `PASS`. A missing binary, missing manifest, architecture mismatch, or hash mismatch remains `BLOCKED`.

## Release rule

Do not enable real Telegram authentication or package the native dependency until the verifier passes and a manual test-account acceptance record exists. Never put API hashes, OTPs, passwords, or session data in this repository.