using System.IO;
using System.Text.Json;
using TileStart.Host.Utilities;

namespace TileStart.Host.Updates;

internal sealed record AutomaticUpdatePreferences(
    bool Enabled = false,
    string? LastAttemptVersion = null,
    DateTimeOffset? LastAttemptUtc = null)
{
    internal bool WasAttemptedRecently(Version version, DateTimeOffset now) =>
        LastAttemptVersion == version.ToString(3) && LastAttemptUtc is { } last && last <= now
        && now - last < TimeSpan.FromHours(24);

    internal static AutomaticUpdatePreferences Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AutomaticUpdatePreferences>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticLog.Write($"Unable to read automatic-update preferences: {exception.Message}");
            return new();
        }
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
