using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixlReplay.Settings;

public static class SettingsStore
{
    /// <summary>%AppData%\PixlReplay; PIXLREPLAY_DATA points a development run (renders of the project page) at another folder.</summary>
    public static string AppDataDir { get; } = Environment.GetEnvironmentVariable("PIXLREPLAY_DATA") is { Length: > 0 } dev
        ? dev
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);

    public static string DefaultPath { get; } = Path.Combine(AppDataDir, "settings.json");

    static readonly JsonSerializerOptions Options = CreateOptions();

    static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        o.Converters.Add(new TolerantEnumConverterFactory());
        return o;
    }

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        if (!File.Exists(path))
        {
            settings = new AppSettings();
            settings.General.Language = AppSettings.DefaultLanguage(System.Globalization.CultureInfo.CurrentUICulture);
        }
        else
        {
            string json = ReadShared(path);
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
            catch (JsonException)
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "settings.bad.json"), json);
                settings = new AppSettings();
            }
        }

        settings.Normalize();
        return settings;
    }

    /// <summary>
    /// Reads without blocking a concurrent save (which replaces the file) and retries briefly while
    /// something else (an antivirus scan, the other PixlReplay process) holds it. Falling back to defaults here
    /// would let the next Apply overwrite the user's real settings, so a lasting lock still throws.
    /// </summary>
    static string ReadShared(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 20 && File.Exists(path))
            {
                Thread.Sleep(50);
            }
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    public static AppSettings Deserialize(string json)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        s.Normalize();
        return s;
    }

    /// <summary>Reads enums as strings and maps unknown names to the enum's first member.</summary>
    sealed class TolerantEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type t) => t.IsEnum;

        public override JsonConverter CreateConverter(Type t, JsonSerializerOptions o) =>
            (JsonConverter)Activator.CreateInstance(typeof(TolerantEnumConverter<>).MakeGenericType(t))!;
    }

    sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: true, out T value) &&
                Enum.IsDefined(value))
                return value;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int n) &&
                Enum.IsDefined(typeof(T), n))
                return (T)Enum.ToObject(typeof(T), n);
            return default;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
