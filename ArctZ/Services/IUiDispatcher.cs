using System;

namespace ArctZ.Services;

/// <summary>
/// Seam for marshalling calls onto the UI thread. IDeviceSession.ConnectionStateChanged
/// (and similar device events) can fire from a background thread — e.g. SerialPort's
/// event thread on the real-device path — and Avalonia does not auto-marshal bound
/// property updates. Callers that mutate observable state from such events must route
/// through this first. Kept as an interface (rather than calling
/// Avalonia.Threading.Dispatcher.UIThread directly) so view models stay testable without
/// a running Avalonia application/dispatcher loop — touching the real dispatcher from a
/// plain xunit host is unreliable, since it's a process-wide singleton with no message
/// pump running there.
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);
}
