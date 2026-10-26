# AppFlow

AppFlow is a small Windows 10/11 WPF utility for launching a sequence of applications when a selected app starts. Profiles and application paths stay on this PC in `%LOCALAPPDATA%\AppFlow\data.json`.

## Run the published app

For a machine without .NET installed, use the `Setup.exe` installer from a GitHub Release. The installer sets up AppFlow and its update service. The app checks for stable releases at startup and can also check from **Settings → App updates**. When an update is ready, choose **Restart to update** in Settings or right-click the tray icon and choose **Restart to install update**. The updater is configured by the release workflow for the repository it runs in.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
dotnet build AppFlow.csproj -c Release
dotnet publish AppFlow.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
```

Velopack packages the published folder, so keep the files together; a single-file publish is not used for the installer build.

## GitHub installer and updates

The workflow at `.github/workflows/release.yml` builds a self-contained Windows x64 package, creates `Setup.exe`, and publishes the installer and update feed as a GitHub Release. A public repository lets installed copies check and download updates without embedding a secret token. Private repositories need an authenticated update host; do not put a personal GitHub token in the app.

To publish:

1. Create or use a GitHub repository and push this project to it, including the `.github/workflows/release.yml` file.
2. In GitHub, open **Settings → Actions → General** and make sure workflows have permission to create releases (the workflow requests `contents: write`).
3. Create and push a version tag from the project folder. For example:

   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```

4. Open the repository's **Actions** tab and wait for **Build and publish AppFlow** to finish. The workflow publishes a release containing `Setup.exe` and the update files.
5. Share the `Setup.exe` link from that GitHub Release. For future releases, push a new tag such as `v1.1.0`; users can update from AppFlow.

Use increasing semantic versions for tags (`v1.0.1`, `v1.1.0`, `v2.0.0`). The release must be published, not left as a draft, for the updater to see it. GitHub-hosted public release downloads have API rate limits, so the app checks on startup and users can also check manually.

## Create your first profile

1. Open **Applications** and wait for Start Menu and installed app discovery, or choose **Add application** to select an executable or shortcut.
2. Open **Profiles** and choose **New profile**.
3. Name the profile, choose the trigger app, add companion apps from the searchable picker, and use **Move up** and **Move down** to set launch order.
4. Set the delay and optional graceful close requests, then save and leave the profile enabled.

AppFlow uses Windows process start/stop event notifications when the current account can subscribe to them. If Windows denies that subscription, it checks only the configured trigger executable names once per second, so normal user accounts can still monitor without administrator access. The optional main-window trigger waits for a window from the started process, checking only that candidate process. AppFlow suppresses additional processes for the same executable while its first trigger process remains alive, which avoids retriggering for apps such as browsers. Already-running companions are skipped by default. Profile-launched companions are closed only through a graceful window-close request, and only when the user selected them; no process is force-terminated.

For isolated testing, set the `APPFLOW_DATA_DIR` environment variable to a temporary directory before launching AppFlow. It stores its JSON data there instead of the current user's normal AppFlow folder.

The included `Smoke.ps1` checks ordered launches with a same-named headless helper, single-instance activation, tray behavior, and idle use with temporary test data. `Idle.ps1` measures CPU and working set after startup and cleans up its temporary data when finished. On this development machine, the self-contained build used 150 MB working set and 0.00 CPU seconds over a 10-second idle sample after startup. Measurements vary by Windows version and installed applications.

Minimizing or closing the window always sends AppFlow to the tray. Right-click the tray icon to open the window, pause or resume automation, or choose **Exit AppFlow** to stop it completely. Launching AppFlow again opens the existing window instead of starting another monitor. Companion apps are skipped as already open when the configured executable has a visible window or a hidden titled top-level window commonly retained by tray apps; same-named helper processes from another path and headless services no longer block their launch. Profile data remains local; Windows process monitoring does not require administrator privileges in normal user sessions. The app discovers installed apps through Start Menu shortcuts and installed-app registry entries; some packaged apps or apps that hide their executable may need to be added manually.
