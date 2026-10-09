using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

    public Task<string> TranslateAsync(
        TranslationRequest request, CancellationToken cancellationToken,
        bool stream = false, Action<string>? onTextUpdated = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserText },
            },
            temperature = 0.2,
            stream,
        });
        return SendChatAsync(request.Endpoint, request.ApiKey, payload, stream, false, onTextUpdated, cancellationToken);
    }

    public Task<string> TranslateImageAsync(
        ImageTranslationRequest request, CancellationToken cancellationToken,
        bool stream = false, Action<string>? onTextUpdated = null)
    {
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
                        new { type = "image_url", image_url = new { url = request.ImageDataUrl } },
                    },
                },
            },
            temperature = 0.2,
            stream,
        });
        return SendChatAsync(request.Endpoint, request.ApiKey, payload, stream, true, onTextUpdated, cancellationToken);
    }

    private async Task<string> SendChatAsync(
        string endpoint, string? apiKey, string payload, bool stream, bool image,
        Action<string>? onTextUpdated, CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, BuildChatEndpoint(endpoint))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        AddAuthorization(requestMessage, apiKey);
        using var response = await _httpClient.SendAsync(
            requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw RequestFailed(image, ExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}");
        }

        // Some compatible providers return a normal JSON response even when streaming is requested.
        if (!stream || string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var document = JsonDocument.Parse(body);
                if (TryGetContent(document.RootElement, "message", out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    onTextUpdated?.Invoke(text.Trim());
                    return text.Trim();
                }
                var error = ExtractErrorMessage(body);
                if (error is not null) throw RequestFailed(image, error);
            }
            catch (JsonException)
            {
                throw InvalidResponse(image);
            }
            throw EmptyResponse(image);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var result = new StringBuilder();
        var eventData = new StringBuilder();
        var lastUpdate = Stopwatch.StartNew();
        var completed = false;
        var finishedChoice = false;
        while (!completed)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null || line.Length == 0)
            {
                if (eventData.Length > 0)
                {
                    var data = eventData.ToString();
                    eventData.Clear();
                    if (data.Trim() == "[DONE]")
                    {
                        completed = true;
                    }
                    else
                    {
                        try
                        {
                            using var document = JsonDocument.Parse(data);
                            var root = document.RootElement;
                            var error = ExtractErrorMessage(data);
                            if (error is not null) throw RequestFailed(image, error);
                            if (TryGetContent(root, "delta", out var delta)) result.Append(delta);
                            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
                                && choices.GetArrayLength() > 0
                                && choices[0].TryGetProperty("finish_reason", out var reason)
                                && reason.ValueKind == JsonValueKind.String)
                            {
                                finishedChoice = true;
                            }
                        }
                        catch (JsonException)
                        {
                            throw InvalidResponse(image);
                        }
                        // Limit UI refreshes to about 20 per second, with no queued updates after cancellation.
                        if (result.Length > 0 && lastUpdate.ElapsedMilliseconds >= 50)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            onTextUpdated?.Invoke(result.ToString());
                            lastUpdate.Restart();
                        }
                    }
                }
                if (line is null) break;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (eventData.Length > 0) eventData.Append('\n');
                eventData.Append(line[5..].TrimStart(' '));
            }
            // Ignore event names, IDs, retry hints and SSE heartbeat comments.
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!completed && !finishedChoice)
            throw new InvalidOperationException(Localization.Text("流式翻译响应意外中断，请重试。"));
        var translated = result.ToString().Trim();
        if (translated.Length == 0) throw EmptyResponse(image);
        onTextUpdated?.Invoke(translated);
        return translated;
    }

    private static bool TryGetContent(JsonElement root, string messageProperty, out string text)
    {
        text = string.Empty;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0 && choices[0].TryGetProperty(messageProperty, out var message)
            && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString() ?? string.Empty;
            return true;
        }
        return false;
    }

    private static InvalidOperationException RequestFailed(bool image, string error) => new(image
        ? Localization.Text("图片翻译请求失败：{0}", error) : Localization.Text("翻译请求失败：{0}", error));

    private static InvalidOperationException InvalidResponse(bool image) => new(image
        ? Localization.Text("图片翻译服务返回了无法解析的响应。") : Localization.Text("翻译服务返回了无法解析的响应。"));

    private static InvalidOperationException EmptyResponse(bool image) => new(image
        ? Localization.Text("图片翻译服务没有返回可用的译文。") : Localization.Text("翻译服务没有返回可用的译文。"));

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
            throw new InvalidOperationException(Localization.Text("获取模型列表失败：{0}", errorMessage));
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
            throw new InvalidOperationException(Localization.Text("本地服务返回了无法解析的模型列表。"));
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
