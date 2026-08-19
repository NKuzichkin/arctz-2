using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using ArctZ.Services.App;
using ArctZ.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ArctZ.Android;

/// <summary>
/// Постоянное уведомление сеанса со станком плюс — и это главное — живой процесс на время,
/// которого хватает остановить станок при закрытии приложения из недавних. Без запущенного
/// сервиса Android не вызывает OnTaskRemoved и убивает процесс молча, оставив станок
/// доигрывать содержимое буфера прошивки.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public class MachineSessionService : Service
{
    public const string ActionShow = "com.arctz.app.action.SHOW";
    public const string ActionPause = "com.arctz.app.action.PAUSE";
    public const string ActionResume = "com.arctz.app.action.RESUME";
    public const string ActionStop = "com.arctz.app.action.STOP";

    private const string ChannelId = "arctz.session";
    private const int NotificationId = 1;

    /// <summary>Последнее состояние, отданное ядром. Пишется из AndroidBackgroundSessionHost
    /// перед тем, как поднять сервис, и читается здесь при построении уведомления.</summary>
    public static BackgroundSessionState CurrentState { get; set; } =
        new(BackgroundSessionProjector.AppName, "Ожидание", false, false, false, null);

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannel();
        StartInForeground();

        var program = ArctZ.App.Services?.GetService<ProgramViewModel>();
        switch (intent?.Action)
        {
            case ActionPause:
                program?.PauseCommand.Execute(null);
                break;
            case ActionResume:
                program?.PlayCommand.Execute(null);
                break;
            case ActionStop:
                program?.StopCommand.Execute(null);
                break;
        }

        // NotSticky: перезапускать сервис после убийства процесса бессмысленно — вместе с
        // процессом исчезли и ViewModel, и связь со станком, управлять нечем.
        return StartCommandResult.NotSticky;
    }

    /// <summary>Приложение смахнули из недавних. Активности уже нет, спрашивать некого —
    /// останавливаем станок молча и только потом отпускаем процесс.</summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        base.OnTaskRemoved(rootIntent);

        var program = ArctZ.App.Services?.GetService<ProgramViewModel>();
        if (program is null)
        {
            StopSession();
            return;
        }

        _ = StopMachineThenSessionAsync(program);
    }

    private async Task StopMachineThenSessionAsync(ProgramViewModel program)
    {
        try
        {
            await program.ShutdownAsync(confirmIfRunning: false);
        }
        catch (Exception)
        {
            // Связь могла оборваться вместе с закрытием приложения. Остановить станок в этом
            // случае уже нечем, но процесс отпустить надо в любом случае — иначе сервис
            // останется висеть в шторке навсегда.
        }
        finally
        {
            StopSession();
        }
    }

    private void StopSession()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        else
        {
#pragma warning disable CA1422 // до API 24 другой перегрузки нет
            StopForeground(removeNotification: true);
#pragma warning restore CA1422
        }

        StopSelf();
    }

    private void StartInForeground()
    {
        var notification = BuildNotification();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        // Low: уведомление висит всё время работы со станком, звук и всплытие тут были бы
        // наказанием.
        var channel = new NotificationChannel(ChannelId, "Сеанс со станком", NotificationImportance.Low)
        {
            Description = "Состояние связи со станком и управление выполнением программы",
        };
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var state = CurrentState;

        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);

        builder
            .SetContentTitle(state.Title)
            .SetContentText(state.Status)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetOngoing(true)
            .SetContentIntent(OpenAppIntent());

        if (state.CanPause)
        {
            builder.AddAction(BuildAction(global::Android.Resource.Drawable.IcMediaPause, "Пауза", ActionPause));
        }

        if (state.CanResume)
        {
            builder.AddAction(BuildAction(global::Android.Resource.Drawable.IcMediaPlay, "Продолжить", ActionResume));
        }

        if (state.CanStop)
        {
            builder.AddAction(BuildAction(global::Android.Resource.Drawable.IcMenuCloseClearCancel, "Стоп", ActionStop));
        }

        return builder.Build();
    }

    private Notification.Action BuildAction(int iconResource, string title, string action)
    {
        var intent = new Intent(this, typeof(MachineSessionService)).SetAction(action);
        var pending = PendingIntent.GetService(this, action.GetHashCode(), intent, BuildPendingIntentFlags())!;

        // Перегрузка с ресурсом-int объявлена устаревшей с того же API 23, ниже которого проект
        // и не собирается, поэтому иконка строится через Icon.
        var icon = global::Android.Graphics.Drawables.Icon.CreateWithResource(this, iconResource);

        return new Notification.Action.Builder(icon, title, pending).Build();
    }

    private PendingIntent OpenAppIntent()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.NewTask);

        return PendingIntent.GetActivity(this, 0, intent, BuildPendingIntentFlags())!;
    }

    // Immutable обязателен с API 31; UpdateCurrent нужен, чтобы кнопки не залипали на первом
    // созданном интенте.
    private static PendingIntentFlags BuildPendingIntentFlags() =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;
}
