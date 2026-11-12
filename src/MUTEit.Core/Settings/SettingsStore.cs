using System.Text.Json;
using System.Text.Json.Serialization;

namespace MUTEit.Core.Settings;

/// <summary>
/// Loads and saves <see cref="MuteitSettings"/>.
/// Location: <c>%AppData%\MUTEit\settings.json</c>; portable mode (file <c>MUTEit.portable</c> next to the EXE):
/// <c>data\settings.json</c> next to the EXE.
/// Tray and settings window run as separate processes of the same EXE, so every change is a read-modify-write
/// under a named mutex (<see cref="Update"/>): each side only changes the values it owns.
/// </summary>
public sealed class SettingsStore
{
    public const string PortableMarker = "MUTEit.portable";
    private const string MutexName = @"Local\MUTEit.SettingsFile";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory();
        FilePath = Path.Combine(Directory, "settings.json");
    }

    public string Directory { get; }

    public string FilePath { get; }

    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    public static string DefaultDirectory() => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MUTEit");

    public MuteitSettings Load() => WithLock(LoadUnlocked);

    public static string Serialize(MuteitSettings settings) => JsonSerializer.Serialize(settings, Json);

    /// <summary>Parses settings JSON (import); throws <see cref="JsonException"/> / <see cref="FormatException"/> on invalid content.</summary>
    public static MuteitSettings Parse(string json) =>
        (JsonSerializer.Deserialize<MuteitSettings>(json, Json) ?? throw new FormatException("empty settings")).Normalize();

    /// <summary>
    /// Export file: everything except transient state (overlay position, update check bookkeeping).
    /// </summary>
    public static string Export(MuteitSettings settings)
    {
        var copy = settings.Clone();
        copy.OverlayX = null;
        copy.OverlayY = null;
        copy.LastUpdateCheck = null;
        copy.NotifiedVersion = null;
        copy.LatestVersion = null;
        copy.LatestReleaseUrl = null;
        return Serialize(copy);
    }

    /// <summary>Replaces the settings with an imported file (keeps the autostart marker and the overlay position).</summary>
    public MuteitSettings Import(string json)
    {
        var imported = Parse(json);
        return WithLock(() =>
        {
            var current = LoadUnlocked();
            imported.AutostartInitialized = current.AutostartInitialized;
            imported.OverlayX = current.OverlayX;
            imported.OverlayY = current.OverlayY;
            imported.Normalize();
            SaveUnlocked(imported);
            return imported;
        });
    }

    /// <summary>Back to defaults; "Start with Windows" (registry) is not touched and not switched on again.</summary>
    public MuteitSettings Reset() => WithLock(() =>
    {
        var fresh = new MuteitSettings { AutostartInitialized = true }.Normalize();
        SaveUnlocked(fresh);
        return fresh;
    });

    /// <summary>Reads the current file, applies <paramref name="change"/> and writes it back – atomically for both processes.</summary>
    public MuteitSettings Update(Action<MuteitSettings> change) => WithLock(() =>
    {
        var settings = LoadUnlocked();
        change(settings);
        settings.Normalize();
        SaveUnlocked(settings);
        return settings;
    });

    private MuteitSettings LoadUnlocked()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<MuteitSettings>(json, Json);
                if (loaded is not null)
                {
                    return loaded.Normalize();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Unreadable or damaged file: start with defaults, keep a copy for the user.
            TryBackupDamaged();
        }

        return new MuteitSettings().Normalize();
    }

    private void SaveUnlocked(MuteitSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }

    private void TryBackupDamaged()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, FilePath + ".damaged", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private static T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, MutexName);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(3));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            return action();
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
