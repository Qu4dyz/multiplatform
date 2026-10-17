using CommunityToolkit.Mvvm.Messaging;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Helpers;
using GameAnalytics.Desktop.Converters;
using GameAnalytics.Desktop.ViewModels;
using GameAnalytics.Infrastructure.Services;

namespace GameAnalytics.Tests;

public class DataDragonAssetsTests
{
    [Theory]
    [InlineData("FiddleSticks", "Fiddlesticks")]
    [InlineData("Wukong", "MonkeyKing")]
    [InlineData("MonkeyKing", "MonkeyKing")]
    [InlineData("Nunu & Willump", "Nunu")]
    [InlineData("Renata Glasc", "Renata")]
    [InlineData("Bel'Veth", "Belveth")]
    [InlineData("K'Sante", "KSante")]
    [InlineData("Kai'Sa", "Kaisa")]
    [InlineData("Dr. Mundo", "DrMundo")]
    [InlineData("Cho'Gath", "Chogath")]
    [InlineData("Kog'Maw", "KogMaw")]
    [InlineData("Rek'Sai", "RekSai")]
    [InlineData("LeBlanc", "Leblanc")]
    [InlineData("Ambessa", "Ambessa")]
    public void ToDDragonImageKey_MapsSpecialChampionKeys(string name, string expected)
    {
        Assert.Equal(expected, ChampionNameHelper.ToDDragonImageKey(name));
    }

    [Fact]
    public void CandidateUrls_AddsCommunityDragonFallbackForChampions()
    {
        var url = "https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/Yunara.png";
        var candidates = BitmapAssetValueConverter.CandidateUrls(url).ToList();

        Assert.Equal(url, candidates[0]);
        Assert.Equal("https://cdn.communitydragon.org/latest/champion/Yunara/square", candidates[1]);

        var item = "https://ddragon.leagueoflegends.com/cdn/16.20.1/img/item/3040.png";
        Assert.Single(BitmapAssetValueConverter.CandidateUrls(item));
    }

    [Fact]
    public void FormatItemDescription_PutsStatsOnSeparateLines()
    {
        var raw = "<mainText><stats><attention>40</attention> Attack Damage<br><attention>20%</attention> Critical Strike Chance</stats><br><br><passive>Rage</passive><br>Gain &amp; keep speed.</mainText>";

        var text = DataDragonService.FormatItemDescription(raw);

        Assert.Equal("40 Attack Damage\n20% Critical Strike Chance\nRage\nGain & keep speed.", text);
    }

    [Fact]
    public void LoadItemJson_FillsItemsAndTooltipsUseNames()
    {
        const string json = """
        {"data":{"993040":{"name":"Seraph's Test","plaintext":"Mana into shield","description":"<mainText><stats><attention>70</attention> Ability Power</stats></mainText>","gold":{"total":2900}}}}
        """;

        var count = DataDragonService.LoadItemJson(json);
        var tooltip = GameConstants.GetItemTooltip(993040);

        Assert.Equal(1, count);
        Assert.Contains("Seraph's Test", tooltip);
        Assert.Contains("2900", tooltip);
        Assert.Contains("70 Ability Power", tooltip);
        GameConstants.Items.TryRemove(993040, out _);
    }

    [Fact]
    public void ItemSlot_RaisesTooltipChangeWhenItemDataArrives()
    {
        var slot = new ItemSlotViewModel { ItemId = 3040 };
        var raised = 0;
        slot.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ItemSlotViewModel.ToolTipText)) raised++; };

        WeakReferenceMessenger.Default.Send(new ItemDataLoadedMessage());

        Assert.Equal(1, raised);
    }
}
