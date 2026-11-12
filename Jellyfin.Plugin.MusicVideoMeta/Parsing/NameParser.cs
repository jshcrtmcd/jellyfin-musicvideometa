using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MusicVideoMeta.Parsing;

/// <summary>
/// Parses artist, title, featured artists and year out of messy music video file names,
/// such as the ones yt-dlp and MeTube produce.
/// </summary>
public static class NameParser
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex _videoId = new(@"\s*[\[\(]([A-Za-z0-9_-]{11})[\]\)]\s*$", RegexOptions.Compiled);
    private static readonly Regex _bracket = new(@"[\(\[\{【]([^\(\)\[\]\{\}【】]*)[\)\]\}】]", RegexOptions.Compiled);
    private static readonly Regex _featPrefix = new(@"^\s*(?:feat\.?|ft\.?|featuring|with)\s+(.+)$", Opts);
    private static readonly Regex _featInline = new(@"\s+(?:feat\.?|ft\.?|featuring)\s+(.+)$", Opts);
    private static readonly Regex _year = new(@"^(19[5-9]\d|20\d\d)$", RegexOptions.Compiled);
    private static readonly Regex _separator = new(@"\s+[-–—~]+\s+|\s*\|\s*|\s+//\s+|\s*[-–—]{2,}\s*", RegexOptions.Compiled);
    private static readonly Regex _trackNumber = new(@"^\d{1,2}(?:\.\s+|\)\s*)(?=\S)", RegexOptions.Compiled);
    private static readonly Regex _artistJoiner = new(@"\s*,\s*|\s+&\s+|\s+and\s+|\s+x\s+|\s+with\s+|\s+vs\.?\s+|\s+(?:feat\.?|ft\.?|featuring)\s+|\s*\+\s*", Opts);
    private static readonly Regex _tokenSplit = new(@"[\s/\-,:;|.!_]+", RegexOptions.Compiled);
    private static readonly Regex _spaces = new(@"\s{2,}", RegexOptions.Compiled);

    // Words that only describe the upload, never the song.
    private static readonly HashSet<string> _junkWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "official", "officiel", "officielle", "oficial", "music", "musical", "musique", "video", "vídeo", "vidéo",
        "videoclip", "clip", "audio", "lyric", "lyrics", "letra", "visualizer", "visualiser", "visual",
        "hd", "hq", "sd", "4k", "8k", "uhd", "fhd", "1080p", "720p", "480p", "1440p", "2160p", "60fps", "remaster",
        "remastered", "explicit", "uncensored", "censored", "dirty", "clean", "mv", "vevo", "premiere",
        "directed", "dir", "upscaled", "restored", "hdr", "fullhd", "color", "coded", "colour", "eng", "sub",
        "subs", "kor", "rom", "han", "out", "now", "new", "the", "by", "a", "an", "in", "of", "on", "with", "and",
        "version", "ver", "full", "song", "track", "single", "teaser", "trailer", "performance", "promo",
    };

    // Junk words strong enough to mark a bracket or a trailing run as junk.
    private static readonly HashSet<string> _strongJunk = new(StringComparer.OrdinalIgnoreCase)
    {
        "official", "officiel", "officielle", "oficial", "video", "vídeo", "vidéo", "videoclip", "audio", "lyric",
        "lyrics", "letra", "visualizer", "visualiser", "hd", "hq", "4k", "8k", "uhd", "fhd", "1080p", "720p", "480p",
        "1440p", "2160p", "60fps", "remaster", "remastered", "explicit", "uncensored", "censored", "dirty", "clean",
        "mv", "vevo", "premiere", "directed", "upscaled", "restored", "hdr", "fullhd",
    };

    // A trailing run of junk words is only cut when it contains one of these, so titles like "Come Clean" survive.
    private static readonly HashSet<string> _trailingTrigger = new(StringComparer.OrdinalIgnoreCase)
    {
        "official", "officiel", "officielle", "oficial", "lyric", "lyrics", "letra", "visualizer", "visualiser",
        "hd", "hq", "4k", "8k", "uhd", "fhd", "1080p", "720p", "480p", "1440p", "2160p", "60fps", "vevo", "mv",
    };

    // Words that describe the recording itself and are worth keeping in the title.
    private static readonly HashSet<string> _keepWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "live", "remix", "rmx", "acoustic", "unplugged", "cover", "part", "pt", "edit", "mix", "freestyle",
        "interlude", "intro", "outro", "extended", "instrumental", "acapella", "reprise", "demo", "radio",
        "session", "sessions", "medley", "mashup", "bootleg", "flip", "dub", "rework", "vip", "slowed", "reverb",
        "sped", "chopped", "screwed", "pt.", "vol", "volume", "ii", "iii", "iv",
    };

    // Suffixes YouTube channel names add to an artist's name, e.g. "kevingatesTV" or "DrakeOfficial".
    private static readonly string[] _channelSuffixes = ["tv", "music", "official", "vevo", "channel", "hd", "videos", "records", "ent"];

    private static readonly (char From, char To)[] _fullWidth =
    [
        ('｜', '|'), ('：', ':'), ('⧸', '/'), ('／', '/'), ('？', '?'), ('＂', '"'), ('＊', '*'), ('＜', '<'),
        ('＞', '>'), ('＼', '\\'), ('＃', '#'), ('＆', '&'), ('＇', '\''), ('＂', '"'), ('“', '"'), ('”', '"'),
        ('‘', '\''), ('’', '\''), (' ', ' '),
    ];

    /// <summary>
    /// Parse a file name (without extension).
    /// </summary>
    /// <param name="fileName">File name without extension.</param>
    /// <param name="folderArtist">The artist from the first folder, if the file is inside one.</param>
    /// <param name="isKnownArtist">Optional check for names that are artists in the music library, used to keep
    /// names like "Earth, Wind &amp; Fire" in one piece and to decide which side of "A - B" is the artist.</param>
    /// <returns>Parsed name.</returns>
    public static ParsedName Parse(string fileName, string? folderArtist, Func<string, bool>? isKnownArtist = null)
    {
        isKnownArtist ??= _ => false;
        var result = new ParsedName();
        var featured = new List<string>();

        var s = FixCharacters(fileName);
        s = _videoId.Replace(s, m => LooksLikeVideoId(m.Groups[1].Value) ? string.Empty : m.Value);
        s = StripBrackets(s, featured, result);
        s = Tidy(s);
        s = _trackNumber.Replace(s, string.Empty);

        var segments = _separator.Split(s)
            .Select(Tidy)
            .Where(seg => seg.Length > 0 && !IsAllJunk(seg))
            .ToList();

        if (segments.Count == 0)
        {
            segments.Add(Tidy(s));
        }

        var folderKey = NameNormalizer.ArtistKey(folderArtist is null ? null : NameNormalizer.CleanArtistName(folderArtist));
        string? artistSide = null;
        string title;

        if (segments.Count == 1)
        {
            title = segments[0];
        }
        else if (folderKey.Length > 0)
        {
            // The folder artist's side: the first part may list several artists ("A x B", "A, B"),
            // later parts only count when they start with the artist, so "Title ft. Artist" stays a title.
            var idx = -1;
            for (var i = 0; i < segments.Count && idx < 0; i++)
            {
                var matches = i == 0
                    ? SideHasArtist(segments[i], folderKey, isKnownArtist)
                    : KeyOf(FirstArtist(segments[i], isKnownArtist)) == folderKey;
                if (matches)
                {
                    idx = i;
                }
            }

            if (idx < 0 && FirstArtist(segments[0], isKnownArtist) is { } named && IsChannelOf(folderKey, KeyOf(named)))
            {
                // Folder is a channel name ("kevingatesTV") and the file names the real artist ("Kevin Gates - ...").
                idx = 0;
                result.Artist = NameNormalizer.CleanArtistName(named);
                folderKey = KeyOf(named);
            }

            if (idx < 0 && FirstArtist(segments[0], isKnownArtist) is { } first0 && isKnownArtist(first0))
            {
                // Another library artist comes first, e.g. a guest verse filed under the main artist.
                idx = 0;
            }

            if (idx >= 0)
            {
                artistSide = segments[idx];
                title = string.Join(" - ", segments.Where((_, i) => i != idx));
            }
            else
            {
                // Can't tell which part is the artist: keep the whole name as the title.
                title = string.Join(" - ", segments);
            }
        }
        else
        {
            artistSide = segments[0];
            title = string.Join(" - ", segments.Skip(1));
        }

        if (artistSide is not null)
        {
            var sideArtists = SplitArtists(artistSide, isKnownArtist);
            if ((folderKey.Length == 0 || result.Artist is not null) && sideArtists.Count > 0)
            {
                result.Artist = NameNormalizer.CleanArtistName(sideArtists[0]);
                sideArtists.RemoveAt(0);
            }

            featured.InsertRange(0, sideArtists);
        }

        var inline = _featInline.Match(title);
        if (inline.Success)
        {
            featured.AddRange(SplitArtists(inline.Groups[1].Value, isKnownArtist));
            title = title[..inline.Index];
        }

        title = StripTrailingJunk(Tidy(title));
        title = title.Trim('"', '\'', ' ', '-', '_', ',', '.', ':');
        if (title.Length == 0)
        {
            title = Tidy(FixCharacters(fileName));
        }

        result.Title = title;

        var primaryKey = result.Artist is not null ? NameNormalizer.ArtistKey(result.Artist) : folderKey;
        foreach (var name in featured.Select(f => NameNormalizer.CleanArtistName(Tidy(f))))
        {
            var key = NameNormalizer.ArtistKey(name);
            if (key.Length == 0 || key == primaryKey)
            {
                continue;
            }

            if (!result.FeaturedArtists.Any(f => NameNormalizer.ArtistKey(f) == key))
            {
                result.FeaturedArtists.Add(name);
            }
        }

        return result;
    }

    /// <summary>
    /// Split "A, B &amp; C" into artists, keeping names the library knows (like "Earth, Wind &amp; Fire") whole.
    /// </summary>
    /// <param name="text">Text.</param>
    /// <param name="isKnownArtist">Library check.</param>
    /// <returns>Artists.</returns>
    public static List<string> SplitArtists(string text, Func<string, bool> isKnownArtist)
    {
        text = Tidy(text);
        if (text.Length == 0)
        {
            return [];
        }

        if (isKnownArtist(text))
        {
            return [text];
        }

        // Split on "feat." first, then try each side whole before splitting it further.
        var parts = new List<string>();
        foreach (var part in Regex.Split(text, @"\s+(?:feat\.?|ft\.?|featuring)\s+", RegexOptions.IgnoreCase))
        {
            var p = Tidy(part);
            if (p.Length == 0)
            {
                continue;
            }

            if (isKnownArtist(p))
            {
                parts.Add(p);
            }
            else
            {
                parts.AddRange(_artistJoiner.Split(p).Select(Tidy).Where(x => x.Length > 0));
            }
        }

        return parts;
    }

    private static bool IsChannelOf(string folderKey, string artistKey)
        => artistKey.Length >= 3
            && folderKey.Length > artistKey.Length
            && folderKey.StartsWith(artistKey, StringComparison.Ordinal)
            && _channelSuffixes.Contains(folderKey[artistKey.Length..], StringComparer.Ordinal);

    private static string KeyOf(string? artist)
        => NameNormalizer.ArtistKey(artist is null ? null : NameNormalizer.CleanArtistName(artist));

    private static string? FirstArtist(string side, Func<string, bool> isKnownArtist)
        => SplitArtists(side, isKnownArtist).FirstOrDefault();

    private static bool SideHasArtist(string side, string folderKey, Func<string, bool> isKnownArtist)
    {
        if (NameNormalizer.ArtistKey(NameNormalizer.CleanArtistName(side)) == folderKey)
        {
            return true;
        }

        return SplitArtists(side, isKnownArtist)
            .Any(a => NameNormalizer.ArtistKey(NameNormalizer.CleanArtistName(a)) == folderKey);
    }

    private static string StripBrackets(string s, List<string> featured, ParsedName result)
    {
        // Repeat so nested or adjacent brackets are all handled.
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            s = _bracket.Replace(s, m =>
            {
                changed = true;
                var inner = Tidy(m.Groups[1].Value);
                if (inner.Length == 0)
                {
                    return " ";
                }

                var feat = _featPrefix.Match(inner);
                if (feat.Success && !_tokenSplit.Split(feat.Groups[1].Value).Any(t => _strongJunk.Contains(t)))
                {
                    featured.Add(feat.Groups[1].Value);
                    return " ";
                }

                if (_year.IsMatch(inner))
                {
                    result.Year ??= int.Parse(inner, CultureInfo.InvariantCulture);
                    return " ";
                }

                var kept = CleanBracket(inner, result);
                return kept is null ? " " : " \u0001" + kept + "\u0002 ";
            });

            if (!changed)
            {
                break;
            }
        }

        // Restore the brackets that were kept.
        return s.Replace('\u0001', '(').Replace('\u0002', ')');
    }

    /// <summary>
    /// Returns the bracket text to keep, or null to drop it.
    /// </summary>
    private static string? CleanBracket(string inner, ParsedName result)
    {
        var normalized = Regex.Replace(inner, @"\bm/v\b", "mv", RegexOptions.IgnoreCase);
        var tokens = _tokenSplit.Split(normalized).Where(t => t.Length > 0).ToList();
        if (!tokens.Any(t => _strongJunk.Contains(t)))
        {
            return inner;
        }

        foreach (var t in tokens)
        {
            if (_year.IsMatch(t))
            {
                result.Year ??= int.Parse(t, CultureInfo.InvariantCulture);
            }
        }

        // "(Official Live Video)" keeps "(Live)"; "(Official Music Video)" is dropped.
        var meaningful = tokens.Where(t => !_junkWords.Contains(t) && !_year.IsMatch(t)).ToList();
        return meaningful.Any(t => _keepWords.Contains(t)) ? string.Join(' ', meaningful) : null;
    }

    private static bool IsAllJunk(string segment)
    {
        var tokens = _tokenSplit.Split(segment).Where(t => t.Length > 0).ToList();
        return tokens.Count > 0
            && tokens.Any(t => _strongJunk.Contains(t))
            && tokens.All(t => _junkWords.Contains(t) || _year.IsMatch(t));
    }

    private static string StripTrailingJunk(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var cut = words.Count;
        var sawStrong = false;
        while (cut > 1)
        {
            var w = words[cut - 1].Trim('|', '-', ',', '.', ':', '!');
            if (w.Length == 0 || _junkWords.Contains(w))
            {
                sawStrong |= _trailingTrigger.Contains(w);
                cut--;
            }
            else
            {
                break;
            }
        }

        if (!sawStrong || cut == words.Count)
        {
            return title;
        }

        return string.Join(' ', words.Take(cut)).TrimEnd('|', '-', ',', ':', ' ');
    }

    private static bool LooksLikeVideoId(string id)
    {
        // YouTube IDs are 11 characters of mixed case, digits, '-' or '_'; skip plain words like "(Unplugged)".
        var hasDigitOrSymbol = id.Any(c => char.IsDigit(c) || c == '-' || c == '_');
        var innerUpper = id.Skip(1).Any(char.IsUpper);
        var hasLower = id.Any(char.IsLower);
        return hasDigitOrSymbol || (innerUpper && hasLower);
    }

    private static string FixCharacters(string s)
    {
        var sb = new StringBuilder(s);
        foreach (var (from, to) in _fullWidth)
        {
            sb.Replace(from, to);
        }

        var fixedText = sb.ToString();
        if (!fixedText.Contains(' ', StringComparison.Ordinal) && fixedText.Contains('_', StringComparison.Ordinal))
        {
            fixedText = fixedText.Replace('_', ' ');
        }

        return fixedText;
    }

    private static string Tidy(string s) => _spaces.Replace(s.Replace('\t', ' '), " ").Trim();
}
