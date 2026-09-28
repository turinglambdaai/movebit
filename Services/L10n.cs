using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MoveBit.Services;

/// <summary>
/// Runtime UI localization. All strings live in Assets/Strings.{zh,en}.axaml as
/// x:String resources; <see cref="Apply"/> merges the right dictionary into
/// App.Resources so every DynamicResource binding updates live. Strings composed
/// in code go through <see cref="T"/>, which reads the same merged dictionary —
/// one source of truth per language.
/// </summary>
public static class L10n
{
    /// Resolved culture for date/number formatting ("ddd" day labels, MM-DD, etc.).
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public static bool IsEnglish => Culture.TwoLetterISOLanguageName != "zh";

    /// Currently merged string dictionary, tracked so it can be swapped in place.
    private static ResourceDictionary? _strings;

    /// Merge the dictionary matching <paramref name="setting"/> ("auto" | "zh" | "en").
    public static void Apply(string? setting)
    {
        Culture = setting switch
        {
            "zh" => CultureInfo.GetCultureInfo("zh-CN"),
            "en" => CultureInfo.GetCultureInfo("en-US"),
            _ => CultureInfo.CurrentUICulture,
        };

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (_strings is not null)
        {
            app.Resources.MergedDictionaries.Remove(_strings);
        }

        _strings = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri($"avares://MoveBit/Assets/Strings.{(IsEnglish ? "en" : "zh")}.axaml"));
        app.Resources.MergedDictionaries.Add(_strings);
    }

    /// Look up a string resource; missing keys render as the key itself so a typo
    /// is visible in the UI instead of crashing.
    public static string T(string key, params object?[] args)
    {
        var template = key;
        if (Application.Current?.Resources.TryGetResource(key, null, out var value) == true && value is string s)
        {
            template = s;
        }

        return args.Length == 0 ? template : string.Format(Culture, template, args);
    }
}
