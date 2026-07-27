using System;

namespace ArctZ.Services.Device;

public interface IPeriodicTimer
{
    event Action? Elapsed;

    void Start(TimeSpan interval);

    void Stop();
}
