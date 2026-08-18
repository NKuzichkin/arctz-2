using System;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Screenshots.Support;
using ArctZ.ViewModels;
using ArctZ.Views;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace ArctZ.Tests.Screenshots;

/// <summary>
/// Regression coverage for the "Invalid Arrange rectangle" InvalidOperationException
/// thrown from MainView.OnSentGCodeLinesChanged → GCodeLogList.ScrollIntoView. That
/// handler used to call ScrollIntoView synchronously, inline, from inside
/// SentGCodeLines.CollectionChanged — including for the Add half of
/// ConnectionViewModel.AppendSentGCodeLine's trim-then-add pair (RemoveAt(0) followed
/// immediately by Add once the 200-line cap is hit). Forcing VirtualizingStackPanel's
/// own internal ScrollIntoView layout pass mid-mutation, before the panel's realized-
/// element bookkeeping has settled from the preceding Remove, could hand it a rect with
/// a NaN/negative component, which Avalonia's Layoutable.Arrange rejects.
/// </summary>
[Collection(HeadlessAppCollection.Name)]
public class GCodeLogAutoScrollTests
{
    public GCodeLogAutoScrollTests() => HeadlessAppBootstrap.EnsureInitialized();

    [Fact]
    public void AppendingLinesPastTheTrimCapWithLogOpenDoesNotThrow()
    {
        var connection = new ConnectionViewModel(
            new FakeDeviceTransport(),
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            new SingleRealDeviceEndpointProvider());
        var programViewModel = new ProgramViewModel(connection, new FakeProgramStorage(), new TrajectoryCompiler(), new FakeAppExitService());

        var mainView = new MainView { DataContext = programViewModel };
        VisualTreeAnimationStripper.StripRevealAnimations(mainView);

        var window = new Window { Width = 1280, Height = 800, Content = mainView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            connection.IsGCodeLogOpen = true;
            Dispatcher.UIThread.RunJobs();

            // Mirrors ConnectionViewModel.AppendSentGCodeLine (trim-before-add once the
            // private MaxSentGCodeLines=200 cap is reached), driven directly through the
            // public SentGCodeLines collection since AppendSentGCodeLine itself is only
            // reachable via the real device-session pipeline.
            const int maxSentGCodeLines = 200;
            for (var i = 0; i < maxSentGCodeLines + 25; i++)
            {
                if (connection.SentGCodeLines.Count >= maxSentGCodeLines)
                {
                    connection.SentGCodeLines.RemoveAt(0);
                }

                connection.SentGCodeLines.Add($"G1 X{i} Y0 Z0 F500");
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            window.Close();
        }
    }
}
