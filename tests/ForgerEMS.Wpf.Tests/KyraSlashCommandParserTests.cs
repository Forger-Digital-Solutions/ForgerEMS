using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraSlashCommandParserTests
{
    [Fact]
    public void Parse_Help_Matches()
    {
        var r = KyraSlashCommandParser.Parse("/help");
        Assert.True(r.IsSlashCommand);
        Assert.Equal("help", r.MatchedCommand!.Name);
    }

    [Fact]
    public void Parse_DiagnoseLag_SplitsArguments()
    {
        var r = KyraSlashCommandParser.Parse("/diagnose lag");
        Assert.True(r.IsSlashCommand);
        Assert.Equal("diagnose", r.MatchedCommand!.Name);
        Assert.Equal("lag", r.Arguments);
    }

    [Fact]
    public void Parse_ListingFacebook()
    {
        var r = KyraSlashCommandParser.Parse("/listing facebook");
        Assert.Equal("listing", r.MatchedCommand!.Name);
        Assert.Equal("facebook", r.Arguments);
    }

    [Fact]
    public void Parse_WeatherZip()
    {
        var r = KyraSlashCommandParser.Parse("/weather 11710");
        Assert.Equal("weather", r.MatchedCommand!.Name);
        Assert.Equal("11710", r.Arguments);
    }

    [Fact]
    public void Parse_CryptoBtc()
    {
        var r = KyraSlashCommandParser.Parse("/crypto BTC");
        Assert.Equal("crypto", r.MatchedCommand!.Name);
    }

    [Fact]
    public void Parse_StocksNvda()
    {
        var r = KyraSlashCommandParser.Parse("/stocks NVDA");
        Assert.Equal("stocks", r.MatchedCommand!.Name);
    }

    [Fact]
    public void Parse_MemoryView()
    {
        var r = KyraSlashCommandParser.Parse("/memory view");
        Assert.Equal("memory", r.MatchedCommand!.Name);
        Assert.StartsWith("view", r.Arguments, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_Unknown_SuggestsHelp()
    {
        var r = KyraSlashCommandParser.Parse("/notreal");
        Assert.True(r.IsSlashCommand);
        Assert.Null(r.MatchedCommand);
        Assert.Contains("know", r.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/help", string.Join(',', r.SuggestedCommands), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindClosest_SuggestsDiagnose()
    {
        var closest = KyraSlashCommandParser.FindClosestCommands("diagno", 5);
        Assert.Contains("diagnose", closest, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EmptySlash_SuggestsCommands()
    {
        var r = KyraSlashCommandParser.Parse("/");
        Assert.True(r.IsSlashCommand);
        Assert.NotEmpty(r.SuggestedCommands);
    }

    [Fact]
    public void Parse_AliasDiag()
    {
        var r = KyraSlashCommandParser.Parse("/diag");
        Assert.Equal("diagnose", r.MatchedCommand!.Name);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var r = KyraSlashCommandParser.Parse("/HELP");
        Assert.Equal("help", r.MatchedCommand!.Name);
    }

    [Fact]
    public void Parse_NonSlash_NotCommand()
    {
        var r = KyraSlashCommandParser.Parse("hello");
        Assert.False(r.IsSlashCommand);
    }
}
