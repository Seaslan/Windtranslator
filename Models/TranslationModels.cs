using System;
using System.Collections.Generic;

namespace Windtranslator.Models;

public sealed class LanguageOption
{
    public LanguageOption(string display, string promptName, string? speechTag, string apiCode)
    {
        Display = display;
        PromptName = promptName;
        SpeechTag = speechTag;
        ApiCode = apiCode;
    }

    public string Display { get; }

    public string PromptName { get; }

    public string? SpeechTag { get; }

    public string ApiCode { get; }
}

public sealed class ProviderProfile
{
    public ProviderProfile(
        string name,
        string defaultEndpoint,
        IReadOnlyList<string> defaultModels,
        bool requiresKey)
    {
        Name = name;
        DefaultEndpoint = defaultEndpoint;
        DefaultModels = defaultModels;
        RequiresKey = requiresKey;
    }

    public string Name { get; }

    public string DefaultEndpoint { get; }

    public IReadOnlyList<string> DefaultModels { get; }

    public bool RequiresKey { get; }
}

public sealed record TranslationRequest(
    string Endpoint,
    string? ApiKey,
    string Model,
    string SystemPrompt,
    string UserText);

public sealed record ImageTranslationRequest(
    string Endpoint,
    string? ApiKey,
    string Model,
    string SystemPrompt,
    string UserText,
    string ImageDataUrl);

public sealed record MachineTranslationRequest(
    string ServiceUrl,
    string AccessKeyId,
    string AccessKeySecret,
    string SourceLanguage,
    string TargetLanguage,
    string SourceText);

public sealed class AppSettings
{
    public int ModeIndex { get; set; } = 0;

    public string ProviderName { get; set; } = "DeepSeek";

    public int SourceLanguageIndex { get; set; } = 1;

    public int TargetLanguageIndex { get; set; } = 3;

    public string CustomPrompt { get; set; } = string.Empty;

    public int AiPromptStyleIndex { get; set; }

    public bool AiIncludeLanguageDetails { get; set; }

    public string ImageEndpoint { get; set; } = string.Empty;

    public string ImageProviderName { get; set; } = "DeepSeek";

    public Dictionary<string, string> ImageEndpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ImageModel { get; set; } = string.Empty;

    public List<string> ImageModels { get; set; } = new();

    public bool RememberImageKeys { get; set; }

    public bool RememberKeys { get; set; }

    public bool RememberAliyunKeys { get; set; }

    public int ThemeIndex { get; set; }

    public bool MicaBackdropEnabled { get; set; } = true;

    public int LocalTranslationSourceIndex { get; set; }

    public string LocalModelPath { get; set; } = string.Empty;

    // 0 disables history; -1 keeps every entry.
    public int TranslationHistoryLimit { get; set; } = 20;

    public List<string> TranslationHistory { get; set; } = new();

    public Dictionary<string, string> Endpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> AvailableModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OfflineTranslationModel
{
    public OfflineTranslationModel(
        string id,
        string sourceLanguageCode,
        string targetLanguageCode,
        long sizeBytes,
        IReadOnlyList<OfflineModelFile> files,
        string? localDirectory = null)
    {
        Id = id;
        SourceLanguageCode = sourceLanguageCode;
        TargetLanguageCode = targetLanguageCode;
        SizeBytes = sizeBytes;
        Files = files;
        LocalDirectory = localDirectory;
    }

    public string Id { get; }

    public string SourceLanguageCode { get; }

    public string TargetLanguageCode { get; }

    public long SizeBytes { get; }

    public IReadOnlyList<OfflineModelFile> Files { get; }

    public string? LocalDirectory { get; }

    public bool IsInstalled => !string.IsNullOrWhiteSpace(LocalDirectory);

    public string Title { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public string PrimaryActionText => IsInstalled ? "使用" : "下载";

    public bool CanDownload { get; set; }
}

public sealed record OfflineModelFile(string Path);
