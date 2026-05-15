# Contributing to Fontager

Thanks for helping improve Fontager. This guide matches how the repo works today: **WinUI 3**, **Visual Studio–first** workflow, and **no automated test project yet** (see [Testing](#testing) and [roadmap.md](../roadmap.md)).

## Before you start

- **Bug or feature?** Open an [issue](https://github.com/ysfemreAlbyrk/Fontager/issues) first for larger changes so we can align on scope.
- **Roadmap:** Planned work lives in [roadmap.md](../roadmap.md). Shipped work is in [CHANGELOG.md](../CHANGELOG.md).
- **License:** Contributions are under the [MIT License](../LICENSE).

## What you need

| Requirement | Notes |
|-------------|--------|
| Windows 10 (19041+) or Windows 11 | WinUI 3 target |
| Visual Studio 2022 (17.8+) | **Recommended** — Windows App SDK workload |
| .NET 8 SDK | Required for build |
| Windows App SDK 1.8+ | See [README](../README.md) |

## Getting the code running

1. Fork and clone the repo.
2. Open `Fontager.sln` in Visual Studio 2022.
3. Set **Fontager.Viewer** as the startup project.
4. Build and run (F5).

Command-line build is possible (`dotnet build` / `dotnet publish`) but **not the primary dev path** for WinUI — use VS when you can. Details: [README → Building](../README.md#-building).

## Where to make changes

| Project | Use for |
|---------|---------|
| **Fontager.Core** | Parsing (`FontParser`, `Woff2Decoder`), models, `FontService`, shared logic that Viewer and Manager should share |
| **Fontager.Viewer** | WinUI UI, `MainWindow`, `SettingsPage`, install UX, file association UI |
| **Fontager.Manager** | Future management app (scaffold only today) |
| **docs/** | Research and design notes |
| **CHANGELOG.md** | User-facing release notes for shipped versions |
| **roadmap.md** | Completed vs planned features (not a substitute for CHANGELOG) |

Prefer putting reusable logic in **Core** instead of copying it in Viewer or Manager.

## Coding guidelines

- Follow normal C# conventions and match existing style in the file you edit.
- Keep public APIs documented with XML comments when you add or change them.
- Avoid drive-by refactors unrelated to your PR.
- WinUI / packaging: see [docs/research/packaging-decision.md](../docs/research/packaging-decision.md) for unpackaged vs MSIX context.

## Testing

**There is no test project in the solution today.** PRs are validated with **manual testing** until we add automated tests (tracked on the roadmap).

### What we expect in every PR

Describe what you ran in the PR template under **Manual verification**. At minimum for Viewer changes:

- [ ] Build succeeds in Visual Studio (Debug, x64).
- [ ] App launches and opens a **`.ttf`** and **`.otf`** file.
- [ ] If you touched parsing / WOFF2 / glyphs: also test **`.ttc`** and **`.woff2`**.
- [ ] If you touched install / Settings → Fonts: install for **current user**, confirm font appears, then **uninstall** if applicable.
- [ ] If you touched UI: attach **screenshots** or a short screen recording.

### Good manual checks (when relevant)

| Area | Suggestions |
|------|-------------|
| **Glyphs** | Large font (CJK/emoji), search by character and `U+XXXX`, block sidebar, copy glyph |
| **Install** | Standard user vs elevated run; success / warning / error dialogs |
| **Settings** | Save settings, reopen app, backdrop unchanged without flash |
| **File association** | Toggle registration; double-click opens font (if you changed association code) |
| **Edge cases** | Corrupt or tiny font file; empty path; cancel file picker |

### Automated tests (future)

We plan a **Fontager.Core** test project (xUnit), golden/fixture fonts for `FontParser` / `Woff2Decoder`, and CI — see [roadmap.md → Testing](../roadmap.md#testing). If you want to start that infrastructure in a PR, discuss it in an issue first.

## Pull requests

1. Branch from `dev` (or the branch the maintainer specifies for your change).
2. Keep commits focused; write clear messages.
3. Fill out [.github/pull_request_template.md](pull_request_template.md) completely.
4. Update **CHANGELOG.md** under `[Unreleased]` (or the appropriate version) for **user-visible** fixes and features.
5. Link issues: `Fixes #123` / `Relates to #456`.

Maintainers may ask for changes or manual re-test before merge.

## Issues

| Type | Template |
|------|----------|
| Bug | [bug_report.md](ISSUE_TEMPLATE/bug_report.md) — steps, Windows version, font format, screenshots |
| Feature | [feature_request.md](ISSUE_TEMPLATE/feature_request.md) — problem, proposed solution |
| Question | [question.md](ISSUE_TEMPLATE/question.md) |

Do **not** attach proprietary fonts unless you have rights to share them; describe the font or use a freely licensed sample.

## Conduct

Be respectful and constructive. Disagree on technical merits, not people.

## Questions

Open a [Question issue](https://github.com/ysfemreAlbyrk/Fontager/issues/new/choose) or comment on an existing discussion.
