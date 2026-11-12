using System.Globalization;

namespace MUTEit.Core.Localization;

/// <summary>
/// Strings for tray and settings window. English is the default, German optional ("auto" picks German when the
/// Windows display language is German).
/// </summary>
public static class Loc
{
    private static string _language = Resolve("auto");

    public static event Action? Changed;

    /// <summary>"en" or "de".</summary>
    public static string Language => _language;

    public static CultureInfo Culture => _language == "de" ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-GB");

    public static void SetLanguage(string setting)
    {
        var resolved = Resolve(setting);
        if (resolved == _language)
        {
            return;
        }

        _language = resolved;
        Changed?.Invoke();
    }

    public static string Resolve(string setting) => setting switch
    {
        "de" => "de",
        "en" => "en",
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en",
    };

    public static string T(string key) =>
        Strings.All.TryGetValue(key, out var s) ? (_language == "de" ? s.De : s.En) : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>Short local time (e.g. 21:00).</summary>
    public static string Time(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, TimeZoneInfo.Local).ToString("HH:mm", Culture);
}
