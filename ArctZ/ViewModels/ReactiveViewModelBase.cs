using System;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using ReactiveUI;
using Zafiro.UI.Commands;

namespace ArctZ.ViewModels;

public abstract class ReactiveViewModelBase : ReactiveObject, IDisposable
{
    protected CompositeDisposable Disposables { get; } = new();

    private string? lastCommandError;

    /// <summary>
    /// Most recent exception message from any command created via <see cref="Track{T}"/>.
    /// There is no logging framework in this project (no ILogger/Serilog wired up), so this is
    /// the smallest mechanism that is both safe (nothing crashes) and honest (nothing is
    /// silently dropped): the error lands on bindable state a view can surface later, instead of
    /// vanishing into Debug.WriteLine output nobody watches in a release build. Deliberately
    /// separate from IDeviceSession.LastError, which tracks session-level reconnect failures
    /// (set internally by DeviceSession) rather than UI-triggered command failures.
    /// </summary>
    public string? LastCommandError
    {
        get => lastCommandError;
        private set => this.RaiseAndSetIfChanged(ref lastCommandError, value);
    }

    /// <summary>
    /// Subscribes ThrownExceptions and registers the command for disposal in one call. Every
    /// ReactiveCommand-backed command needs this: under ReactiveUI 23.x, an unobserved command
    /// exception is rescheduled as a throw on RxSchedulers.MainThreadScheduler, which crashes the
    /// process (the CommunityToolkit.Mvvm AsyncRelayCommand this replaced instead left an
    /// unobserved Task, effectively swallowing the failure). IEnhancedCommand's interface chain
    /// already includes IHandleObservableErrors.ThrownExceptions and (via
    /// ReactiveUI.IReactiveCommand) IDisposable, so both calls below are ordinary interface
    /// members, not unchecked casts.
    /// </summary>
    protected T Track<T>(T command) where T : IEnhancedCommand
    {
        command.ThrownExceptions
            .Subscribe(ex => LastCommandError = ex.Message)
            .DisposeWith(Disposables);
        command.DisposeWith(Disposables);
        return command;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Disposables.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
