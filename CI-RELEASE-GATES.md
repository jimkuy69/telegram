# CI and Release Gates

GitHub Actions runs on pushes and pull requests to `main`:

1. Restore .NET 8 dependencies.
2. Build the solution in Release configuration.
3. Run the full xUnit test suite.
4. Run Gitleaks with read-only repository permission.

The local release preflight runs the TDLib no-login runtime gate (`getOption(version)`) only after a verified Windows x64 TDLib artifact is present.

The native build workflow creates the artifact from a pinned official TDLib source and records hashes in a manifest. The artifact is not committed to `main`; a release must retain evidence of the successful workflow run and artifact hash.

The CI workflow does not authenticate to Telegram, use OTP/2FA, send messages,
or claim TDLib End-to-End success.
