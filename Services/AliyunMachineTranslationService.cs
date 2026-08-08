using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windtranslator.Models;

namespace Windtranslator.Services;

public sealed class AliyunMachineTranslationService
{
    private const string ActionName = "TranslateECommerce";
    private const string ApiVersion = "2018-10-12";
    private const string RegionId = "cn-hangzhou";

    private readonly HttpClient _httpClient;

    public AliyunMachineTranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> TranslateAsync(
        MachineTranslationRequest request,
        CancellationToken cancellationToken)
    {
        var signedUrl = BuildSignedUrl(
            request.ServiceUrl,
            request.AccessKeyId,
            request.AccessKeySecret);

        var body = JsonSerializer.Serialize(new
        {
            FormatType = "text",
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            SourceText = request.SourceText,
            Scene = "title",
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, signedUrl)
        {
            Content = content,
        };

        using var response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ExtractErrorMessage(responseBody) ?? $"HTTP {(int)response.StatusCode}";
            throw new InvalidOperationException($"机器翻译请求失败：{errorMessage}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.TryGetProperty("Code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() != "200")
            {
                var message = root.TryGetProperty("Message", out var errorMessageElement)
                    && errorMessageElement.ValueKind == JsonValueKind.String
                        ? errorMessageElement.GetString()
                        : "未知错误";
                throw new InvalidOperationException($"机器翻译请求失败：{message}");
            }

            if (root.TryGetProperty("Data", out var data)
                && data.TryGetProperty("Translated", out var translated)
                && translated.ValueKind == JsonValueKind.String)
            {
                var translatedText = translated.GetString();
                if (!string.IsNullOrWhiteSpace(translatedText))
                {
                    return translatedText.Trim();
                }
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("机器翻译服务返回了无法解析的响应。");
        }

        throw new InvalidOperationException("机器翻译服务没有返回可用的译文。");
    }

    private static string BuildSignedUrl(string serviceUrl, string accessKeyId, string accessKeySecret)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["AccessKeyId"] = accessKeyId,
            ["Action"] = ActionName,
            ["Format"] = "JSON",
            ["RegionId"] = RegionId,
            ["SignatureMethod"] = "HMAC-SHA1",
            ["SignatureNonce"] = Guid.NewGuid().ToString("N"),
            ["SignatureVersion"] = "1.0",
            ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["Version"] = ApiVersion,
        };

        var canonicalizedQuery = string.Join(
            "&",
            parameters.Select(parameter =>
                $"{PercentEncode(parameter.Key)}={PercentEncode(parameter.Value)}"));

        var stringToSign = "POST&"
            + PercentEncode("/")
            + "&"
            + PercentEncode(canonicalizedQuery);

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(accessKeySecret + "&"));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        return $"{serviceUrl}?{canonicalizedQuery}&Signature={PercentEncode(signature)}";
    }

    private static string PercentEncode(string value)
    {
        return Uri.EscapeDataString(value)
            .Replace("+", "%20")
            .Replace("*", "%2A")
            .Replace("%7E", "~");
    }

    private static string? ExtractErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
