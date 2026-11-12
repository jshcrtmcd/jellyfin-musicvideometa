using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MusicVideoMeta.Parsing;
using Xunit;

namespace Jellyfin.Plugin.MusicVideoMeta.Tests;

public class NameParserTests
{
    private static readonly HashSet<string> _library = new(
        new[] { "Drake", "Rihanna", "Earth, Wind & Fire", "JAY-Z", "50 Cent", "The Weeknd", "Beyoncé", "Future", "Hilary Duff" }
            .Select(NameNormalizer.ArtistKey),
        StringComparer.Ordinal);

    private static bool Known(string name) => _library.Contains(NameNormalizer.ArtistKey(name));

    [Theory]
    [InlineData("Drake", "Drake - Hotline Bling", "Hotline Bling")]
    [InlineData("Drake", "Drake - Hotline Bling (Official Video) [uxpDa-c-4Mc]", "Hotline Bling")]
    [InlineData("Drake", "Drake ｜ Hotline Bling ｜ Official Music Video", "Hotline Bling")]
    [InlineData("Drake", "Hotline Bling (Official Live Video)", "Hotline Bling (Live)")]
    [InlineData("Drake", "Hotline Bling Official Video HD", "Hotline Bling")]
    [InlineData("Drake", "Hotline Bling - Remix", "Hotline Bling - Remix")]
    [InlineData("Drake", "Drake_-_Hotline_Bling", "Hotline Bling")]
    [InlineData("Drake", "DrakeVEVO - Hotline Bling", "Hotline Bling")]
    [InlineData("Drake", "01. Hotline Bling", "Hotline Bling")]
    [InlineData("Drake", "Drake - Hotline Bling (Lyrics)", "Hotline Bling")]
    [InlineData("Drake", "Drake - Hotline Bling [4K Remastered]", "Hotline Bling")]
    [InlineData("Drake", "Drake - Hotline Bling (With Lyrics)", "Hotline Bling")]
    [InlineData("Hilary Duff", "Come Clean", "Come Clean")]
    [InlineData("50 Cent", "50 Cent - In Da Club (Official Music Video)", "In Da Club")]
    [InlineData("Beyoncé", "Beyonce - Halo", "Halo")]
    [InlineData("Earth, Wind & Fire", "Earth, Wind & Fire - September (Official HD Video)", "September")]
    [InlineData("Rihanna", "Rihanna - Umbrella (Orange Version) ft. JAY-Z", "Umbrella (Orange Version)")]
    public void Title(string folder, string file, string expected)
    {
        Assert.Equal(expected, NameParser.Parse(file, folder, Known).Title);
    }

    [Fact]
    public void FeaturedFromBrackets()
    {
        var p = NameParser.Parse("Work (feat. Drake)", "Rihanna", Known);
        Assert.Equal("Work", p.Title);
        Assert.Equal(["Drake"], p.FeaturedArtists);
    }

    [Fact]
    public void FeaturedInline()
    {
        var p = NameParser.Parse("Rihanna - Work (Explicit) ft. Drake", "Rihanna", Known);
        Assert.Equal("Work", p.Title);
        Assert.Equal(["Drake"], p.FeaturedArtists);
    }

    [Fact]
    public void FeaturedFromArtistSide()
    {
        var p = NameParser.Parse("Drake x Future - Jumpman", "Drake", Known);
        Assert.Equal("Jumpman", p.Title);
        Assert.Equal(["Future"], p.FeaturedArtists);
    }

    [Fact]
    public void GuestFiledUnderFeaturedArtist()
    {
        var p = NameParser.Parse("Rihanna - Umbrella ft. JAY-Z", "JAY-Z", Known);
        Assert.Equal("Umbrella", p.Title);
        Assert.Equal(["Rihanna"], p.FeaturedArtists);
    }

    [Fact]
    public void KnownArtistWithCommaStaysWhole()
    {
        var p = NameParser.Parse("Earth, Wind & Fire - September", "Earth, Wind & Fire", Known);
        Assert.Empty(p.FeaturedArtists);
    }

    [Fact]
    public void ArtistAfterTitleAndYear()
    {
        var p = NameParser.Parse("Hotline Bling - Drake (2015)", "Drake", Known);
        Assert.Equal("Hotline Bling", p.Title);
        Assert.Equal(2015, p.Year);
    }

    [Fact]
    public void YearInsideJunkBracket()
    {
        var p = NameParser.Parse("Drake - Hotline Bling (Official Video 2015)", "Drake", Known);
        Assert.Equal("Hotline Bling", p.Title);
        Assert.Equal(2015, p.Year);
    }

    [Fact]
    public void NoFolderUsesFileNameArtist()
    {
        var p = NameParser.Parse("The Weeknd - Blinding Lights (Official Video)", null, Known);
        Assert.Equal("The Weeknd", p.Artist);
        Assert.Equal("Blinding Lights", p.Title);
    }

    [Theory]
    [InlineData("The Weeknd", "Weeknd")]
    [InlineData("Beyoncé", "BEYONCE")]
    [InlineData("A$AP Rocky", "ASAP Rocky")]
    [InlineData("Simon & Garfunkel", "Simon and Garfunkel")]
    [InlineData("DrakeVEVO", "Drake")]
    public void ArtistKeysMatch(string a, string b)
    {
        Assert.Equal(
            NameNormalizer.ArtistKey(NameNormalizer.CleanArtistName(a)),
            NameNormalizer.ArtistKey(NameNormalizer.CleanArtistName(b)));
    }
}
