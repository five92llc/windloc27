# WinLoc27 (Windows)

Locks the keyboard and mouse system-wide, keeps the PC and screen awake, and shows an unlock
QR code on screen. C# / .NET 8 / WinForms.

## Build

On Windows with the [.NET 8 SDK](https://dotnet.microsoft.com/download):

```
build.bat
```

Output: `dist\WinLoc27.exe` (self-contained, no install). It can also be cross-built from macOS/Linux
with the same `dotnet publish` command (the project sets `EnableWindowsTargeting`).

## Publish, install and auto-update

1. Make this folder its own GitHub repo (public, so the updater can read releases without a token).
   `Repo` in `WinLoc27/Updater.cs` is already set to `five92llc/windloc27`.
2. Push to `main`: the **Build** workflow compiles it on a Windows runner, so you see compile errors
   without needing a local SDK.
3. Release: bump nothing by hand, just tag.
   ```
   git tag v1.0.0 && git push origin v1.0.0
   ```
   The **Release** workflow builds `WinLoc27.exe` with that version number and attaches it, plus a
   `.sha256`, to a GitHub Release.
4. On your Windows PC, download `WinLoc27.exe` from the Release and run it (keep it in a folder you
   own, e.g. Documents, not Program Files). The version shows in the title bar and bottom of the window.
5. Later tags are picked up on start (toggle in Settings) or via **Check for updates**. The app
   downloads the new exe, checks its SHA-256, swaps itself and relaunches. Never while locked.

## Use

1. Run `WinLoc27.exe`. First run opens **Settings**: set a password, record a key combination,
   and/or choose a paired Bluetooth device.
2. Click **Lock now**. After the delay (default 5 s) all keyboard and mouse input is swallowed and the
   lock screen appears (full screen, or a small corner panel so you can keep watching something).
3. Unlock any of these ways:
   - **QR + password**: scan the QR code with your phone (same Wi-Fi), type the password on the
     phone page. The QR token changes on every lock; 5 wrong passwords block attempts for 60 s.
   - **Bluetooth**: the chosen paired device must *connect* after the lock engages. If your phone is
     already connected, disconnect and reconnect it (or walk away and back). Windows only reports a
     phone as connected while a profile (audio, tethering...) is active.
   - **Secret key combination**: hold the recorded keys at once (3 or more).

While the app runs it also prevents sleep and screen-off (toggle in Settings).

## Limits and safety

- **Ctrl+Alt+Del and Win+L cannot be blocked** by any normal app. Task Manager can still be opened from
  Ctrl+Alt+Del, so this stops accidents, kids, pets and passers-by, not a determined user.
- The phone page is plain HTTP on your LAN, so the password is sent unencrypted on that network.
  Use a password you do not reuse elsewhere. Windows Firewall will ask to allow the app on first lock.
- Lock never engages unless at least one unlock method is actually working, and Settings has an
  optional **auto-unlock after N minutes** safety net.
- Settings are in `%APPDATA%\WinLoc27\settings.json`. Password and key combo are stored salted/hashed.

## Status

Written on a Mac without a .NET SDK, so it has **not been compiled or run yet**. Expect to fix a few
compile errors on the first `build.bat`.
