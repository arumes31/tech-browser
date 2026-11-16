# Tech Browser

Tech Browser is a portable Chromium browser designed for the restricted Backstage desktop in ConnectWise Control (ScreenConnect). It renders Chromium offscreen and copies each frame into a standard WinForms/GDI surface, avoiding black or incomplete browser windows caused by GPU, DWM, and DirectComposition capture limitations.

The project is intended for technicians who need to open current web applications, device interfaces, and administration portals without disturbing the user's primary desktop session.

## Current implementation

The primary implementation is `native/TechBrowser.Gdi`:

- CefSharp OffScreen with a current Chromium engine
- software rendering with GPU and DirectComposition disabled
- WinForms/GDI presentation compatible with Backstage capture
- address bar, back, forward, reload, and explicit navigation button
- HTTP handling for localhost, local IP addresses and single-word host names such as `nas` or `printer01:8080`
- links that ask for a new window open in the same window (a form that posts data to a new window arrives without its data)
- Ctrl+L or F6 moves the keyboard focus to the address bar
- prompts for HTTP logins and for untrusted certificates
- pages rendered at the Windows display scale
- no persistent browser profile, cookies, passwords, or history
- application and CEF diagnostic logs, started fresh on every launch and limited to scheme and host of visited addresses
- one portable, self-contained x64 exe: no installer, no script launcher, no archive tool
- app-local Visual C++ 2022 runtime deployment

An earlier Electron prototype was removed; its normal Chromium compositor was not consistently visible through Backstage capture. It remains in the git history.

## Repository layout

```text
native/TechBrowser.Gdi/   CefSharp OffScreen and WinForms/GDI browser
scripts/                  publish and verification scripts
```

## Requirements

- Windows x64
- .NET 8 SDK
- Visual C++ 2022 x64 runtime on the build machine (copied into the exe)

The build uses the system `dotnet` command. A private SDK installation at `.tools/dotnet/dotnet.exe` is also detected automatically.

## Build the exe

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-toolbox-gdi.ps1
```

The result is a single `Tech-Browser-<version>-x64-gdi.exe` in `dist`, published as a .NET single-file app. No .NET or Visual C++ installation is required on the endpoint.

Chromium can only load its DLLs and resource files from disk, so on first start the .NET host inside the exe unpacks them to `%TEMP%\.net\Tech Browser\<hash>` (for SYSTEM: `C:\Windows\SystemTemp\.net`) and reuses that folder on later starts. No script or separate archive tool is involved, and Chromium's renderer and utility processes run as further instances of the same exe. On start, the browser removes folders left there by earlier releases.

To keep the exe small, the build removes parts the browser never loads: Chromium UI languages other than English and German, the WebGPU and SwiftShader libraries (the GPU is disabled, so WebGL and WebGPU pages do not work), and the WPF, debugger and satellite-resource files of the .NET runtime. These rules live in `native/TechBrowser.Gdi/TechBrowser.Gdi.csproj`.

Verify the finished package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-toolbox-gdi.ps1
```

The verification workflow copies the exe into an empty folder and starts it with an isolated extraction folder. The smoke test clicks a link that asks for a new window and passes only when the second page is rendered with visible content, which covers mouse input, popup handling and painting. It also checks that the exe unpacked the Visual C++ runtime and `libcef.dll`, that the log is written beside the exe,, and runs the address-bar self-test built into the executable (`--self-test`).

## Continuous integration and releases

- **CI** (`.github/workflows/ci.yml`) builds, smoke-tests and uploads the exe. It runs on every branch push in the repository that owns the branch, and on every pull request against the merge result.
- **Release** (`.github/workflows/release.yml`) runs on every push to `main` and publishes the exe as a GitHub release. Versioning is automatic and works like the noxa repo: `VERSION` names the release line, the workflow takes the next unused patch after the existing `v*` tags, and nothing is committed back. Re-running a released commit keeps its version. Pushing a `v*` tag by hand releases exactly that version. To start a new minor or major line, raise `VERSION`. Builds outside a release are stamped `<VERSION>-dev+<commit>` (`scripts/version.ps1`).
- **CodeQL**, **dependency review** and **Dependabot** cover code scanning and dependency updates. Actions are pinned to commit hashes; Dependabot keeps them current. In a fork, Dependabot version updates must be switched on under Insights → Dependency graph → Dependabot, otherwise no Chromium (CefSharp) updates arrive.

The workflows contain no repository-specific names or secrets, so they behave the same in a fork and in the upstream repository; each repository keeps its own `v*` tags and releases. Two repository settings matter: Actions must be enabled, and dependency review needs the dependency graph (off by default in forks, under Settings → Advanced Security). GitHub may hold the first pull-request run from a new contributor until a maintainer approves it.

## ScreenConnect deployment

1. Add the generated `.exe` file to the ConnectWise Control toolbox.
2. Start it from a Backstage session.
3. On first launch of a new version, allow a few seconds for the exe to unpack Chromium.
4. Test a public HTTPS site and the intended internal device or administration interface.
5. Review the diagnostic files if startup or navigation fails.

Diagnostic files are created beside the exe:

- `Tech-Browser.log`
- `Tech-Browser-cef.log`

## Security notes

Backstage applications commonly run with elevated privileges. Use this browser only for trusted vendor, customer, and device interfaces. Chromium certificate validation and sandboxing remain enabled; a page with an untrusted certificate opens only after the technician confirms a warning. The application does not alter whether ConnectWise displays a connection indicator; that behavior is controlled by the ConnectWise configuration and connection method.

## Status

The project is under active field testing with ConnectWise Control Backstage. Build outputs, local SDKs, and published exe files are intentionally excluded from version control.
