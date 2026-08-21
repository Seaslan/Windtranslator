# Mozilla Translations Runtime

`translator-cli.exe` is the native Mozilla Translations/Bergamot inference engine. It is included in the application package, but translation models are intentionally not included.

Build the x64 runtime from the repository root:

```powershell
.\tools\Build-BergamotRuntime.ps1
```

The script requires CMake and the Visual Studio "Desktop development with C++" workload. It copies the resulting engine to `Runtime\Bergamot\x64`, where the project file includes it in packaged and published builds. Use `-Architecture arm64` to build the ARM64 runtime after the x64 build works.

## Model folders

In the app, choose one folder containing exactly one `*.bergamot.yml` file. The configuration and its referenced model, vocabulary, and sentence-splitting files must remain together in that folder. The selected translation direction must match the model's direction.

Mozilla/Bergamot models are not ONNX models. Existing T5 folders containing files such as `encoder_model.onnx`, `decoder_model.onnx`, or `tokenizer.json` cannot be used by this engine.
