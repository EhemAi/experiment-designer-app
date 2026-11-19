using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PoultryDesigner.AppCore;

public enum AppLanguage { English, SpanishSpain, SpanishMexico }

public static class LanguagePreference
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExperimentDesigner", "language.json");
    public static AppLanguage Load(string? path = null)
    {
        try
        {
            if (!File.Exists(path ?? DefaultPath)) return AppLanguage.English;
            using var json = JsonDocument.Parse(File.ReadAllText(path ?? DefaultPath));
            return AppText.FromCode(json.RootElement.GetProperty("Language").GetString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return AppLanguage.English; }
    }
    public static void Save(AppLanguage language, string? path = null)
    {
        string target = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { Language = AppText.CodeFor(language) }));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class AppText
{
    private sealed record Pattern(string Source, string Prefix, Regex Expression);
    private static readonly Dictionary<string, string> Spain = Read("es-ES");
    private static readonly Dictionary<string, string> Mexico = Read("es-MX");
    private static readonly Pattern[] Patterns = Spain.Keys.Where(k => Regex.IsMatch(k, @"\{\d+\}"))
        .OrderByDescending(k => Regex.Replace(k, @"\{\d+\}", "").Length)
        .Select(BuildPattern)
        .ToArray();
    private static readonly string[] Fragments = Spain.Keys.Where(k => k.Length >= 4 && k.Any(char.IsWhiteSpace) && !k.Contains('{') && (char.IsWhiteSpace(k[0]) || char.IsWhiteSpace(k[^1])))
        .OrderByDescending(k => k.Length).ToArray();
    public static AppLanguage Language { get; private set; } = AppLanguage.English;
    public static event EventHandler? Changed;
    public static string Code => CodeFor(Language);
    public static string CodeFor(AppLanguage language) => language switch { AppLanguage.SpanishSpain => "es-ES", AppLanguage.SpanishMexico => "es-MX", _ => "en" };
    public static AppLanguage FromCode(string? code) => code switch { "es-ES" => AppLanguage.SpanishSpain, "es-MX" => AppLanguage.SpanishMexico, _ => AppLanguage.English };
    public static IReadOnlyCollection<string> ResourceKeys => Spain.Keys;
    public static void Select(AppLanguage language, bool remember = true, string? preferencePath = null)
    {
        if (!Enum.IsDefined(language)) throw new ArgumentOutOfRangeException(nameof(language));
        if (remember) LanguagePreference.Save(language, preferencePath);
        if (Language == language) return;
        Language = language;
        Changed?.Invoke(null, EventArgs.Empty);
    }
    public static string T(string? source)
    {
        if (string.IsNullOrEmpty(source) || Language == AppLanguage.English) return source ?? "";
        string Lookup(string key) => Language == AppLanguage.SpanishMexico && Mexico.TryGetValue(key, out var mx) ? mx : Spain[key];
        if (Spain.ContainsKey(source)) return Lookup(source);
        foreach (var pattern in Patterns)
        {
            if (source.Contains('\n') && !pattern.Source.Contains('\n')) continue;
            if (!source.StartsWith(pattern.Prefix, StringComparison.Ordinal)) continue;
            var match = pattern.Expression.Match(source);
            if (!match.Success) continue;
            return Regex.Replace(Lookup(pattern.Source), @"\{(\d+)\}", m => Argument(pattern.Source, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups["p" + m.Groups[1].Value].Value, match));
        }
        if (source.Contains('\n')) return string.Join("\n", source.Replace("\r\n", "\n").Split('\n').Select(T));
        foreach (string fragment in Fragments)
        {
            if (source.StartsWith(fragment, StringComparison.Ordinal)) return Lookup(fragment) + T(source[fragment.Length..]);
            if (source.EndsWith(fragment, StringComparison.Ordinal)) return T(source[..^fragment.Length]) + Lookup(fragment);
        }
        return source;
    }
    private static string Argument(string source, int index, string value, Match match)
    {
        bool plural = source switch
        {
            "Prepared layout · {0}s {1}–{2} · No animals allocated" => index == 0,
            "{0} {1}s · {2} per replicate" or "{0} {1}s required" or "Choose exactly {0} {1}s, one per treatment in each replicate." => index == 1,
            "{0}s / treatment" => index == 0,
            "{0}  Source animals\n{1}  Selected\n{2}  {3}s required" or "{0}\nanimals in source pool\n\n{1}\nexplicitly selected\n\n{2}\n{3}s required" => index == 3,
            _ => false
        };
        if (plural)
        {
            int countIndex = source.StartsWith("{0}  Source", StringComparison.Ordinal) || source.StartsWith("{0}\nanimals", StringComparison.Ordinal) ? 2 : 0;
            bool singular = index != 0 && match.Groups["p" + countIndex].Value.Trim() == "1";
            return T(singular ? value : value + "s");
        }
        bool semantic = source switch
        {
            "{0}{1} chosen result. " => true,
            "{0} / {1} {2} / block {3}" => index == 1,
            "{0}Result {1} · {2}" => index is 0 or 2,
            "Result {0} · {1} · {2}" => index == 2,
            "Run {0}. " or "Run {0}: " or "Last run: {0}. " => index == 0,
            "Descriptive BW for each experimental {0}. SD is the sample SD of animal weights within the unit." => index == 0,
            "Row {0}: {1} — {2}" => index is 1 or 2,
            "{0}Valid records: {1}. Review the preview tab. "
                or "{0}Valid records: {1}. Review the preview tab. Heaviest animals will be selected automatically; unused animals become EX."
                or "{0}Valid records: {1}. Review the preview tab. Treatment order and source provenance are retained." => index == 0,
            "{0}: enter a whole number from {1} to {2}." => index == 0,
            _ => false
        };
        return semantic ? T(value) : value;
    }
    private static Pattern BuildPattern(string source)
    {
        string expression = string.Concat(Regex.Split(source, @"(\{\d+\})").Select(part =>
            Regex.IsMatch(part, @"^\{\d+\}$") ? "(?<p" + part[1..^1] + ">.*?)" : Regex.Escape(part)));
        return new(source, source[..source.IndexOf('{')], new Regex("\\A" + expression + "\\z",
            RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)));
    }
    private static Dictionary<string, string> Read(string code)
    {
        using var stream = typeof(AppText).Assembly.GetManifestResourceStream("PoultryDesigner.AppCore.Localization." + code + ".json")
            ?? throw new InvalidOperationException("Missing language resource: " + code);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Invalid language resource: " + code);
    }
}
