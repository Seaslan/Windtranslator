using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text;

namespace Windtranslator.Services;

public static class Localization
{
    private static readonly ResourceManager Resources = new("Windtranslator.Resources.UiStrings", typeof(Localization).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static string AppName => Text("Windtranslator");

    public static string ProviderName(string name) => name is "千问" or "智谱" or "自定义模型"
        ? Text(name)
        : name;

    // Keep persisted provider IDs, API language codes, and translation prompts independent of UI language.
    public static void Initialize(string? preference)
    {
        var language = preference is "zh-CN" or "en-US"
            ? preference
            : (Windows.System.UserProfile.GlobalizationPreferences.Languages.Count > 0
                ? Windows.System.UserProfile.GlobalizationPreferences.Languages[0]
                : CultureInfo.InstalledUICulture.Name);
        Culture = CultureInfo.GetCultureInfo(language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = Culture.Name;
    }

    public static string Get(string key) => Resources.GetString(key, Culture)
        ?? throw new InvalidOperationException($"Missing UI resource: {key}");

    public static string Text(string source, params object?[] arguments)
    {
        var key = "S" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16].ToLowerInvariant();
        var value = Get(key);
        return arguments.Length == 0 ? value : string.Format(Culture, value, arguments);
    }
}

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class LocalizedStringExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Localization.Get(Key);
}

public sealed class LocalizedProviderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string name
            ? Localization.ProviderName(name)
            : value;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
