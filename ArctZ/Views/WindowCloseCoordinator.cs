using System;
using System.Threading.Tasks;

namespace ArctZ.Views;

/// <summary>
/// Держит закрытие окна открытым, пока станок не остановлен. Window.Closing синхронен, а
/// остановка ждёт подтверждения от устройства, поэтому первое закрытие отменяется, а окно
/// закрывает себя само, когда останов завершён.
/// </summary>
public sealed class WindowCloseCoordinator
{
    private readonly Func<Task<bool>> _shutdownAsync;
    private readonly Action _close;
    private bool _shutdownRunning;

    /// <param name="shutdownAsync">Останавливает станок; false — пользователь отменил выход.</param>
    /// <param name="close">Закрывает окно повторно, уже без отмены.</param>
    public WindowCloseCoordinator(Func<Task<bool>> shutdownAsync, Action close)
    {
        _shutdownAsync = shutdownAsync;
        _close = close;
    }

    /// <summary>Останов, запущенный этим координатором. Нужен тестам, чтобы дождаться его
    /// завершения; в приложении задача намеренно никем не ожидается.</summary>
    public Task PendingShutdown { get; private set; } = Task.CompletedTask;

    /// <param name="deviceAlreadyStopped">Станок уже остановлен другим путём выхода —
    /// пунктом меню «Выход», который сам вызывает остановку до закрытия окна.</param>
    /// <returns>True, если закрытие нужно отменить.</returns>
    public bool ShouldCancelClose(bool deviceAlreadyStopped)
    {
        if (deviceAlreadyStopped && !_shutdownRunning)
        {
            return false;
        }

        if (_shutdownRunning)
        {
            // Повторный крестик, пока станок тормозит: закрыться сейчас — значит бросить
            // остановку недоделанной.
            return true;
        }

        _shutdownRunning = true;
        PendingShutdown = RunShutdownAsync();
        return true;
    }

    private async Task RunShutdownAsync()
    {
        bool stopped;
        try
        {
            stopped = await _shutdownAsync();
        }
        finally
        {
            // Снимается до _close(): тот заново поднимает Window.Closing, и увидь он останов
            // ещё «идущим», закрытие было бы отменено — окно не закрылось бы никогда.
            _shutdownRunning = false;
        }

        if (stopped)
        {
            _close();
        }
    }
}
