# Windtranslator

WinUI 3 桌面翻译工具，支持云端 API 翻译、AI 翻译和本地翻译。

## 功能

- 翻译模式：API 翻译、AI 翻译、本地翻译
- 服务商：DeepSeek、千问，本地 OpenAI 兼容服务（Ollama / LM Studio）
- 预置模型：DeepSeek `v4flash` / `v4pro`，千问 `qwen3.7-plus` / `qwen3.7-flash` / `qwen3.7-max`
- API Key 仅由用户手动输入；可选择保存到 Windows 凭据库
- AI 提示词可自定义补充要求
- 图片翻译：侧边栏选择图片，使用千问 API 的 `qwen3.5-ocr` 模型
- 本地翻译：可使用 OpenAI 兼容接口，或选择用户本地保存的 Mozilla Translations 模型目录
- 语音输入：Windows 语音识别
- 语音输出：Windows 文本转语音
- 设置自动保存到 `%LOCALAPPDATA%\Windtranslator\settings.json`

## 构建

需要 .NET 8 SDK 和 Windows App SDK 2.3.1。

```powershell
dotnet build Windtranslator.slnx -p:Platform=x64
```

也可以直接用 Visual Studio 打开 `Windtranslator.slnx`，选择 `Windtranslator (Unpackaged)` 运行。

### Mozilla Translations 本地模型

在“设置 > 本地翻译”中选择“Mozilla Translations 模型”，然后选择一个包含单个 `*.bergamot.yml` 的模型文件夹。配置文件与它引用的模型、词表和断句文件必须保留在同一目录。模型不会复制到应用目录或发布包。

本地推理由应用内置的 Mozilla Translations/Bergamot 原生引擎完成，无需安装 Python、PyTorch、ONNX Runtime 或其他本地服务。发布前需先运行 `tools\\Build-BergamotRuntime.ps1`，将 x64 运行库写入 `Runtime\\Bergamot\\x64`。

Mozilla/Bergamot 模型不是 ONNX 模型；已有的 T5 ONNX 文件夹无法直接使用。模型本身决定翻译方向，应用中选择的源语言和目标语言必须与模型一致。

联网时可在“设置 > 离线语言模型”获取 Mozilla 官方模型列表。应用显示每个语言方向的模型大小，下载后保存到 `%LOCALAPPDATA%\\Windtranslator\\OfflineModels`，可直接选择使用或删除；断网时仅保留已下载模型的使用和删除操作。

## 使用

1. 选择翻译模式和源/目标语言。
2. 在设置区选择服务商，手动输入 API Key，确认模型和接口地址。
3. 点击“翻译”得到译文，可复制或朗读。
4. 本地翻译模式可使用 `http://localhost:11434/v1` 的 OpenAI 兼容接口，也可切换为 Mozilla Translations 模型目录。
