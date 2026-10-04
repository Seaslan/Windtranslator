using System;
using System.Collections.Generic;
using System.Linq;
using Windtranslator.Models;

namespace Windtranslator.Services;

// AI and image translation use the same provider identities, endpoints and model catalog.
public static class ProviderCatalog
{
    private static readonly string[] BuiltInNames = { "DeepSeek", "千问", "Kimi", "智谱", "自定义模型" };

    public static void ResetToDefaults(AppSettings settings)
    {
        // The local translator shares these dictionaries but is outside the provider catalog.
        var localEndpoint = settings.Endpoints.GetValueOrDefault("本地 AI");
        var localModel = settings.Models.GetValueOrDefault("本地 AI");
        settings.Endpoints.Clear();
        settings.Models.Clear();
        if (localEndpoint is not null) settings.Endpoints["本地 AI"] = localEndpoint;
        if (localModel is not null) settings.Models["本地 AI"] = localModel;

        settings.ProviderNames = BuiltInNames.ToList();
        settings.AvailableModels.Clear();
        settings.ImageSelectedModels.Clear();
        settings.ImageEndpoints.Clear();
        settings.ImageModels.Clear();
        settings.LegacyProviderAliases.Clear();
        settings.ProviderKeysMigrated = true;
        foreach (var provider in settings.ProviderNames)
        {
            var profile = GetProfile(provider);
            var models = profile.DefaultModels.ToList();
            settings.Endpoints[provider] = profile.DefaultEndpoint;
            settings.AvailableModels[provider] = models;
            settings.Models[provider] = models.FirstOrDefault() ?? string.Empty;
            settings.ImageSelectedModels[provider] = models.FirstOrDefault() ?? string.Empty;
        }
        settings.ProviderName = settings.ProviderNames[0];
        settings.ImageProviderName = settings.ProviderNames[0];
        settings.ImageEndpoint = GetEndpoint(settings, settings.ImageProviderName);
        settings.ImageModel = settings.ImageSelectedModels[settings.ImageProviderName];
    }

    public static void Initialize(AppSettings settings)
    {
        settings.Endpoints = new(settings.Endpoints ?? new(), StringComparer.OrdinalIgnoreCase);
        settings.Models = new(settings.Models ?? new(), StringComparer.OrdinalIgnoreCase);
        settings.AvailableModels = new(settings.AvailableModels ?? new(), StringComparer.OrdinalIgnoreCase);
        settings.ImageEndpoints = new(settings.ImageEndpoints ?? new(), StringComparer.OrdinalIgnoreCase);
        settings.ImageSelectedModels = new(settings.ImageSelectedModels ?? new(), StringComparer.OrdinalIgnoreCase);
        settings.LegacyProviderAliases = new(settings.LegacyProviderAliases ?? new(), StringComparer.OrdinalIgnoreCase);
        var migrate = settings.ProviderNames is null;
        if (migrate && !string.IsNullOrWhiteSpace(settings.ImageEndpoint)
            && !settings.ImageEndpoints.ContainsKey(settings.ImageProviderName))
        {
            settings.ImageEndpoints[settings.ImageProviderName] = settings.ImageEndpoint;
        }
        settings.ProviderNames = (settings.ProviderNames ?? BuiltInNames
                .Concat(settings.AvailableModels.Keys)
                .Concat(new[] { settings.ProviderName, settings.ImageProviderName }).ToList())
            .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), "本地 AI", StringComparison.OrdinalIgnoreCase))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (settings.ProviderNames.Count == 0)
        {
            settings.ProviderNames.Add("DeepSeek");
        }

        foreach (var provider in settings.ProviderNames)
        {
            if (!settings.AvailableModels.TryGetValue(provider, out var models) || models is null)
            {
                models = GetProfile(provider).DefaultModels.ToList();
            }
            if (migrate)
            {
                if (settings.Models.TryGetValue(provider, out var selected))
                {
                    models.Add(selected);
                }
                // The old image list was available under every built-in image provider.
                if (provider != "自定义模型")
                {
                    models.AddRange(settings.ImageModels ?? new());
                }
                if (provider.Equals(settings.ImageProviderName, StringComparison.OrdinalIgnoreCase))
                {
                    models.Add(string.IsNullOrWhiteSpace(settings.ImageModel)
                        ? GetDefaultImageModel(provider) : settings.ImageModel);
                }
                if (!settings.Endpoints.TryGetValue(provider, out var endpoint) || string.IsNullOrWhiteSpace(endpoint))
                {
                    settings.Endpoints[provider] = settings.ImageEndpoints.TryGetValue(provider, out var imageEndpoint)
                        && !string.IsNullOrWhiteSpace(imageEndpoint)
                        ? imageEndpoint
                        : provider.Equals(settings.ImageProviderName, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(settings.ImageEndpoint)
                            ? settings.ImageEndpoint : GetProfile(provider).DefaultEndpoint;
                }
            }
            settings.AvailableModels[provider] = models
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (migrate)
        {
            // Keep a separate provider when the old image endpoint was configured independently.
            foreach (var provider in settings.ProviderNames.ToList())
            {
                if (!settings.ImageEndpoints.TryGetValue(provider, out var imageEndpoint)
                    || string.IsNullOrWhiteSpace(imageEndpoint)
                    || string.Equals(GetEndpoint(settings, provider).TrimEnd('/'), imageEndpoint.TrimEnd('/'), StringComparison.Ordinal))
                {
                    continue;
                }
                var host = Uri.TryCreate(imageEndpoint, UriKind.Absolute, out var uri) ? uri.Host : "2";
                var name = provider + " [" + host + "]";
                var suffix = 2;
                while (settings.ProviderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    name = provider + " [" + host + "] " + suffix++;
                }
                settings.ProviderNames.Add(name);
                settings.Endpoints[name] = imageEndpoint;
                settings.AvailableModels[name] = GetModels(settings, provider).ToList();
                settings.LegacyProviderAliases[name] = provider;
                if (provider.Equals(settings.ImageProviderName, StringComparison.OrdinalIgnoreCase))
                {
                    settings.ImageProviderName = name;
                }
            }
            settings.ImageSelectedModels[settings.ImageProviderName] = settings.ImageModel;
            settings.RememberKeys |= settings.RememberImageKeys;
            settings.RememberImageKeys = settings.RememberKeys;
        }
    }

    public static List<string> GetModels(AppSettings settings, string provider) =>
        settings.AvailableModels.TryGetValue(provider, out var models)
            ? models : settings.AvailableModels[provider] = new List<string>();

    public static string GetEndpoint(AppSettings settings, string provider) =>
        settings.Endpoints.TryGetValue(provider, out var endpoint)
            ? endpoint : GetProfile(provider).DefaultEndpoint;

    public static ProviderProfile GetProfile(string provider) => provider switch
    {
        "DeepSeek" => new ProviderProfile(
            "DeepSeek",
            "https://api.deepseek.com",
            new[] { "deepseek-flash", "deepseek-v4-pro" },
            true),
        "千问" => new ProviderProfile(
            "千问",
            "https://ws-hb89wnirs0jbftdm.cn-beijing.maas.aliyuncs.com/compatible-mode/v1",
            new[] { "qwen3.7-plus", "qwen3.7-flash", "qwen3.7-max" },
            true),
        "Kimi" => new ProviderProfile(
            "Kimi",
            "https://api.moonshot.cn/v1",
            new[] { "kimi-k2.6" },
            true),
        "智谱" => new ProviderProfile(
            "智谱",
            "https://open.bigmodel.cn/api/paas/v4",
            new[] { "glm-5.3-flash" },
            true),
        "自定义模型" => new ProviderProfile(
            "自定义模型",
            string.Empty,
            Array.Empty<string>(),
            true),
        _ => new ProviderProfile(
            provider,
            string.Empty,
            Array.Empty<string>(),
            true),
    };

    public static string GetDefaultImageModel(string provider) => provider switch
    {
        "DeepSeek" => "deepseek-flash",
        "Kimi" => "kimi-k2.6",
        "智谱" => "glm-5.3-flash",
        _ => "qwen3.5-ocr",
    };
}
