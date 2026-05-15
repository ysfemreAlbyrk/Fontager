# Packaging decision — why Fontager viewer is unpackaged

**Where we landed:** Fontager.Viewer ships as an **unpackaged** WinUI 3 app with a **self-contained Windows App SDK runtime** (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true` in the csproj). MSIX manifest and tooling stay in the repo but flipped off so flipping back is mostly property changes — I didn’t want to burn bridges with the Store.

**Why I’m writing this:** Packaging sounds like a boring build detail until it isn’t. For a font tool, the difference between “real HKCU” and “virtualised HKCU” is literally whether Windows Settings shows **Uninstall** for a font you just installed. That one UX glitch pushed me hard toward unpackaged for day-to-day builds.

---

## 1. The two shapes, in plain language

| | **Packaged (MSIX)** | **Unpackaged** |
|---|---|---|
| Identity | Strong package identity (`Publisher` + `Name` + `Version`) | None — it’s just an exe in a folder |
| Install path | Under `WindowsApps\…` | Wherever you unzip or your installer puts it |
| File-system access | Sandboxed unless `runFullTrust` | Normal Win32 |
| HKCU writes | **Redirected into the package container** | **The real user hive** |
| HKLM writes | Admin, real HKLM | Same |
| File associations | Declared in manifest | Our code writes HKCU at runtime (where allowed) |
| `.ttf` in manifest | **Blocked by schema** (reserved) | We can still offer “Open with…” via HKCU |
| `Package.Current` | Works | Throws unless caught — we guard icon/version/packaged checks |
| Mica / Acrylic | Works | Works |
| Store listing | Possible | Not with this shape alone |
| Download size | Smaller package | Larger folder (runtime bundled) |

---

## 2. Fontager-specific: what actually matters

| User-facing thing | Packaged | Unpackaged | Notes |
|---|---|---|---|
| Open `.otf` / `.ttc` / `.woff2` from Explorer | ✅ | ✅ | Both fine. |
| Open `.ttf` from Explorer | ❌ reserved in manifest | ✅ HKCU “Open with…” | Big practical win for unpackaged. |
| Install font for current user | File lands correctly either way | Same | |
| **Uninstall in Settings → Fonts** | ❌ often **Hide** only | ✅ **Uninstall** works | HKCU virtualisation breaks the link packaged-side. |
| Install for all users | ✅ needs admin | ✅ needs admin | Same mechanics. |
| Drag-drop / elevated picker | Same UIPI story | Same | We fixed the awkward bits in code either way. |
| Settings file | Would lean on WinRT storage bridges | `%LocalAppData%\Fontager\settings.json` | I chose JSON on disk so moving the exe doesn’t scatter behaviour across bridges — see `SettingsService`. |
| Font preview URI (`FontFamily`) | Works | Works via WinAppSDK identity helpers | |
| Clean uninstall from Settings → Apps | ✅ | ❌ unless we ship an installer | Trade-off I accept for now. |

Rough takeaway: **six** important behaviours lean unpackaged or neutral; **two** lean packaged (Store polish, Apps uninstall). We weren’t chasing Store yet, so unpackaged won.

---

## 3. The HKCU virtualisation trap (the detail)

Under MSIX, writes you think go to `HKCU\Software\…` can land in a **private package hive**. Settings → Fonts builds its list from the **real** user registry plus the font folder.

What went wrong in practice:

- Font file appeared under `%LocalAppData%\Microsoft\Windows\Fonts`.
- Registry entry for that install lived in the **virtual** hive.
- Settings decided the font wasn’t really “user-owned” and only offered **Hide**, not **Uninstall**.

There isn’t an elegant in-package fix — either we ship our own uninstall UX for every font, or we stop pretending HKCU is shared. I picked the second for the default build.

---

## 4. The `.ttf` manifest wall

`Package.appxmanifest` file-type associations **cannot** claim `.ttf` — Windows reserves it for the bundled font viewer story. Validation fails if you try.

Unpackaged builds sidestep that: we register **ProgIDs** under HKCU so Fontager shows up under **Open with…**. We never fight Windows for *default* `.ttf` ownership; we just want to be choosable.

*(Same HKCU caveat: under MSIX identity those writes don’t help the real shell.)*

---

## 5. What we give up (honestly)

1. **No Store pipeline** until we flip tooling back on — fine for now.
2. **Fatter folder** — self-contained WinAppSDK costs tens of MB. Release builds can use trimming (`PublishTrimmed` when self-contained); **`PublishReadyToRun` stays false** in the csproj today — I valued predictable builds over cold-start micro-optimisations last time I touched it.
3. **No built-in auto-update** — Velopack/Squirrel territory later.
4. **No “Uninstall app” from Settings** without an installer — users delete the folder; optional Inno/MSI later.
5. **`Package.Current` throws** when unpackaged — every caller handles it (version string, icons, “are we packaged?” for file assoc).

---

## 6. What we gain

1. Settings → Fonts **Uninstall** behaves like users expect for per-user installs.
2. `.ttf` “Open with…” is achievable.
3. Settings live in a **predictable JSON path** — same mental model dev vs user machine.
4. CI / local builds stay **`dotnet publish`** shaped instead of msbuild package choreography.
5. Debugging — registry and files are where `regedit` and Explorer say they are.

---

## 7. If we go MSIX / Store again

Rough checklist (don’t treat this as gospel without re-reading the csproj):

1. **`Fontager.Viewer.csproj`** — flip `WindowsPackageType`, enable `EnableMsixTooling`, reconsider `WindowsAppSDKSelfContained` (Store builds often share the framework runtime).
2. **`Package.appxmanifest`** — bump version; confirm `.otf`/`.ttc`/`.woff2`; accept `.ttf` **still** can’t be declared.
3. **Own the HKCU gap** — either document “Hide only” again or build **Uninstall font** inside the app.
4. **`SettingsService`** — JSON store keeps working; no migration drama.
5. **`FileAssociationService`** — packaged mode disables the `.ttf` toggle and relies on manifest entries for the formats we’re allowed.

Nothing in the viewer codebase **requires** unpackaged logic everywhere — we branch where reality differs (`IsRunningPackaged`, install verification, etc.).

---

## 8. One-line summary

For a font viewer that cares about **honest per-user installs**, **choosable `.ttf` handling**, and **registry you can trust**, **unpackaged is the default that matches user expectations**. Revisit when Store visibility outweighs those wins.

---

## 9. References

- [Windows App SDK packaging modes](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/)
- [MSIX file-type associations / reserved types](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-extensions#filetype-associations)
- [`Microsoft.Windows.Storage.ApplicationData` spec](https://github.com/microsoft/WindowsAppSDK/blob/main/specs/applicationdata/ApplicationData.md)
- [`font-parsing.md`](./font-parsing.md) — `.ttf` association appendix lines up with this doc.
