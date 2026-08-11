using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class MainViewNarrowLayoutTests
{
    [Theory]
    [InlineData(360, true)]    // телефон-портрет
    [InlineData(699, true)]    // прямо под порогом
    [InlineData(700, false)]   // порог не включён (строго <)
    [InlineData(1200, false)]  // десктоп
    public void ComputeIsNarrowLayout_ReturnsExpectedResult(double mainViewWidth, bool expectedIsNarrow)
    {
        var isNarrow = MainView.ComputeIsNarrowLayout(mainViewWidth);

        Assert.Equal(expectedIsNarrow, isNarrow);
    }
}
