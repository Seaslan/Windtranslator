using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Windtranslator.Services;

public sealed class BergamotTranslationService
{
    public async Task<string> TranslateAsync(
        string modelDirectory,
        string sourceText,
        CancellationToken cancellationToken)
    {
        var executablePath = ResolveExecutablePath();
        var configurationPath = ResolveModelConfiguration(modelDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(configurationPath)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--model-config-paths");
        startInfo.ArgumentList.Add(configurationPath);
        startInfo.ArgumentList.Add("--cpu-threads");
        startInfo.ArgumentList.Add(Math.Max(1, Environment.ProcessorCount - 1).ToString());

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Mozilla Translations 本地翻译引擎。");
        using var cancellationRegistration = cancellationToken.Register(() => TryStop(process));

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(sourceText.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"Mozilla Translations 翻译引擎异常退出，退出代码 {process.ExitCode}。"
                    : "Mozilla Translations 翻译失败：" + error);
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Mozilla Translations 模型没有生成可用的译文。");
        }

        return output;
    }

    private static string ResolveExecutablePath()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new InvalidOperationException("Mozilla Translations 本地翻译仅支持 x64 或 ARM64 发布版本。"),
        };
        var executablePath = Path.Combine(
            AppContext.BaseDirectory,
            "Runtime",
            "Bergamot",
            architecture,
            "translator-cli.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                $"未找到 Mozilla Translations {architecture} 运行库。请使用 tools\\Build-BergamotRuntime.ps1 构建后重新发布应用。");
        }

        return executablePath;
    }

    private static string ResolveModelConfiguration(string modelDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelDirectory) || !Directory.Exists(modelDirectory))
        {
            throw new InvalidOperationException("Mozilla Translations 模型文件夹不存在。");
        }

        var configurations = Directory
            .EnumerateFiles(modelDirectory, "*.bergamot.yml", SearchOption.TopDirectoryOnly)
            .ToArray();
        return configurations.Length switch
        {
            1 => configurations[0],
            0 => throw new InvalidOperationException(
                "所选文件夹不是 Mozilla Translations 模型，缺少 .bergamot.yml 配置文件。"),
            _ => throw new InvalidOperationException(
                "所选模型文件夹包含多个 .bergamot.yml 配置文件，请为每个翻译方向选择单独的模型文件夹。"),
        };
    }

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
