# Windtranslator

WinUI 3 桌面翻译工具，支持云端 API 翻译、AI 翻译和本地 AI 翻译。

## 功能

- 翻译模式：API 翻译、AI 翻译、本地 AI 翻译
- 服务商：DeepSeek、千问，本地 OpenAI 兼容服务（Ollama / LM Studio）
- 预置模型：DeepSeek `v4flash` / `v4pro`，千问 `qwen3.7-plus` / `qwen3.7-flash` / `qwen3.7-max`
- API Key 仅由用户手动输入；默认不落盘，可勾选后保存到 Windows 凭据库
- AI 提示词可自定义，默认翻译提示词始终保留
- 图片翻译：侧边栏选择图片，使用千问 API 的 `qwen3.5-ocr` 模型
- 本地 AI 翻译：可使用 OpenAI 兼容接口，或选择用户本地保存的 T5 中英俄翻译模型目录
- 语音输入：Windows 语音识别
- 语音输出：Windows 文本转语音
- 设置自动保存到 `%LOCALAPPDATA%\Windtranslator\settings.json`

## 构建

需要 .NET 8 SDK 和 Windows App SDK 2.3.1。

```powershell
dotnet build Windtranslator.slnx -p:Platform=x64
```

也可以直接用 Visual Studio 打开 `Windtranslator.slnx`，选择 `Windtranslator (Unpackaged)` 运行。

### 本地 T5 模型

在“设置 > 本地 AI 翻译”中选择“本地模型”，然后选择导出的 ONNX 模型文件夹。文件夹需包含 `encoder_model.onnx`、`decoder_model.onnx` 和 `tokenizer.json`，例如 `t5_translate_en_ru_zh_small_1024_onnx`。模型不会复制到应用目录或发布包。

本地推理由应用内的 ONNX Runtime 完成，无需安装 Python、PyTorch 或其他本地服务。

当前 T5 模型支持中英俄互译，目标语言可选择简体中文、英语或俄语。

## 使用

1. 选择翻译模式和源/目标语言。
2. 在设置区选择服务商，手动输入 API Key，确认模型和接口地址。
3. 点击“翻译”得到译文，可复制或朗读。
4. 本地 AI 模式默认使用 `http://localhost:11434/v1`，可切换为 LM Studio 的 `http://localhost:1234/v1`，并通过“刷新本地模型”获取模型列表。
