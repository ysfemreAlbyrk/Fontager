# Packaging Decision — Unpackaged WinUI 3

> **Decision:** Fontager.Viewer ships as an **unpackaged** WinUI 3 application
> with a **self-contained Windows App SDK runtime**. The MSIX manifest and
> tooling are kept dormant in the repository so the Store distribution channel
> can be re-enabled later as a one-property change.

## 1. The two options, distilled

WinUI 3 apps can be built in two distribution shapes:

| | **Packaged (MSIX)** | **Unpackaged** |
|---|---|---|
| Identity | Strong package identity (`Publisher` + `Name` + `Version`) | None — just an EXE on disk |
| Install path | `C:\Program Files\WindowsApps\<package family>\…` | Anywhere the user puts the folder |
| File-system access | Sandboxed-ish via `runFullTrust` capability | Full Win32 |
| HKCU registry writes | **Virtualized into the package container** | Real HKCU |
| HKLM registry writes | Requires admin, real HKLM | Requires admin, real HKLM |
| File-type associations | Declared in `Package.appxmanifest` | Declared via HKCU registry writes at runtime |
| `.ttf` association | **Forbidden by manifest schema** (reserved by Windows) | Allowed for the current user |
| `Windows.Storage.ApplicationData.Current` | Works natively | Works via the WinAppSDK identity bridge |
| `Microsoft.Windows.Storage.ApplicationData` | Works | Works (designed for unpackaged) |
| Mica / Acrylic backdrop | Works | Works |
| Drag-drop / file-picker under admin | Same UIPI rules either way | Same |
| Install/uninstall UX | Settings → Apps → uninstall | Manual (or installer-provided) |
| Store distribution | Yes | No |
| Auto-update | Store / App Installer | Roll your own (Squirrel/Velopack/etc.) |
| Distribution size | `.msix` ~15-25 MB (excluding runtime) | Folder ~50-100 MB (with self-contained runtime) |
| Code signing | Required for sideloading; cert managed by VS | Optional but recommended (SmartScreen) |

## 2. What the user gets from each path, applied to Fontager

These are the user-visible features of Fontager.Viewer and how each path
serves them.

| User-facing feature | Packaged | Unpackaged | Notes |
|---|---|---|---|
| Open `.otf`/`.ttc`/`.woff2` from Explorer | ✅ (manifest) | ✅ (HKCU writes) | Both work cleanly. |
| Open `.ttf` from Explorer | ❌ (schema blocks `.ttf`) | ✅ (HKCU "Open with…") | Single biggest gap of the packaged path. |
| Install for current user | ✅ (file copied) | ✅ | |
| **Uninstall from Windows Settings → Fonts** | ❌ (shows "Hide" only) | ✅ ("Uninstall" appears) | Caused by HKCU virtualisation under MSIX. |
| Install for all users (admin) | ✅ | ✅ | Identical (writes to HKLM either way). |
| Drag-drop / picker under admin | ✅ (with our UIPI fix) | ✅ (with our UIPI fix) | Same fix applies to both. |
| Settings persistence | ✅ (`Windows.Storage.ApplicationData`) | ✅ (JSON file in `%LocalAppData%\Fontager`) | We switched to JSON so the path is identical across modes. |
| Font preview rendering (`FontFamily` URI) | ✅ (`ms-appdata://`) | ✅ (via WinAppSDK identity bridge) | Works either way for our use case. |
| Alt+Tab / taskbar icon | ✅ | ✅ | |
| Auto-update | ✅ if listed on Store | ❌ unless we add Squirrel/Velopack | Not used today. |
| Clean uninstall via Settings → Apps | ✅ | ❌ (drop the folder) | Packaged wins here, but installer-based unpackaged can match it. |

Six of the eight features that actually matter for a font viewer either
favour unpackaged or are a tie. The two that favour packaged are
auto-update (we don't use it) and clean uninstall (cosmetic).

## 3. The HKCU virtualisation issue in detail

This is the strongest argument against MSIX for Fontager specifically.

When an MSIX-packaged app writes to `HKCU\Software\…`, the registry write is
redirected by the package runtime into a per-package private hive
(`Reg.dat`) inside the package's state folder. Windows Settings reads from
the *real* user hive when populating Settings → Fonts. The result:

- Our `HKCU\…\CurrentVersion\Fonts\<FamilyName (TrueType)>` write lands in
  the package's private hive.
- Settings → Fonts scans `%LocalAppData%\Microsoft\Windows\Fonts` and finds
  the file we copied there.
- Settings cross-references against the real HKCU and finds *no* matching
  entry.
- It treats the font as system-managed and offers only the **Hide** button.
  **Uninstall** is suppressed.

There is no app-side fix while we stay packaged. The only paths out are:

1. Ship our own "Uninstall font" command in Fontager (works but adds a
   per-app UI to do something Windows already does).
2. Stop being packaged.

We took option 2.

## 4. The `.ttf` association issue in detail

`Package.appxmanifest`'s `<uap:FileTypeAssociation>` extension lists each
extension our app claims. The packaging tools reject `.ttf` at manifest
validation time because it appears on Windows' list of reserved file
extensions (alongside `.exe`, `.dll`, `.lnk`, and the OS-bundled Font
Viewer's claim on `.ttf`). This is enforced by the AppxManifest schema
itself, not by MSIX runtime — there is no flag we can flip.

For an unpackaged build the registration is a per-user HKCU write:

```text
HKCU\Software\Classes\.ttf\OpenWithProgids\Fontager.Viewer.ttf
HKCU\Software\Classes\Fontager.Viewer.ttf\…
HKCU\Software\Classes\Applications\Fontager.Viewer.exe\…
```

Windows never lets a non-system app claim *default* for `.ttf`, but the
per-user "Open with…" entry shows up in the Explorer context menu and is
respected if the user picks "Always use this app". This is the same way
many designer tools (FontBase, RightFont, etc.) handle it.

## 5. What we lose by going unpackaged

These are real, accept them with eyes open:

1. **No Store distribution.** Acceptable today — we're not listing on the
   Store yet. The MSIX manifest stays in the repo so re-enabling is
   cheap.
2. **Larger zipped download.** Self-contained adds ~40-60 MB of WinAppSDK
   runtime. Mitigations:
   - `<PublishTrimmed>true</PublishTrimmed>` for Release.
   - `<PublishReadyToRun>true</PublishReadyToRun>` for cold start, at the
     cost of more disk. (We currently keep ReadyToRun off for parity
     with the previous build.)
3. **No auto-update.** Acceptable until v1.x. Adding
   [Velopack](https://github.com/velopack/velopack) or Squirrel later
   takes a small amount of work and integrates cleanly with an
   unpackaged build.
4. **No "Uninstall" button in Settings → Apps.** Users delete the folder
   and (optionally) hit the in-app "Unregister .ttf" toggle. An
   installer (Inno Setup, MSI) is the standard way to fix this; we can
   add one later.
5. **`Package.Current` throws.** Caught everywhere we access it (icon
   resolution, version string, `FileAssociationService.IsRunningPackaged`).

## 6. What we gain

1. **Settings → Fonts shows "Uninstall"** for fonts we install.
2. **`.ttf` "Open with…"** is now possible.
3. **Standard `%LocalAppData%\Fontager\` settings path** (we wrote our own
   JSON store specifically to avoid identity-bridge weirdness).
4. **Simpler CI** — `dotnet publish -c Release -r win-x64 --self-contained` is
   the whole build instead of `msbuild /t:Package /p:Configuration=Release`.
5. **Easier debugging** — registry writes are inspectable in `regedit`,
   files are in `%LocalAppData%\Fontager`. With MSIX both are hidden in
   the package container.

## 7. Switching back to MSIX (when, not if)

If we list on the Store later, here's the recipe:

1. In [`Fontager.Viewer/Fontager.Viewer.csproj`](../../Fontager.Viewer/Fontager.Viewer.csproj):
   - Set `<WindowsPackageType>` to empty (or remove the property).
   - Set `<EnableMsixTooling>true</EnableMsixTooling>`.
   - Re-evaluate `<WindowsAppSDKSelfContained>` — Store distribution
     prefers framework-dependent so multiple apps share the runtime.
2. In [`Fontager.Viewer/Package.appxmanifest`](../../Fontager.Viewer/Package.appxmanifest):
   - Bump the `Version` attribute.
   - Re-confirm `windows.fileTypeAssociation` covers `.otf`, `.ttc`,
     `.woff2`. `.ttf` stays out — that hasn't changed and won't.
3. Accept that **the per-user install path will lose the
   Settings → Fonts "Uninstall" button** under MSIX again. Add our own
   "Uninstall font" action inside the app to make up for it.
4. In `SettingsService`, the JSON store keeps working; no migration
   needed.
5. In `FileAssociationService`, `IsRunningPackaged` will start returning
   `true`, the `.ttf` toggle will disable itself (correctly — MSIX
   schema bans it), and the manifest declarations take over for the
   other three formats.

The intentional design here: nothing in the codebase actively *depends*
on being unpackaged. We just lose a few features when we re-package.

## 8. Decision summary

For a font viewer / manager that wants:
- to be the default handler for `.ttf` (yes),
- to give the user a working "Uninstall" button in Settings → Fonts (yes),
- to read/write the real HKCU when the user expects it to (yes),

**unpackaged is the right shape**. Re-evaluate when the answers to any of
those change, or when Store presence becomes a goal.

## 9. References

- [Windows App SDK packaging modes](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/) (Microsoft Learn)
- [MSIX reserved file types](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-extensions#filetype-associations) (manifest schema reference)
- [`Microsoft.Windows.Storage.ApplicationData` spec](https://github.com/microsoft/WindowsAppSDK/blob/main/specs/applicationdata/ApplicationData.md) (WinAppSDK repo)
- The companion [`docs/research/font-parsing.md`](font-parsing.md) appendix on TTF associations.
