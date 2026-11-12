# Tech Browser

Tech Browser is a portable Chromium browser designed for the restricted Backstage desktop in ConnectWise Control (ScreenConnect). It renders Chromium offscreen and copies each frame into a standard WinForms/GDI surface, avoiding black or incomplete browser windows caused by GPU, DWM, and DirectComposition capture limitations.

The project is intended for technicians who need to open current web applications, device interfaces, and administration portals without disturbing the user's primary desktop session.

## Current implementation

The primary implementation is `native/TechBrowser.Gdi`:

- CefSharp OffScreen with a current Chromium engine
- software rendering with GPU and DirectComposition disabled
- WinForms/GDI presentation compatible with Backstage capture
- address bar, back, forward, reload, and explicit navigation button
- HTTP handling for localhost and local IP addresses
- no persistent browser profile, cookies, passwords, or history
- application, launcher, and CEF diagnostic logs
- self-contained x64 .NET runtime
- app-local Visual C++ 2022 runtime deployment

The older Electron implementation remains under `src` as a reference prototype. Its normal Chromium compositor was not consistently visible through Backstage capture.

## Repository layout

```text
native/TechBrowser.Gdi/   CefSharp OffScreen and WinForms/GDI browser
scripts/                  publish, package, and verification scripts
toolbox/                  ScreenConnect toolbox launchers
src/                      legacy Electron prototype
test/                     Electron navigation tests
```

## Requirements

- Windows x64
- .NET 8 SDK
- Node.js and pnpm

The build uses the system `dotnet` command. A private SDK installation at `.tools/dotnet/dotnet.exe` is also detected automatically.

## Build the ScreenConnect package

```powershell
pnpm install --frozen-lockfile
pnpm pack:toolbox:gdi
```

The resulting `.scapp` package is written to `dist`. It contains a compressed, self-contained browser payload, so no .NET or Visual C++ installation is required on the endpoint.

To keep the transfer small, the build removes parts the browser never loads: Chromium UI languages other than English and German, the WebGPU and SwiftShader libraries (the GPU is disabled, so WebGL and WebGPU pages do not work), and the WPF, debugger and satellite-resource files of the .NET runtime. The payload is compressed with a 256 MB LZMA2 dictionary, so extraction needs about that much free memory.

Verify the finished package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-toolbox-gdi.ps1
```

The verification workflow extracts the actual `.scapp`, starts it through the toolbox launcher, checks the required Visual C++ runtime files and diagnostic logs, and confirms that Chromium produced a rendered frame.

## Continuous integration and releases

- **CI** (`.github/workflows/ci.yml`) runs the Node tests and builds, smoke-tests and uploads the `.scapp`. It runs on every branch push in the repository that owns the branch, and on every pull request against the merge result.
- **Release** (`.github/workflows/release.yml`) runs on every push to `main` and publishes the `.scapp` as a GitHub release. Versioning is automatic and works like the noxa repo: `VERSION` names the release line, the workflow takes the next unused patch after the existing `v*` tags, and nothing is committed back. Re-running a released commit keeps its version. Pushing a `v*` tag by hand releases exactly that version. To start a new minor or major line, raise `VERSION`. Builds outside a release are stamped `<VERSION>-dev+<commit>` (`scripts/version.ps1`).
- **CodeQL**, **dependency review** and **Dependabot** cover code scanning and dependency updates.

The workflows contain no repository-specific names or secrets, so they behave the same in a fork and in the upstream repository; each repository keeps its own `v*` tags and releases. Two repository settings matter: Actions must be enabled, and dependency review needs the dependency graph (off by default in forks, under Settings → Advanced Security). GitHub may hold the first pull-request run from a new contributor until a maintainer approves it.

## ScreenConnect deployment

1. Add the generated `.scapp` file to the ConnectWise Control toolbox.
2. Start it from a Backstage session.
3. On first launch, allow time for the Chromium payload to extract.
4. Test a public HTTPS site and the intended internal device or administration interface.
5. Review the diagnostic files if startup or navigation fails.

Diagnostic files are created beside the extracted payload:

- `Tech-Browser-launcher.log`
- `Tech-Browser.log`
- `Tech-Browser-cef.log`

## Security notes

Backstage applications commonly run with elevated privileges. Use this browser only for trusted vendor, customer, and device interfaces. Chromium certificate validation and sandboxing remain enabled. The application does not alter whether ConnectWise displays a connection indicator; that behavior is controlled by the ConnectWise configuration and connection method.

## Status

The project is under active field testing with ConnectWise Control Backstage. Build outputs, local SDKs, runtime payloads, and `.scapp` files are intentionally excluded from version control.
