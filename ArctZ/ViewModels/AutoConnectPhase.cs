namespace ArctZ.ViewModels;

/// <summary>Progress of ConnectionViewModel.AutoConnectAsync's find-and-connect loop.
/// Drives IsAutoConnectSplashVisible/AutoConnectStatusText (see Task 7).</summary>
public enum AutoConnectPhase
{
    Idle,
    Searching,
    Connecting,
    WaitingRetry,
    GivenUp
}
