namespace ArctZ.Services.App;

/// <summary>
/// Что показывать в постоянном уведомлении фонового сеанса. Платформа получает уже готовые
/// строки и флаги: решение о том, какая кнопка уместна, принимается в ядре и покрыто тестами.
/// </summary>
public readonly record struct BackgroundSessionState(
    string Title,
    string Status,
    bool CanPause,
    bool CanResume,
    bool CanStop);
