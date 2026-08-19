using ArctZ.Services.App;
using ArctZ.ViewModels;

namespace ArctZ.Tests.Services.App;

public class BackgroundSessionProjectorTests
{
    [Fact]
    public void Project_WhileRunning_OffersPauseAndStop()
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Running, "Выполнение", "Панорама цеха", overallFraction: null);

        Assert.Equal("Панорама цеха", state.Title);
        Assert.Equal("Выполнение", state.Status);
        Assert.True(state.CanPause);
        Assert.False(state.CanResume);
        Assert.True(state.CanStop);
    }

    [Fact]
    public void Project_WhilePaused_OffersResumeAndStop()
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Paused, "Пауза", "Панорама цеха", overallFraction: null);

        Assert.False(state.CanPause);
        Assert.True(state.CanResume);
        Assert.True(state.CanStop);
    }

    [Theory]
    [InlineData(PlaybackState.Idle)]
    [InlineData(PlaybackState.Stopped)]
    [InlineData(PlaybackState.Completed)]
    [InlineData(PlaybackState.Faulted)]
    public void Project_WhenNoProgramIsInFlight_OffersNoButtons(PlaybackState playback)
    {
        var state = BackgroundSessionProjector.Project(playback, "Ожидание", "Панорама цеха", overallFraction: null);

        Assert.False(state.CanPause);
        Assert.False(state.CanResume);
        Assert.False(state.CanStop);
    }

    /// <summary>Программа может быть не сохранена и не названа — в шторке всё равно должно
    /// стоять узнаваемое имя приложения, а не пустая строка.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Project_WithoutAProgramName_FallsBackToTheAppName(string? programName)
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Idle, "Ожидание", programName, overallFraction: null);

        Assert.Equal("ArctZ", state.Title);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.02, 0)]
    [InlineData(0.03, 5)]
    [InlineData(0.475, 50)]
    [InlineData(0.99, 100)]
    [InlineData(1.0, 100)]
    public void Project_WhileRunning_RoundsOverallFractionToTheNearestFivePercent(double fraction, int expectedPercent)
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Running, "Выполнение", "Панорама цеха", fraction);
        Assert.Equal(expectedPercent, state.ProgressPercent);
    }

    [Fact]
    public void Project_WithoutAnOverallFraction_HasNoProgressPercent()
    {
        var state = BackgroundSessionProjector.Project(PlaybackState.Idle, "Ожидание", "Панорама цеха", overallFraction: null);
        Assert.Null(state.ProgressPercent);
    }
}
