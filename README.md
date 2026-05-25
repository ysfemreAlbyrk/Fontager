<img width="1400" height="400" alt="banner" src="https://github.com/user-attachments/assets/7591f81c-d3d0-4f48-bdcc-826c75372041" />


# Fontager

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![WinUI](https://img.shields.io/badge/UI-WinUI%203-0078D4.svg)](https://learn.microsoft.com/en-us/windows/apps/winui/)

*A modern, powerful font manager and viewer application for Windows, built with WinUI 3. Official Website: [ysfemrealbyrk.github.io/Fontager](https://ysfemrealbyrk.github.io/Fontager/)*

Fontager replaces the outdated Windows `fontview.exe` with a fast, beautiful font previewer and aims to provide a full-featured font management suite for designers and developers.

[🌐 Website](https://ysfemrealbyrk.github.io/Fontager/) • [🚀 Download](#building) • [📖 Features](#features) • [🗺️ Roadmap](roadmap.md) • [🛠️ Tech Stack](#tech-stack) • [🤝 Contributing](#contributing)

</div>
<p  align="center">
  <img width="800" alt="img-1" src="https://github.com/ysfemreAlbyrk/Fontager/blob/2bc9b8373e40f728e509be7206471a90f894d8f9/Assets/Latest-1.gif" />
</p>

## 📖 Features

### Fontager Viewer

A lightweight font previewer that can be set as the default handler for font files.

**Core Features:**

- 🔍 **Instant preview** of `.ttf`, `.otf`, `.ttc`, and `.woff2` files
- ✏️ **Editable preview text** with adjustable font size
- 👀 **Quick View** — character set overview at a glance
- 🌊 **Waterfall view** — preview at multiple sizes simultaneously
- 📋 **Glyph grid** — browse every character with Unicode code points
- ℹ️ **Font metadata** — family, designer, license, version, and more
- 📚 **Multi-font support** — navigate fonts within TrueType Collections (`.ttc`)
- 🎯 **Drag & drop** — open fonts by dropping them onto the window
- 💾 **Font installation** — install for current user or all users
- 🎨 **Modern UI** — Mica/Acrylic backdrop, Fluent Design, custom title bar
- ⚙️ **Configurable** — theme, backdrop, preview defaults, and display options

<table>
  <tr>
    <td> 
      <img width="1920" height="1080" alt="img-1" src="https://github.com/user-attachments/assets/62713c1d-c14f-4a51-a8f1-a7b231b8f95c" />
    </td>
    <td>
      <img width="1920" height="1080" alt="img-2" src="https://github.com/user-attachments/assets/dd25198e-7339-4e79-a679-adad2ab8d116" />
    </td>
  </tr>
  <tr>
    <td>
      <img width="1920" height="1080" alt="img-3" src="https://github.com/user-attachments/assets/217856aa-6c5b-448b-ba8d-ae7a273e8c3c" />
    </td>
  </tr>
</table>

### Fontager Manager *(planned)*

A professional-grade font management suite with library organization, temporary activation, collections, and Google Fonts integration.

### Fontager.Core

Shared library containing models, services, and helpers used by both applications:

- 🔬 **FontParser** — binary parser for TTF/OTF/TTC metadata (name, OS/2, head, maxp, fvar tables)
- 📦 **FontService** — font loading, discovery, and format detection
- 🏗️ **Models** — `FontModel`, `FontMetadata`, `FontFormat`, `FontClassification`

## 💻 Requirements

- 🪟 **Windows 10 (19041+) or Windows 11**
- 🟣 **.NET 8**
- 📱 **Windows App SDK 1.8+**

## 🚀 Building

### 📋 Prerequisites

1. **Visual Studio 2022 (17.8+)** with **Windows App SDK** workload installed
2. **.NET 8 SDK** installed

### 🛠️ Build Steps

1. Open `Fontager.sln` in Visual Studio 2022
2. Ensure the **Windows App SDK** workload is installed
3. Set `Fontager.Viewer` as the startup project
4. Build and run (F5)

### 💻 Command Line Build *(Not Recommended for Development)*

If you prefer not to use Visual Studio, you can build the project using the **dotnet CLI**.

<span style="color: #ee6600; background-color: #ffdd99; padding: 2px 4px; border-radius: 3px;">
While you can build the project using the **dotnet CLI**, **Visual Studio 2022 is the recommended way** for development due to its seamless integration with WinUI 3 and Windows App SDK.
</span>

#### Steps
1. Navigate to the project directory:
  ```sh
    cd Fontager
  ```
2. Restore dependencies:
  ```sh
    dotnet restore
  ```
3. Build the project:
  ```sh
  dotnet build Fontager.Viewer -c Debug -f net8.0-windows10.0.19041.0 -r win-x64
  ```
  - `-c Debug` : Uses the Release configuration. (`Debug`,  `Release`)
  - `-f net8.0-windows10.0.19041.0` : Specifies the target framework
  - `-r win-x64` : RuntimeIdentifier (RID), target platform (`win-x64`, `win-x86`, `win-arm64`)
  
4. Output files will be in:
  `Fontager.Viewer\bin\Release\net8.0-windows10.0.19041.0\`

### 📦 Installation

Fontager ships as an **unpackaged WinUI 3 app**: the runtime payload is a folder containing
`Fontager Viewer.exe` and the self-contained Windows App SDK / .NET runtime — typically delivered inside an **installer** (shortcuts + uninstall) rather than as a loose zip.
No MSIX/Store identity on that path, so no separate runtime install on the target machine.

The rationale (and the path to switch back to MSIX/Store later) is documented
in [`docs/research/packaging-decision.md`](docs/research/packaging-decision.md).

**Visual Studio:**
1. Set configuration to **Release** and platform to **x64**
2. Right-click `Fontager.Viewer` → **Publish** → **Folder**
3. Accept **self-contained** + **win-x64** (see `Properties/PublishProfiles/FolderProfile.pubxml`).
4. Ship the folder **`bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\`** — same output as `dotnet publish`.

Do **not** rely on **`bin\Release\Publish`**. That was an old default `PublishDir` outside the normal SDK folder layout; publishes there often missed native / WinAppSDK files while **`bin\x64\Release\net8.0-windows10.0.19041.0\`** looked fine after a normal **Build** because MSBuild copies the full dependency set during compile.

**Command line:**
```sh
dotnet publish Fontager.Viewer -c Release -r win-x64 --self-contained
```
Output lands in `Fontager.Viewer\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\` when publishing with platform **x64**.
Run `Fontager Viewer.exe` from there directly, or copy the folder anywhere.

**Note:** Always ship the **entire** publish folder — not only the `.exe`. Also unzip fully before running (running from inside the zip often fails).

**Download from GitHub Releases:**
1. Go to the [latest release page](https://github.com/ysfemreAlbyrk/Fontager/releases/latest) and download.
2. Extract the zip and run `Fontager Viewer.exe`.

> **Microsoft Store / MSIX:** not pursued at this stage.

## 🛠️ Tech Stack

| Component | Technology | Description |
|-----------|------------|-------------|
| 🎨 **UI Framework** | WinUI 3 (Windows App SDK) | Modern Windows UI framework |
| 💻 **Language** | C# / .NET 8 | Modern .NET platform |
| 🏗️ **Architecture** | MVVM with `CommunityToolkit.Mvvm` | Model-View-ViewModel pattern |
| 🔌 **DI** | `Microsoft.Extensions.DependencyInjection` | Dependency injection |
| 🔍 **Font Parsing** | Custom binary parser (`name`, `OS/2`, `head`, `maxp`, `fvar`, `cmap`) | No external dependencies |
| 📦 **Font Loading** | Win32 GDI (`AddFontResourceEx`) + XAML `ms-appdata` URI caching | Native font handling |
| 📐 **Packaging** | Unpackaged WinUI 3, self-contained WinAppSDK | See [packaging-decision](docs/research/packaging-decision.md) |

## 🗺️ Roadmap

Completed and planned work (Core, Viewer, Manager) is tracked in **[roadmap.md](roadmap.md)** — separate from release history in [CHANGELOG.md](CHANGELOG.md).

## 📄 License

This project is free and open-source software (FOSS). See [LICENSE](LICENSE) for details.

## 🤝 Contributing

Contributions are welcome! Please open an issue to discuss proposed changes before submitting a pull request.

---

<div align="center">

**⭐ Star this repository if it helped you!**

Made with ❤️ by [Yusuf Emre Albayrak](https://github.com/ysfemreAlbyrk) • [Official Website](https://ysfemrealbyrk.github.io/Fontager/)

</div>
