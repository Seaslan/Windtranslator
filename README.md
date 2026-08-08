# Windtranslator

WinUI 3 桌面翻译工具，支持云端 API 翻译、AI 翻译和本地 AI 翻译。

## 功能

- 翻译模式：API 翻译、AI 翻译、本地 AI 翻译
- 服务商：DeepSeek、千问，本地 OpenAI 兼容服务（Ollama / LM Studio）
- 预置模型：DeepSeek `v4flash` / `v4pro`，千问 `qwen3.7-plus` / `qwen3.7-flash` / `qwen3.7-max`
- API Key 仅由用户手动输入；默认不落盘，可勾选后保存到 Windows 凭据库
- AI 提示词可自定义，默认翻译提示词始终保留
- 图片翻译：侧边栏选择图片，使用千问 API 的 `qwen3.5-ocr` 模型
- 目标语言不允许为自动检测，也不允许与原语言相同
- 语音输入：Windows 语音识别
- 语音输出：Windows 文本转语音
- 设置自动保存到 `%LOCALAPPDATA%\Windtranslator\settings.json`

## 构建

需要 .NET 8 SDK 和 Windows App SDK 2.3.1。

```powershell
dotnet build Windtranslator.slnx -p:Platform=x64
```

也可以直接用 Visual Studio 打开 `Windtranslator.slnx`，选择 `Windtranslator (Unpackaged)` 运行。

## 使用

1. 选择翻译模式和源/目标语言。
2. 在设置区选择服务商，手动输入 API Key，确认模型和接口地址。
3. 点击“翻译”得到译文，可复制或朗读。
4. 本地 AI 模式默认使用 `http://localhost:11434/v1`，可切换为 LM Studio 的 `http://localhost:1234/v1`，并通过“刷新本地模型”获取模型列表。
