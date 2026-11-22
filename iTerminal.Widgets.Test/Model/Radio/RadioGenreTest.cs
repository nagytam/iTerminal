using iTerminal.Widgets.Model.Radio;

namespace iTerminal.Widgets.Test.Model.Radio;

[TestClass]
public class RadioGenreTest
{
    [TestMethod]
    [DataRow("music, jazz", "♭")]
    [DataRow("smooth jazz", "♭")]
    [DataRow("music, classical", "♩")]
    [DataRow("music, electronic", "»")]
    [DataRow("music, ambient", "~")]
    [DataRow("music, alternative", "♬")]
    [DataRow("music, oldies", "↺")]
    [DataRow("music, 80s, 90s", "↺")]
    [DataRow("music, pop", "♫")]
    [DataRow("music, hits", "♫")]
    [DataRow("news, public radio", "¶")]
    [DataRow("business, news", "$")]
    [DataRow("technology", "◈")]
    [DataRow("sport", "◎")]
    [DataRow("music, eclectic", "♪")]
    [DataRow("MUSIC, JAZZ", "♭")]
    public void TheIconFollowsTheFirstKnownWordOfTheGenre(string genre, string expected) =>
        Assert.AreEqual(expected, RadioGenre.IconFor(genre));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void AStationWithoutAGenreGetsTheBullet(string? genre) =>
        Assert.AreEqual(RadioGenre.NoGenreIcon, RadioGenre.IconFor(genre));
}
