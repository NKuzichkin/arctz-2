using System;
using ArctZ.Services;

namespace ArctZ.Tests.Services;

/// <summary>Runs everything inline/synchronously — tests never touch the real Avalonia
/// Dispatcher.UIThread singleton, which is process-wide and unreliable to assert against
/// from a plain xunit host with no running message pump.</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();
}
