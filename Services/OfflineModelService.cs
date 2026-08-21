using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windtranslator.Models;

namespace Windtranslator.Services;

public sealed class OfflineModelService
{
    private const string RegistryUrl =
        "https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/db/models.json";
    private const string ManifestFileName = "offline-model.json";
    private const string ConfigFileName = "model.bergamot.yml";
    private readonly HttpClient _httpClient;

    public OfflineModelService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string ModelRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Windtranslator",
        "OfflineModels");

    public async Task<IReadOnlyList<OfflineTranslationModel>> GetAvailableModelsAsync(
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(RegistryUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var baseUrl = root.GetProperty("baseUrl").GetString()
            ?? throw new InvalidOperationException("Mozilla 模型注册表缺少下载地址。");
        var models = root.GetProperty("models");
        var installed = GetInstalledModels()
            .ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<OfflineTranslationModel>();

        foreach (var languagePair in models.EnumerateObject())
        {
            var selected = SelectPreferredModel(languagePair.Value);
            if (selected.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var sourceLanguage = selected.GetProperty("sourceLanguage").GetString();
            var targetLanguage = selected.GetProperty("targetLanguage").GetString();
            if (string.IsNullOrWhiteSpace(sourceLanguage) || string.IsNullOrWhiteSpace(targetLanguage))
            {
                continue;
            }

            var files = ReadFiles(selected.GetProperty("files"), baseUrl);
            if (!files.Any(file => file.Kind == "model") || !files.Any(file => file.Kind == "vocab" || file.Kind == "srcVocab")
                || !files.Any(file => file.Kind == "vocab" || file.Kind == "trgVocab"))
            {
                continue;
            }

            var modelUrl = files.Single(file => file.Kind == "model").Url;
            files.Add(new RemoteModelFile("metadata", new Uri(new Uri(modelUrl), "metadata.json").AbsoluteUri));

            var id = ToModelId(sourceLanguage, targetLanguage);
            var sizeBytes = selected.GetProperty("files").GetProperty("model")
                .TryGetProperty("uncompressedSize", out var size)
                    ? size.GetInt64()
                    : 0;
            installed.TryGetValue(id, out var installedModel);
            result.Add(new OfflineTranslationModel(
                id,
                sourceLanguage,
                targetLanguage,
                sizeBytes,
                files.Select(file => new OfflineModelFile(file.Url)).ToList(),
                installedModel?.LocalDirectory));
        }

        return result
            .OrderBy(model => model.SourceLanguageCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.TargetLanguageCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<OfflineTranslationModel> GetInstalledModels()
    {
        if (!Directory.Exists(ModelRootDirectory))
        {
            return Array.Empty<OfflineTranslationModel>();
        }

        var models = new List<OfflineTranslationModel>();
        foreach (var directory in Directory.EnumerateDirectories(ModelRootDirectory))
        {
            try
            {
                var manifestPath = Path.Combine(directory, ManifestFileName);
                var configPath = Path.Combine(directory, ConfigFileName);
                if (!File.Exists(manifestPath) || !File.Exists(configPath))
                {
                    continue;
                }

                var manifest = JsonSerializer.Deserialize<OfflineModelManifest>(File.ReadAllText(manifestPath));
                if (manifest is null || !IsSafeModelId(manifest.Id))
                {
                    continue;
                }

                models.Add(new OfflineTranslationModel(
                    manifest.Id,
                    manifest.SourceLanguageCode,
                    manifest.TargetLanguageCode,
                    manifest.SizeBytes,
                    Array.Empty<OfflineModelFile>(),
                    directory));
            }
            catch (IOException)
            {
                // A concurrent download or a manually modified folder is ignored until it is complete.
            }
            catch (JsonException)
            {
                // Only models created by this app have a manifest that can be safely managed.
            }
        }

        return models;
    }

    public async Task DownloadAsync(
        OfflineTranslationModel model,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (model.IsInstalled || model.Files.Count == 0)
        {
            return;
        }

        if (!IsSafeModelId(model.Id))
        {
            throw new InvalidOperationException("模型标识无效，无法创建下载目录。");
        }

        Directory.CreateDirectory(ModelRootDirectory);
        var targetDirectory = GetModelDirectory(model.Id);
        var temporaryDirectory = targetDirectory + ".downloading";
        EnsureManagedPath(targetDirectory);
        EnsureManagedPath(temporaryDirectory);

        DeleteDirectoryIfExists(temporaryDirectory);
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            for (var index = 0; index < model.Files.Count; index++)
            {
                var file = model.Files[index];
                progress?.Report((double)index / model.Files.Count);
                using var request = new HttpRequestMessage(HttpMethod.Get, file.Path);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                var compressedName = Path.GetFileName(new Uri(file.Path).LocalPath);
                var outputName = compressedName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? compressedName[..^3]
                    : compressedName;
                var outputPath = Path.Combine(temporaryDirectory, outputName);
                await using var output = File.Create(outputPath);

                if (compressedName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    await using var gzip = new GZipStream(source, CompressionMode.Decompress);
                    await CopyToAsync(gzip, output, cancellationToken);
                }
                else
                {
                    await CopyToAsync(source, output, cancellationToken);
                }

                progress?.Report((double)(index + 1) / model.Files.Count);
            }

            WriteConfiguration(temporaryDirectory, model);
            var manifest = new OfflineModelManifest(
                model.Id,
                model.SourceLanguageCode,
                model.TargetLanguageCode,
                model.SizeBytes);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, ManifestFileName),
                JsonSerializer.Serialize(manifest),
                cancellationToken);

            DeleteDirectoryIfExists(targetDirectory);
            Directory.Move(temporaryDirectory, targetDirectory);
            progress?.Report(1);
        }
        catch
        {
            DeleteDirectoryIfExists(temporaryDirectory);
            throw;
        }
    }

    public void Delete(OfflineTranslationModel model)
    {
        if (!model.IsInstalled || string.IsNullOrWhiteSpace(model.LocalDirectory))
        {
            return;
        }

        var expectedDirectory = GetModelDirectory(model.Id);
        if (!PathsEqual(expectedDirectory, model.LocalDirectory))
        {
            throw new InvalidOperationException("只能删除由应用下载的离线模型。");
        }

        DeleteDirectoryIfExists(expectedDirectory);
    }

    private static JsonElement SelectPreferredModel(JsonElement candidates)
    {
        var ordered = candidates.EnumerateArray()
            .Where(candidate => candidate.TryGetProperty("files", out _))
            .OrderByDescending(candidate =>
                candidate.TryGetProperty("releaseStatus", out var status)
                && status.ValueKind == JsonValueKind.String
                && status.GetString()?.StartsWith("Release", StringComparison.OrdinalIgnoreCase) == true)
            .ThenByDescending(candidate =>
                candidate.TryGetProperty("architecture", out var architecture)
                && architecture.GetString() == "base")
            .ToList();
        return ordered.Count > 0 ? ordered[0] : default;
    }

    private static List<RemoteModelFile> ReadFiles(JsonElement files, string baseUrl)
    {
        var result = new List<RemoteModelFile>();
        foreach (var file in files.EnumerateObject())
        {
            if (file.Value.TryGetProperty("path", out var path) && path.GetString() is { Length: > 0 } relativePath)
            {
                result.Add(new RemoteModelFile(
                    file.Name,
                    baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/')));
            }
        }

        return result;
    }

    private static async Task CopyToAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void WriteConfiguration(string directory, OfflineTranslationModel model)
    {
        var fileNames = Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToList();
        var modelFile = fileNames.Single(fileName => fileName.Contains(".intgemm.alphas.bin", StringComparison.OrdinalIgnoreCase));
        var vocabularyFiles = fileNames.Where(fileName => fileName.EndsWith(".spm", StringComparison.OrdinalIgnoreCase)).ToList();
        var sourceVocabulary = vocabularyFiles.SingleOrDefault(fileName => fileName.StartsWith("srcvocab.", StringComparison.OrdinalIgnoreCase))
            ?? vocabularyFiles.Single(fileName => fileName.StartsWith("vocab.", StringComparison.OrdinalIgnoreCase));
        var targetVocabulary = vocabularyFiles.SingleOrDefault(fileName => fileName.StartsWith("trgvocab.", StringComparison.OrdinalIgnoreCase))
            ?? sourceVocabulary;
        var shortlist = fileNames.SingleOrDefault(fileName => fileName.Contains(".s2t.bin", StringComparison.OrdinalIgnoreCase));
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "metadata.json")));
        if (!metadata.RootElement.TryGetProperty("modelConfig", out var modelConfig)
            || modelConfig.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("离线模型元数据缺少 Bergamot 配置。");
        }

        var lines = new List<string>();
        foreach (var setting in modelConfig.EnumerateObject())
        {
            AppendYamlSetting(lines, setting.Name, setting.Value);
        }

        lines.AddRange(new[]
        {
            "models:",
            "  - " + modelFile,
            "vocabs:",
            "  - " + sourceVocabulary,
            "  - " + targetVocabulary,
            "beam-size: 1",
            "normalize: 1.0",
            "max-length-break: 128",
            "mini-batch-words: 1024",
            "workspace: 128",
            "ssplit-mode: paragraph",
        });
        if (!string.IsNullOrEmpty(shortlist))
        {
            lines.Add("shortlist:");
            lines.Add("  - " + shortlist);
            lines.Add("  - false");
        }

        File.WriteAllLines(Path.Combine(directory, ConfigFileName), lines);
    }

    private static void AppendYamlSetting(List<string> lines, string name, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            lines.Add(name + ": " + ToYamlScalar(value));
            return;
        }

        if (value.GetArrayLength() == 0)
        {
            lines.Add(name + ": []");
            return;
        }

        lines.Add(name + ":");
        foreach (var item in value.EnumerateArray())
        {
            lines.Add("  - " + ToYamlScalar(item));
        }
    }

    private static string ToYamlScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.GetRawText(),
    };

    private string GetModelDirectory(string modelId) => Path.Combine(ModelRootDirectory, modelId);

    private void EnsureManagedPath(string path)
    {
        var root = Path.GetFullPath(ModelRootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("离线模型目录无效。");
        }
    }

    private void DeleteDirectoryIfExists(string path)
    {
        EnsureManagedPath(path);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool PathsEqual(string first, string second) => string.Equals(
        Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    private static string ToModelId(string sourceLanguage, string targetLanguage) => sourceLanguage + "-" + targetLanguage;

    private static bool IsSafeModelId(string modelId) => modelId.All(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_');

    private sealed record RemoteModelFile(string Kind, string Url);

    private sealed record OfflineModelManifest(
        string Id,
        string SourceLanguageCode,
        string TargetLanguageCode,
        long SizeBytes);
}
