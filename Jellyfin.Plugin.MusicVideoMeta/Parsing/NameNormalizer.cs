using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MusicVideoMeta.Parsing;

/// <summary>
/// Turns names into comparison keys.
/// </summary>
public static class NameNormalizer
{
    private static readonly Regex _leadingThe = new(@"^\s*the\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _channelSuffix = new(@"(?:\s*-\s*topic|vevo|\s+official(?:\s+channel)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _bracketed = new(@"\s*[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);

    /// <summary>
    /// Lower case, no accents, "&amp;" as "and", "$" as "s", letters and digits only.
    /// </summary>
    /// <param name="name">Name.</param>
    /// <returns>Key.</returns>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var decomposed = name
            .Replace("&", " and ", StringComparison.Ordinal)
            .Replace('$', 's')
            .Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Key for an artist name: like <see cref="Normalize"/>, without a leading "The".
    /// </summary>
    /// <param name="name">Artist name.</param>
    /// <returns>Key.</returns>
    public static string ArtistKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var key = Normalize(_leadingThe.Replace(name, string.Empty));
        return key.Length > 0 ? key : Normalize(name);
    }

    /// <summary>
    /// Key for a song title, ignoring bracketed parts such as "(feat. X)" or "(Remastered)".
    /// </summary>
    /// <param name="title">Title.</param>
    /// <returns>Key.</returns>
    public static string TitleKey(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var key = Normalize(_bracketed.Replace(title, string.Empty));
        return key.Length > 0 ? key : Normalize(title);
    }

    /// <summary>
    /// Remove YouTube channel decorations such as "VEVO" or " - Topic" from an artist name.
    /// </summary>
    /// <param name="name">Name.</param>
    /// <returns>Cleaned name.</returns>
    public static string CleanArtistName(string name)
    {
        var cleaned = _channelSuffix.Replace(name.Trim(), string.Empty).Trim();
        return cleaned.Length > 0 ? cleaned : name.Trim();
    }
}
