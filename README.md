# Windtranslator

[简体中文 (zh-CN)](#中文) | [English (en-US)](#english)

## 中文

Windtranslator 是一款基于 WinUI 3 的 Windows 桌面翻译工具，支持云端 API、AI 和本地翻译。应用界面默认使用简体中文。

### 功能

- 多种翻译方式：API 翻译、AI 翻译和本地翻译
- 云端服务商：DeepSeek、千问，以及本地 OpenAI 兼容服务（例如 Ollama、LM Studio）
- 预置模型：DeepSeek `v4-flash` / `v4-pro`，千问 `qwen3.7-plus` / `qwen3.7-flash` / `qwen3.7-max`
- API Key 由用户手动输入，可选择保存到 Windows 凭据库
- 支持自定义 AI 提示词和补充要求
- 图片翻译：使用千问 `qwen3.5-ocr` / `v4-flash-vision-exp`模型识别并翻译图片文字
- 本地翻译：支持 OpenAI 兼容接口，也支持 Mozilla Translations（Bergamot）模型
- 语音输入：Windows 语音识别
- 语音输出：Windows 文本转语音
- 设置自动保存到 `%LOCALAPPDATA%\Windtranslator\settings.json`

### 系统要求

- Windows 10 版本 1809（Build 17763）或更高版本
- .NET 8 SDK
- Windows App SDK 2.3.1
- Visual Studio 2022（使用 Visual Studio 构建时）

### 构建应用

在仓库根目录执行：

```powershell
dotnet build Windtranslator.slnx -p:Platform=x64
```

也可以在 Visual Studio 中打开 `Windtranslator.slnx`，选择 `Windtranslator (Unpackaged)` 配置运行。

项目提供 `x86`、`x64` 和 `ARM64` 配置。Mozilla Translations 本地引擎仅支持 `x64` 和 `ARM64`，因此使用本地模型时应选择对应的平台。

### 构建 Mozilla Translations 运行库

`translator-cli.exe` 是 Mozilla Translations/Bergamot 原生推理引擎。运行库会随应用发布，但翻译模型不会打包进应用；模型由用户下载或选择本地目录。

构建运行库还需要：

- CMake，并加入 `PATH`，或通过 `-CMakePath` 指定 `cmake.exe`
- Git for Windows（脚本需要 Git Bash 和递归子模块）
- Visual Studio Installer 中的“使用 C++ 的桌面开发”工作负载
- vcpkg，以及对应平台的静态 OpenBLAS

先安装 OpenBLAS（默认使用 `%USERPROFILE%\vcpkg`；也可以设置 `VCPKG_ROOT`）：

```powershell
vcpkg install openblas:x64-windows-static
vcpkg install openblas:arm64-windows-static
```

从仓库根目录构建 x64 运行库：

```powershell
.\tools\Build-BergamotRuntime.ps1
```

确认 x64 构建成功后，可构建 ARM64 运行库：

```powershell
.\tools\Build-BergamotRuntime.ps1 -Architecture arm64
```

脚本会从 Mozilla Translations 获取源码和所需子模块，编译 `translator-cli.exe`，并将运行库写入 `Runtime\Bergamot\x64` 或 `Runtime\Bergamot\arm64`。项目文件会在构建和发布时自动复制这些目录中的文件。运行库构建不需要 Doxygen。

### 使用 Mozilla Translations 模型

在应用中打开“设置 > 本地翻译”，选择“Mozilla Translations 模型”

联网时，可在“设置 > 离线语言模型”获取 Mozilla 官方模型列表。应用会显示每个语言方向的模型大小，并将下载的模型保存到 `%LOCALAPPDATA%\Windtranslator\OfflineModels`。下载完成后可以直接使用或删除；断网时仍可使用和删除已下载的模型。

### 基本使用

1. 选择翻译模式、源语言和目标语言。
2. 在设置中选择服务商，输入 API Key，并确认模型和接口地址。
3. 点击“翻译”，然后复制译文或使用语音输出。
4. 本地翻译可以使用 `http://localhost:11434/v1` 等 OpenAI 兼容接口，也可以切换到 Mozilla Translations 模型目录。

## English

Windtranslator is a WinUI 3 desktop translation tool for Windows. It supports cloud APIs, AI-assisted translation, and local translation. The application UI currently defaults to Simplified Chinese.

### Features

- Translation modes: API, AI, and local translation
- Cloud providers: DeepSeek, Qwen, and local OpenAI-compatible services such as Ollama and LM Studio
- Preset models: DeepSeek `v4-flash` / `v4-pro`, and Qwen `qwen3.7-plus` / `qwen3.7-flash` / `qwen3.7-max`
- API keys are entered manually and can optionally be stored in Windows Credential Manager
- Custom additional instructions for AI prompts
- Image translation with Qwen `qwen3.5-ocr` Deepseek`v4-flash-vision-exp`
- Local translation through an OpenAI-compatible endpoint or Mozilla Translations (Bergamot) models
- Windows speech recognition for voice input
- Windows text-to-speech for voice output
- Settings are saved automatically to `%LOCALAPPDATA%\Windtranslator\settings.json`

### Requirements

- Windows 10 version 1809 (Build 17763) or later
- .NET 8 SDK
- Windows App SDK 2.3.1
- Visual Studio 2022 when building from Visual Studio

### Build the application

Run this command from the repository root:

```powershell
dotnet build Windtranslator.slnx -p:Platform=x64
```

Alternatively, open `Windtranslator.slnx` in Visual Studio and run the `Windtranslator (Unpackaged)` configuration.

The project provides `x86`, `x64`, and `ARM64` configurations. The Mozilla Translations runtime supports only `x64` and `ARM64`, so use the matching platform when local model translation is required.

### Build the Mozilla Translations runtime

`translator-cli.exe` is the native Mozilla Translations/Bergamot inference engine. The runtime is included in application builds, but translation models are intentionally not bundled; users download them or select an existing model directory.

The runtime build also requires:

- CMake on `PATH`, or a path supplied with `-CMakePath`
- Git for Windows, including Git Bash for recursive submodule checkout
- The Visual Studio Installer “Desktop development with C++” workload
- vcpkg with static OpenBLAS for the target platform

Install OpenBLAS first (the script defaults to `%USERPROFILE%\vcpkg`; set `VCPKG_ROOT` to use another location):

```powershell
vcpkg install openblas:x64-windows-static
vcpkg install openblas:arm64-windows-static
```

Build the x64 runtime from the repository root:

```powershell
.\tools\Build-BergamotRuntime.ps1
```

After the x64 build succeeds, build the ARM64 runtime with:

```powershell
.\tools\Build-BergamotRuntime.ps1 -Architecture arm64
```

The script downloads the Mozilla Translations source and required submodules, builds `translator-cli.exe`, and copies the runtime to `Runtime\Bergamot\x64` or `Runtime\Bergamot\arm64`. The project file includes these directories in build and publish output. Doxygen is not required.

### Use Mozilla Translations models

In the app, open “Settings > Local translation”, choose “Mozilla Translations model”

When the network is available, open “Settings > Offline language models” to retrieve Mozilla’s official model catalog. The app shows the size of each language direction and stores downloaded models under `%LOCALAPPDATA%\Windtranslator\OfflineModels`. Downloaded models can be used or deleted later; when offline, already downloaded models remain available for use and deletion.

### Basic usage

1. Select a translation mode, source language, and target language.
2. Choose a provider in Settings, enter the API key, and confirm the model and endpoint.
3. Click “Translate”, then copy the result or use text-to-speech.
4. Local translation can use an OpenAI-compatible endpoint such as `http://localhost:11434/v1`, or a Mozilla Translations model directory.
