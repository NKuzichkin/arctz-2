namespace ArctZ.Services.App;

/// <summary>
/// Платформенный «фоновый сеанс»: на Android — постоянное уведомление с кнопками управления,
/// которое заодно удерживает процесс живым достаточно долго, чтобы остановить станок при
/// закрытии приложения. На остальных платформах ничего подобного нет — там работает
/// <see cref="NullBackgroundSessionHost"/>.
/// </summary>
public interface IBackgroundSessionHost
{
    /// <summary>Показать или обновить сеанс. Идемпотентно: вызывается на каждое изменение
    /// состояния, в том числе когда сеанс уже показан.</summary>
    void Update(BackgroundSessionState state);

    /// <summary>Убрать сеанс. Идемпотентно: вызывается и когда сеанса нет.</summary>
    void Stop();
}

public sealed class NullBackgroundSessionHost : IBackgroundSessionHost
{
    public void Update(BackgroundSessionState state)
    {
    }

    public void Stop()
    {
    }
}
