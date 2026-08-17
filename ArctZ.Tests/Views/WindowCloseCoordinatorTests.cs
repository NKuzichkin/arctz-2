using System.Threading.Tasks;
using ArctZ.Views;

namespace ArctZ.Tests.Views;

public class WindowCloseCoordinatorTests
{
    private readonly TaskCompletionSource<bool> _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _shutdownStartCount;
    private int _closeCount;
    private bool? _cancelSeenByTheSecondClosing;
    private readonly WindowCloseCoordinator _coordinator;

    public WindowCloseCoordinatorTests()
    {
        _coordinator = new WindowCloseCoordinator(
            () =>
            {
                _shutdownStartCount++;
                return _shutdown.Task;
            },
            () =>
            {
                _closeCount++;
                // Window.Close() поднимает Closing повторно, прямо изнутри этого вызова.
                _cancelSeenByTheSecondClosing ??= _coordinator!.ShouldCancelClose(deviceAlreadyStopped: true);
            });
    }

    [Fact]
    public void FirstClose_IsCancelledAndStartsTheShutdown()
    {
        var cancel = _coordinator.ShouldCancelClose(deviceAlreadyStopped: false);

        Assert.True(cancel);
        Assert.Equal(1, _shutdownStartCount);
        Assert.Equal(0, _closeCount);
    }

    [Fact]
    public async Task OnceTheShutdownFinishes_TheWindowIsClosed()
    {
        _coordinator.ShouldCancelClose(deviceAlreadyStopped: false);

        _shutdown.SetResult(true);
        await _coordinator.PendingShutdown;

        Assert.Equal(1, _closeCount);
        Assert.False(_cancelSeenByTheSecondClosing);
    }

    [Fact]
    public void CloseAgainWhileStopping_IsCancelledWithoutStartingASecondShutdown()
    {
        _coordinator.ShouldCancelClose(deviceAlreadyStopped: false);

        // Пользователь давит крестик повторно, пока станок ещё тормозит.
        var cancel = _coordinator.ShouldCancelClose(deviceAlreadyStopped: true);

        Assert.True(cancel);
        Assert.Equal(1, _shutdownStartCount);
        Assert.Equal(0, _closeCount);
    }

    [Fact]
    public async Task WhenTheUserDeclinesTheExit_TheWindowStaysOpenAndCanBeClosedAgain()
    {
        _coordinator.ShouldCancelClose(deviceAlreadyStopped: false);

        _shutdown.SetResult(false);
        await _coordinator.PendingShutdown;

        Assert.Equal(0, _closeCount);
        Assert.True(_coordinator.ShouldCancelClose(deviceAlreadyStopped: false));
        Assert.Equal(2, _shutdownStartCount);
    }

    /// <summary>Пункт меню «Выход» останавливает станок сам и только потом просит
    /// приложение закрыться — этот close отменять нельзя.</summary>
    [Fact]
    public void CloseRequestedAfterTheExitMenuAlreadyStoppedTheDevice_IsNotCancelled()
    {
        var cancel = _coordinator.ShouldCancelClose(deviceAlreadyStopped: true);

        Assert.False(cancel);
        Assert.Equal(0, _shutdownStartCount);
    }
}
