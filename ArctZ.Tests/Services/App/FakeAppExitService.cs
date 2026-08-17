using ArctZ.Services.App;

namespace ArctZ.Tests.Services.App;

public sealed class FakeAppExitService : IAppExitService
{
    public int ExitCallCount { get; private set; }

    public void Exit()
    {
        ExitCallCount++;
    }
}
