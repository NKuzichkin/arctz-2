using System;
using Android.Content;
using ArctZ.Services.App;

namespace ArctZ.Android;

/// <summary>
/// Поднимает и обновляет <see cref="MachineSessionService"/>. Обновление — это тот же запуск
/// сервиса с ActionShow: сервис на каждый старт перестраивает уведомление из
/// <see cref="MachineSessionService.CurrentState"/>, так что отдельный путь обновления не нужен.
/// </summary>
public sealed class AndroidBackgroundSessionHost : IBackgroundSessionHost
{
    public void Update(BackgroundSessionState state)
    {
        MachineSessionService.CurrentState = state;

        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MachineSessionService)).SetAction(MachineSessionService.ActionShow);

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception)
        {
            // С API 31 запуск foreground-сервиса из фона запрещён и бросает
            // ForegroundServiceStartNotAllowedException. Первый Update приходит на подключение,
            // то есть из активного приложения, поэтому в норме сюда не попадаем; но обновление
            // состояния не должно ронять приложение из-за уведомления.
        }
    }

    public void Stop()
    {
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(MachineSessionService)));
    }
}
