using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TileStart.Host.Updates;

internal sealed record UpdateReleaseManifest(Version? BaseVersion, IReadOnlyDictionary<string, string> Baseline)
{
    private const string Header = "TileStart.Update.v1";

    internal static UpdateReleaseManifest Parse(string text)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine() != Header || reader.ReadLine() is not { } versionText)
        {
            throw new InvalidDataException("无法识别 TileStart 更新清单。");
        }

        Version? baselineVersion = null;
        if (versionText != "-")
        {
            if (!Version.TryParse(versionText, out baselineVersion) || baselineVersion.Build < 0
                || baselineVersion.Revision != -1 || baselineVersion.ToString(3) != versionText)
            {
                throw new InvalidDataException("更新清单的基线版本无效。");
            }
        }

        var baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 67 || line[64..66] != " *" || !line[..64].All(Uri.IsHexDigit)
                || !IsPackagePath(line[66..]) || !baseline.TryAdd(line[66..], line[..64]))
            {
                throw new InvalidDataException("更新清单中存在无效或重复的文件记录。");
            }
        }

        if (baselineVersion is null ? baseline.Count != 0 : !baseline.ContainsKey("TileStart.Host.dll"))
        {
            throw new InvalidDataException("更新清单缺少有效的基线文件记录。");
        }

        return new UpdateReleaseManifest(baselineVersion, baseline);
    }

    internal async Task<bool> MatchesAsync(Version currentVersion, string directory, CancellationToken cancellationToken)
    {
        if (BaseVersion is null || BaseVersion.ToString(3) != currentVersion.ToString(3))
        {
            return false;
        }

        // Check the Host first so development builds with the same numeric version fall back quickly.
        foreach (var (path, expected) in Baseline.OrderByDescending(file =>
                     file.Key.Equals("TileStart.Host.dll", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                await using var stream = File.OpenRead(file);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPackagePath(string path) =>
        Regex.IsMatch(path, @"\A[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\z")
        && path.Split('/').All(segment => !segment.EndsWith('.'));
}
