using System;
using System.ComponentModel;
using ArctZ.ViewModels;

namespace ArctZ.Services.App;

/// <summary>
/// Держит платформенный фоновый сеанс в согласии с состоянием приложения: показывает его, пока
/// есть связь со станком, и убирает, когда связи не стало. Один экземпляр на приложение,
/// создаётся при старте — см. App.OnFrameworkInitializationCompleted.
/// </summary>
public sealed class BackgroundSessionCoordinator : IDisposable
{
    private readonly ProgramViewModel _program;
    private readonly IBackgroundSessionHost _host;
    private bool _shown;
    private BackgroundSessionState? _lastSent;

    public BackgroundSessionCoordinator(ProgramViewModel program, IBackgroundSessionHost host)
    {
        _program = program;
        _host = host;

        _program.PropertyChanged += OnProgramPropertyChanged;
        _program.Connection.PropertyChanged += OnConnectionPropertyChanged;

        Refresh();
    }

    public void Dispose()
    {
        _program.PropertyChanged -= OnProgramPropertyChanged;
        _program.Connection.PropertyChanged -= OnConnectionPropertyChanged;
    }

    private void OnProgramPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProgramViewModel.PlaybackState)
            or nameof(ProgramViewModel.StatusLabel)
            or nameof(ProgramViewModel.ProgramName))
        {
            Refresh();
        }
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionViewModel.Session)
            or nameof(ConnectionViewModel.ConnectionState)
            or nameof(ConnectionViewModel.DeviceStatus))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        // Признак «связь есть» — Session, а не ConnectionState: при обрыве ConnectionState
        // уходит в Reconnecting, но станок никуда не делся, и сеанс обязан пережить
        // переподключение. Session обнуляется только при явном отключении и при закрытии
        // приложения.
        if (_program.Connection.Session is null)
        {
            if (_shown)
            {
                _shown = false;
                _lastSent = null;
                _host.Stop();
            }

            return;
        }

        _shown = true;

        // ProgramViewModel forces a StatusLabel PropertyChanged on every status report while
        // running (position moves, the label text doesn't) — without this dedup, that reposted
        // the Android notification (StartForeground) on every poll and made it visibly jitter.
        var state = BackgroundSessionProjector.Project(
            _program.PlaybackState,
            _program.StatusLabel,
            _program.ProgramName);

        if (_lastSent == state)
        {
            return;
        }

        _lastSent = state;
        _host.Update(state);
    }
}
