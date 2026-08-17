using System.Collections.Generic;
using ArctZ.Services.App;

namespace ArctZ.Tests.Services.App;

public sealed class FakeBackgroundSessionHost : IBackgroundSessionHost
{
    public List<BackgroundSessionState> Updates { get; } = new();

    public int StopCallCount { get; private set; }

    public BackgroundSessionState? LastUpdate => Updates.Count == 0 ? null : Updates[^1];

    public void Update(BackgroundSessionState state) => Updates.Add(state);

    public void Stop() => StopCallCount++;
}
