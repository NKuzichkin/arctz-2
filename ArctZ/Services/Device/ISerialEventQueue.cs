using System;

namespace ArctZ.Services.Device;

public interface ISerialEventQueue
{
    void Enqueue(Action action);
}
