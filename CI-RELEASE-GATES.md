# CI and Release Gates

GitHub Actions runs on pushes and pull requests to `main`:

1. Restore .NET 8 dependencies.
2. Build the solution in Release configuration.
3. Run the full xUnit test suite.
4. Run Gitleaks with read-only repository permission.

The local release preflight also runs the TDLib no-login runtime gate (`getOption(version)`) before packaging.

The CI workflow does not authenticate to Telegram, use OTP/2FA, send messages,
or claim TDLib End-to-End success. Native TDLib remains excluded until its
source, version, architecture, license, and SHA-256 are reviewed.
