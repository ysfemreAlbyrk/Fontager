<div align="center">
<p align="center">
  <img width="15%" src="https://raw.githubusercontent.com/ysfemreAlbyrk/Fontager/refs/heads/main/Fontager.Viewer/Assets/Logo.png">
</p>

# Fontager

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![WinUI](https://img.shields.io/badge/UI-WinUI%203-0078D4.svg)](https://learn.microsoft.com/en-us/windows/apps/winui/)

*A modern, powerful font manager and viewer application for Windows, built with WinUI 3*

Fontager replaces the outdated Windows `fontview.exe` with a fast, beautiful font previewer and aims to provide a full-featured font management suite for designers and developers.
<img width="1920" height="1080" alt="img-1" src="https://github.com/user-attachments/assets/62713c1d-c14f-4a51-a8f1-a7b231b8f95c" />

[🚀 Download](#building) • [📖 Features](#features) • [🛠️ Tech Stack](#tech-stack) • [🤝 Contributing](#contributing)

</div>

## 📖 Features

### 🎯 Fontager Viewer

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

<img width="1920" height="1080" alt="img-2" src="https://github.com/user-attachments/assets/dd25198e-7339-4e79-a679-adad2ab8d116" />
<img width="1920" height="1080" alt="img-3" src="https://github.com/user-attachments/assets/217856aa-6c5b-448b-ba8d-ae7a273e8c3c" />

### 🚧 Fontager Manager *(planned)*

A professional-grade font management suite with library organization, temporary activation, collections, and Google Fonts integration.

### 🔧 Fontager.Core

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

**Visual Studio (MSIX):**
1. Set configuration to **Release** and platform to **x64**
2. Right-click `Fontager.Viewer` → **Package and Publish** → **Create App Packages**
3. Select **Sideloading** → **Next**
4. Create or select a certificate → **Create**

📁 **Output:** `Fontager.Viewer\AppPackages\Fontager.Viewer_X.X.X.X_x64.msix`  
Double-click the `.msix` file to install, or distribute it to other users.

**Download From GitHub Release:**
1. Go to [latest release page](https://github.com/ysfemreAlbyrk/Fontager/releases/latest) and download.
2. Run the bat file.

## 🛠️ Tech Stack

| Component | Technology | Description |
|-----------|------------|-------------|
| 🎨 **UI Framework** | WinUI 3 (Windows App SDK) | Modern Windows UI framework |
| 💻 **Language** | C# / .NET 8 | Modern .NET platform |
| 🏗️ **Architecture** | MVVM with `CommunityToolkit.Mvvm` | Model-View-ViewModel pattern |
| 🔌 **DI** | `Microsoft.Extensions.DependencyInjection` | Dependency injection |
| 🔍 **Font Parsing** | Custom binary parser | No external dependencies |
| 📦 **Font Loading** | Win32 GDI (`AddFontResourceEx`) + XAML `ms-appdata` URI caching | Native font handling |

## 🔗 File Association

Fontager.Viewer registers for `.otf`, `.ttc`, and `.woff2` files. After installation, double-click a font file or set Fontager as default in **Settings → Apps → Default apps → Choose default apps by file type**.

### About `.ttf`

Windows reserves the `.ttf` extension for the built-in Font Viewer and the MSIX schema rejects it inside `Package.appxmanifest`, so Fontager cannot claim it the way it claims the other formats. Two workarounds:

1. **Manual "Open with..."** — right-click any `.ttf` file → *Open with* → *Choose another app* → pick Fontager Viewer and tick *Always use this app*. Works for any build (MSIX or portable).
2. **Settings → Install → "Register .ttf for current user"** — *(portable build only)*. Adds Fontager to the per-user `OpenWithProgids` list so it shows up in the *Open with...* menu without hunting for the executable. No admin needed, never claims the default handler.

A deeper write-up of the limitation lives in [`docs/research/font-parsing.md`](docs/research/font-parsing.md#6-ttf-file-association-limitation-on-windows-appendix).

## 📄 License

This project is free and open-source software (FOSS). See [LICENSE](LICENSE) for details.

## 🤝 Contributing

Contributions are welcome! Please open an issue to discuss proposed changes before submitting a pull request.

---

<div align="center">

**⭐ Star this repository if it helped you!**

Made with ❤️ by [Yusuf Emre Albayrak](https://github.com/ysfemreAlbyrk)

</div>
