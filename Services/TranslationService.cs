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
        // Use model defaults: some providers reject custom sampling parameters.
        var payload = JsonSerializer.Serialize(new
        {
            model = request.Model,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserText },
            },
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
        if (stream)
        {
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }
        using var response = await _httpClient.SendAsync(
            requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw RequestFailed(image, ExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}");
        }

        if (!stream)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ReadJsonResponse(body, image, onTextUpdated, cancellationToken);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var result = new StringBuilder();
        var eventData = new StringBuilder();
        var lastUpdate = Stopwatch.StartNew();
        var publishedLength = 0;
        var responseFormatKnown = false;
        var completed = false;
        var finishedChoice = false;
        while (!completed)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!responseFormatKnown && !string.IsNullOrWhiteSpace(line))
            {
                responseFormatKnown = true;
                var start = line.TrimStart();
                // Compatible gateways can mislabel SSE as application/json, or return
                // JSON despite stream=true. Inspect the body without buffering SSE.
                if (start.StartsWith("{", StringComparison.Ordinal) || start.StartsWith("[", StringComparison.Ordinal))
                {
                    var body = line + "\n" + await reader.ReadToEndAsync(cancellationToken);
                    return ReadJsonResponse(body, image, onTextUpdated, cancellationToken);
                }
            }
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
                        // Show the first content event immediately. Throttle only later
                        // updates, so a short response is not held until completion.
                        if (result.Length > publishedLength
                            && (publishedLength == 0 || lastUpdate.ElapsedMilliseconds >= 50))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            onTextUpdated?.Invoke(result.ToString());
                            publishedLength = result.Length;
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

    private static string ReadJsonResponse(
        string body, bool image, Action<string>? onTextUpdated, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (TryGetContent(document.RootElement, "message", out var text) && !string.IsNullOrWhiteSpace(text))
            {
                var translated = text.Trim();
                onTextUpdated?.Invoke(translated);
                return translated;
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
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(Localization.Text("服务返回了无法解析的模型列表。"));
            }
            var error = ExtractErrorMessage(responseBody);
            if (error is not null)
                throw new InvalidOperationException(Localization.Text("获取模型列表失败：{0}", error));
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException(Localization.Text("服务返回了无法解析的模型列表。"));

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                var modelId = id.GetString()?.Trim();
                if (!string.IsNullOrEmpty(modelId) && known.Add(modelId)) models.Add(modelId);
            }

            return models;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(Localization.Text("服务返回了无法解析的模型列表。"));
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
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
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
