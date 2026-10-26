# AppFlow

AppFlow opens a set of companion apps when you start a chosen trigger app. Your profiles and settings are saved on this PC.

## Install and update

Download and run **Setup.exe** from the [AppFlow GitHub Releases](https://github.com/PiyushKaushik01/AppFlow/releases). The installer includes what AppFlow needs; you do not need to install .NET separately.

AppFlow checks for updates when it starts. You can also check in **Settings → App updates**. When an update is ready, choose **Restart to update** in Settings, or use the tray icon's **Restart to install update** command.

## Create a profile

1. Open **Applications** to find installed apps. If an app is missing, choose **Add application**.
2. Open **Profiles** and choose **New profile**.
3. Give the profile a name and choose the app that will trigger it.
4. Add companion apps and arrange them in the order you want them to open.
5. Set any optional delay or close behavior, then save the profile and make sure it is enabled.

When the trigger app starts, AppFlow opens the selected companion apps in order. Apps that are already open are skipped. Background helper processes alone do not prevent an app from being opened.

## Keep AppFlow running

Minimizing or closing the window hides AppFlow in the system tray so your profiles keep working. Right-click the AppFlow tray icon to open the window, pause or resume automation, install an available update, or choose **Exit AppFlow** to stop it.

Starting AppFlow again while it is already running brings the existing window forward.

## Your data

Profiles and settings stay on this PC in your Windows user account. AppFlow does not require administrator access for normal use.
