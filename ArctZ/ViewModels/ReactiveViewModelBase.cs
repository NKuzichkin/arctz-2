using System;
using System.Reactive.Disposables;
using ReactiveUI;

namespace ArctZ.ViewModels;

public abstract class ReactiveViewModelBase : ReactiveObject, IDisposable
{
    protected CompositeDisposable Disposables { get; } = new();

    public void Dispose() => Disposables.Dispose();
}
