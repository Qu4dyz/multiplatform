using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using Xunit;

namespace GameAnalytics.Tests;

public class SpellRuneHelperTests
{
    [Theory]
    [InlineData("Lux", 8369, 21)]
    [InlineData("Volibear", 8005, 12)]
    [InlineData("Yasuo", 8008, 12)]
    [InlineData("Ahri", 8112, 14)]
    [InlineData("Jinx", 8008, 7)]
    [InlineData("Ezreal", 8005, 12)]
    [InlineData("Kai'Sa", 8008, 6)]
    [InlineData("Kennen", 8230, 12)]
    [InlineData("Fiddlesticks", 8369, 11)]
    [InlineData("Zed", 8112, 14)]
    [InlineData("Orianna", 8369, 12)]
    [InlineData("Malphite", 8229, 12)]
    [InlineData("Thresh", 8439, 14)]
    [InlineData("Blitzcrank", 8351, 14)]
    [InlineData("Lulu", 8214, 3)]
    [InlineData("Teemo", 8005, 14)]
    [InlineData(" Lee Sin ", 8010, 11)]
    [InlineData("Graves", 8005, 11)]
    [InlineData("Hecarim", 8230, 11)]
    public void Recommendation_UsesChampionSignature(string champion, int keystone, int secondSpell)
    {
        var rec = SpellRuneHelper.GetRecommendedRunesAndSpells(champion, Position.Top);

        Assert.Equal(keystone, rec.PrimaryRune);
        Assert.Equal(secondSpell, rec.Spell2);
    }

    [Theory]
    [InlineData(Position.Jungle, 8010, 11)]
    [InlineData(Position.Bottom, 8008, 7)]
    [InlineData(Position.Utility, 8439, 14)]
    [InlineData(Position.Middle, 8112, 14)]
    [InlineData(Position.Top, 8010, 12)]
    public void Recommendation_FallsBackToRole(Position position, int keystone, int secondSpell)
    {
        var rec = SpellRuneHelper.GetRecommendedRunesAndSpells("UnknownChampion", position);

        Assert.Equal(keystone, rec.PrimaryRune);
        Assert.Equal(secondSpell, rec.Spell2);
    }

    [Fact]
    public void ResolveRuneIds_KeepsRealIdsAndFillsOnlyMissingOnes()
    {
        Assert.Equal((8128, 8300), SpellRuneHelper.ResolveRuneIds(8128, 8300, "Ahri", Position.Middle));
        Assert.Equal((8112, 8200), SpellRuneHelper.ResolveRuneIds(0, 0, "Ahri", Position.Middle));
        Assert.Equal((8128, 8200), SpellRuneHelper.ResolveRuneIds(8128, 0, "Ahri", Position.Middle));
    }

    [Theory]
    [InlineData(1, "Cleanse")]
    [InlineData(3, "Exhaust")]
    [InlineData(4, "Flash")]
    [InlineData(6, "Ghost")]
    [InlineData(7, "Heal")]
    [InlineData(11, "Smite")]
    [InlineData(12, "Teleport")]
    [InlineData(13, "Clarity")]
    [InlineData(14, "Ignite")]
    [InlineData(21, "Barrier")]
    [InlineData(32, "Mark")]
    [InlineData(999, "Summoner Spell")]
    public void SpellName_MapsKnownIds(int id, string name) => Assert.Equal(name, SpellRuneHelper.GetSpellName(id));

    [Fact]
    public void ConquerorChampions_AreRecognisedIgnoringCase()
    {
        Assert.True(SpellRuneHelper.IsConquerorChampion(" Darius "));
        Assert.True(SpellRuneHelper.IsConquerorChampion("XIN ZHAO"));
        Assert.False(SpellRuneHelper.IsConquerorChampion("Lux"));
        Assert.False(SpellRuneHelper.IsConquerorChampion(null));
    }

    [Fact]
    public void StyleIcon_UnknownAndEmptyIds()
    {
        Assert.Contains("Precision", SpellRuneHelper.GetRuneStyleIconUrl(123456));
        Assert.Equal(string.Empty, SpellRuneHelper.GetRuneStyleIconUrl(0));
        Assert.Equal(string.Empty, SpellRuneHelper.GetRuneIconUrl(0));
    }
}
