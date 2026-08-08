using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windtranslator.Models;

namespace Windtranslator.Services;

public sealed class TranslationService
{
    private readonly HttpClient _httpClient;

    public TranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var endpoint = BuildChatEndpoint(request.Endpoint);
        var payload = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserText },
            },
            temperature = 0.2,
            stream = false,
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content,
        };

        AddAuthorization(requestMessage, request.ApiKey);

        using var response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ExtractErrorMessage(responseBody) ?? $"HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException($"翻译请求失败：{errorMessage}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                var translatedText = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(translatedText))
                {
                    return translatedText.Trim();
                }
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("翻译服务返回了无法解析的响应。");
        }

        throw new InvalidOperationException("翻译服务没有返回可用的译文。");
    }

    public async Task<string> TranslateImageAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildChatEndpoint(request.Endpoint);
        var payload = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = request.UserText },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = request.ImageDataUrl },
                        },
                    },
                },
            },
            temperature = 0.2,
            stream = false,
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content,
        };

        AddAuthorization(requestMessage, request.ApiKey);

        using var response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ExtractErrorMessage(responseBody) ?? $"HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException($"图片翻译请求失败：{errorMessage}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                var translatedText = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(translatedText))
                {
                    return translatedText.Trim();
                }
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("图片翻译服务返回了无法解析的响应。");
        }

        throw new InvalidOperationException("图片翻译服务没有返回可用的译文。");
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(
        string baseUrl,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var modelsUrl = BuildModelsEndpoint(baseUrl);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        AddAuthorization(requestMessage, apiKey);

        using var response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ExtractErrorMessage(responseBody) ?? $"HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException($"获取模型列表失败：{errorMessage}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var models = new List<string>();

            if (root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    {
                        var modelId = id.GetString();
                        if (!string.IsNullOrWhiteSpace(modelId))
                        {
                            models.Add(modelId);
                        }
                    }
                }
            }

            return models;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("本地服务返回了无法解析的模型列表。");
        }
    }

    private static void AddAuthorization(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    internal static string BuildChatEndpoint(string baseUrl)
    {
        var url = baseUrl.Trim();
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return url.TrimEnd('/') + "/chat/completions";
    }

    private static string BuildModelsEndpoint(string baseUrl)
    {
        var url = baseUrl.Trim();
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return url[..^"/chat/completions".Length].TrimEnd('/') + "/models";
        }

        return url.TrimEnd('/') + "/models";
    }

    private static string? ExtractErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
