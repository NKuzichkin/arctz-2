using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using ArctZ.Tests.Services.Device;
using ArctZ.Tests.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ProgramViewModelAuthoringTests
{
    private static ProgramViewModel CreateViewModel(out FakeDeviceTransport transport, out FakeProgramStorage storage)
    {
        transport = new FakeDeviceTransport();
        storage = new FakeProgramStorage();
        var connection = new ConnectionViewModel(transport, () => new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));
        return new ProgramViewModel(connection, storage, new TrajectoryCompiler());
    }

    [Fact]
    public async Task CaptureKeyPoint_UsesCurrentDeviceStatusPosition()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:1,2,3,4|FS:0,0>");

        vm.CaptureKeyPointCommand.Execute(null);

        Assert.Single(vm.KeyPoints);
        Assert.Equal(new MachinePose(1, 2, 3, 4), vm.KeyPoints[0].Pose);
    }

    [Fact]
    public async Task CaptureKeyPoint_AssignsSequentialNumbers()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        transport.SimulateReceivedLine("<Idle|WPos:10,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        Assert.Equal(2, vm.KeyPoints.Count);
        Assert.Equal(1, vm.KeyPoints[0].Number);
        Assert.Equal(2, vm.KeyPoints[1].Number);
    }

    [Fact]
    public async Task CaptureKeyPoint_DefaultsLabelToPointNumber()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");

        vm.CaptureKeyPointCommand.Execute(null);

        Assert.Equal("Точка 1", vm.KeyPoints[0].Label);
    }

    [Fact]
    public void CaptureKeyPoint_NoActiveSession_DoesNothing()
    {
        var vm = CreateViewModel(out _, out _);

        vm.CaptureKeyPointCommand.Execute(null);

        Assert.Empty(vm.KeyPoints);
    }

    [Fact]
    public async Task RemoveKeyPoint_MiddlePoint_RemovesItAndRenumbersTheRest()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        foreach (var pose in new[] { "0,0,0,0", "10,0,0,0", "20,0,0,0" })
        {
            transport.SimulateReceivedLine($"<Idle|WPos:{pose}|FS:0,0>");
            vm.CaptureKeyPointCommand.Execute(null);
        }

        var middle = vm.KeyPoints[1];
        var removeTask = vm.RemoveKeyPointCommand.ExecuteAsync(middle);
        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmYesCommand.Execute(null);
        await removeTask;

        Assert.Equal(2, vm.KeyPoints.Count);
        Assert.DoesNotContain(middle, vm.KeyPoints);
        Assert.Equal(1, vm.KeyPoints[0].Number);
        Assert.Equal(2, vm.KeyPoints[1].Number);
    }

    [Fact]
    public async Task SaveProgramAsync_ThenRefreshLibrary_ListsSavedProgram()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await saveTask;

        await vm.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.Contains(vm.Library, s => s.Name == "Тест");
    }

    [Fact]
    public async Task SaveProgramAsync_ThenRefreshLibrary_MarksSavedProgramAsLoaded()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await saveTask;

        await vm.RefreshLibraryCommand.ExecuteAsync(null);

        Assert.True(vm.Library.Single(s => s.Name == "Тест").IsLoaded);
    }

    [Fact]
    public async Task LoadProgram_MarksOnlyThatEntryAsLoaded()
    {
        var vm = CreateViewModel(out _, out var storage);
        await storage.SaveAsync(new JibProgram { Id = Guid.NewGuid(), Name = "A" });
        await storage.SaveAsync(new JibProgram { Id = Guid.NewGuid(), Name = "B" });
        await vm.RefreshLibraryCommand.ExecuteAsync(null);
        var target = vm.Library.Single(p => p.Name == "B");

        await vm.LoadProgramCommand.ExecuteAsync(target);

        Assert.True(vm.Library.Single(p => p.Name == "B").IsLoaded);
        Assert.False(vm.Library.Single(p => p.Name == "A").IsLoaded);
    }

    [Fact]
    public async Task NewProgram_ClearsLoadedFlagOnAllEntries()
    {
        var vm = CreateViewModel(out _, out var storage);
        await storage.SaveAsync(new JibProgram { Id = Guid.NewGuid(), Name = "A" });
        await vm.RefreshLibraryCommand.ExecuteAsync(null);
        await vm.LoadProgramCommand.ExecuteAsync(vm.Library[0]);

        vm.NewProgramCommand.Execute(null);

        Assert.All(vm.Library, p => Assert.False(p.IsLoaded));
    }

    [Fact]
    public async Task SaveProgramAsync_OverwritingLoadedProgram_AsksForConfirmation()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var firstSaveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await firstSaveTask;

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);

        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmYesCommand.Execute(null);
        await saveTask;

        Assert.Null(vm.PendingConfirmation);
    }

    [Fact]
    public async Task SaveProgramAsync_DecliningOverwriteConfirmation_DoesNotSave()
    {
        var vm = CreateViewModel(out var transport, out var storage);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";

        var firstSaveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await firstSaveTask;
        var savedId = vm.ProgramId!.Value;

        transport.SimulateReceivedLine("<Idle|WPos:10,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmNoCommand.Execute(null);
        await saveTask;

        var stored = await storage.LoadAsync(savedId);
        Assert.Single(stored.KeyPoints);
    }

    [Fact]
    public async Task SaveProgramAsync_NameCollidesWithDifferentProgram_AsksForConfirmation()
    {
        var vm = CreateViewModel(out _, out var storage);
        await storage.SaveAsync(new JibProgram { Id = Guid.NewGuid(), Name = "Existing" });
        await vm.RefreshLibraryCommand.ExecuteAsync(null);
        vm.ProgramName = "Existing";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);

        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmYesCommand.Execute(null);
        await saveTask;

        Assert.NotNull(vm.ProgramId);
    }

    [Fact]
    public async Task SaveProgramAsync_DecliningNameCollisionConfirmation_DoesNotSave()
    {
        var vm = CreateViewModel(out _, out var storage);
        await storage.SaveAsync(new JibProgram { Id = Guid.NewGuid(), Name = "Existing" });
        await vm.RefreshLibraryCommand.ExecuteAsync(null);
        vm.ProgramName = "Existing";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);

        Assert.NotNull(vm.PendingConfirmation);
        vm.ConfirmNoCommand.Execute(null);
        await saveTask;

        Assert.Null(vm.ProgramId);
    }

    [Fact]
    public async Task SaveProgramAsync_NewProgram_CancellingRenameDialog_DoesNotSave()
    {
        var vm = CreateViewModel(out _, out var storage);
        vm.ProgramName = "Тест";

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.CancelRenameCommand.Execute(null);
        await saveTask;

        Assert.Null(vm.ProgramId);
        Assert.Empty(await storage.ListAsync());
    }

    [Fact]
    public async Task EditKeyPoint_OpensEditorPrefilledFromThePoint()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:1,2,3,4|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        vm.EditKeyPointCommand.Execute(vm.KeyPoints[0]);

        Assert.True(vm.IsEditingKeyPoint);
        Assert.NotNull(vm.KeyPointEditor);
        Assert.Equal(1, vm.KeyPointEditor!.Number);
        Assert.Equal(1, vm.KeyPointEditor.X);
        Assert.Equal(2, vm.KeyPointEditor.Y);
        Assert.Equal(3, vm.KeyPointEditor.Z);
        Assert.Equal(4, vm.KeyPointEditor.A);
    }

    [Fact]
    public async Task EditKeyPoint_Save_UpdatesThePointInPlaceAndClosesEditor()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        vm.EditKeyPointCommand.Execute(vm.KeyPoints[0]);
        vm.KeyPointEditor!.Label = "Общий план";
        vm.KeyPointEditor.X = 42;
        vm.KeyPointEditor.DwellSeconds = 5;
        vm.KeyPointEditor.SaveCommand.Execute(null);

        Assert.False(vm.IsEditingKeyPoint);
        Assert.Equal("Общий план", vm.KeyPoints[0].Label);
        Assert.Equal(new MachinePose(42, 0, 0, 0), vm.KeyPoints[0].Pose);
        Assert.Equal(5, vm.KeyPoints[0].DwellSeconds);
        Assert.Equal(1, vm.KeyPoints[0].Number);
    }

    [Fact]
    public async Task EditKeyPoint_Cancel_LeavesThePointUnchangedAndClosesEditor()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        var original = vm.KeyPoints[0];

        vm.EditKeyPointCommand.Execute(original);
        vm.KeyPointEditor!.X = 99;
        vm.KeyPointEditor.CancelCommand.Execute(null);

        Assert.False(vm.IsEditingKeyPoint);
        Assert.Equal(original, vm.KeyPoints[0]);
    }

    [Fact]
    public async Task FillKeyPointFromCurrentPosition_ReplacesPoseButKeepsOtherFields()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        var beforeNumber = vm.KeyPoints[0].Number;

        transport.SimulateReceivedLine("<Idle|WPos:5,6,7,8|FS:0,0>");
        vm.FillKeyPointFromCurrentPositionCommand.Execute(vm.KeyPoints[0]);

        Assert.Equal(new MachinePose(5, 6, 7, 8), vm.KeyPoints[0].Pose);
        Assert.Equal(beforeNumber, vm.KeyPoints[0].Number);
    }

    [Fact]
    public async Task MoveMachineToKeyPoint_SendsG1MoveToThePointsPoseAndFeed()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);

        _ = vm.MoveMachineToKeyPointCommand.ExecuteAsync(vm.KeyPoints[0]);

        Assert.Contains(transport.SentLines, l => l == "G1 X0 Y0 Z0 A0 F500");
    }

    [Fact]
    public async Task LeftAndRightJoystick_EndJogOnlyAfterBothSticksReleased()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();

        vm.OnLeftJoystickDown(new JoystickEventArgs { Force = 1, AngleDeg = 0 });
        vm.OnRightJoystickDown(new JoystickEventArgs { Force = 1, AngleDeg = 90 });
        vm.OnLeftJoystickUp(new JoystickEventArgs { Force = 0, AngleDeg = 0 });

        Assert.DoesNotContain((byte)0x85, transport.SentRawBytes);

        vm.OnRightJoystickUp(new JoystickEventArgs { Force = 0, AngleDeg = 90 });

        Assert.Contains((byte)0x85, transport.SentRawBytes);
    }

    [Fact]
    public void RenameProgramAsync_Confirmed_UpdatesProgramName()
    {
        var vm = CreateViewModel(out _, out _);
        vm.ProgramName = "Старое имя";

        var renameTask = vm.RenameProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        Assert.Equal("Старое имя", vm.PendingRename!.Name);

        vm.PendingRename.Name = "Новое имя";
        vm.ConfirmRenameCommand.Execute(null);

        Assert.Null(vm.PendingRename);
        Assert.Equal("Новое имя", vm.ProgramName);
        Assert.True(renameTask.IsCompleted);
    }

    [Fact]
    public void RenameProgramAsync_Cancelled_LeavesProgramNameUnchanged()
    {
        var vm = CreateViewModel(out _, out _);
        vm.ProgramName = "Старое имя";

        var renameTask = vm.RenameProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);

        vm.CancelRenameCommand.Execute(null);

        Assert.Null(vm.PendingRename);
        Assert.Equal("Старое имя", vm.ProgramName);
        Assert.True(renameTask.IsCompleted);
    }

    [Fact]
    public void EditCompletionSettings_OpensEditorPrefilledFromCurrentSettings()
    {
        var vm = CreateViewModel(out _, out _);
        vm.CompletionMode = ProgramCompletionMode.Loop;
        vm.RepeatCount = 5;
        vm.ReturnToStartOnFinish = true;

        vm.EditCompletionSettingsCommand.Execute(null);

        Assert.True(vm.IsEditingCompletionSettings);
        Assert.NotNull(vm.CompletionSettingsEditor);
        Assert.Equal(ProgramCompletionMode.Loop, vm.CompletionSettingsEditor!.Mode);
        Assert.Equal(5, vm.CompletionSettingsEditor.RepeatCount);
        Assert.False(vm.CompletionSettingsEditor.IsRepeatUnlimited);
        Assert.True(vm.CompletionSettingsEditor.ReturnToStartOnFinish);
    }

    [Fact]
    public void EditCompletionSettings_Save_UpdatesProgramViewModelAndClosesEditor()
    {
        var vm = CreateViewModel(out _, out _);

        vm.EditCompletionSettingsCommand.Execute(null);
        vm.CompletionSettingsEditor!.Mode = ProgramCompletionMode.Loop;
        vm.CompletionSettingsEditor.RepeatCount = 10;
        vm.CompletionSettingsEditor.IsRepeatUnlimited = false;
        vm.CompletionSettingsEditor.ReturnToStartOnFinish = true;
        vm.CompletionSettingsEditor.SaveCommand.Execute(null);

        Assert.False(vm.IsEditingCompletionSettings);
        Assert.Equal(ProgramCompletionMode.Loop, vm.CompletionMode);
        Assert.Equal(10, vm.RepeatCount);
        Assert.True(vm.ReturnToStartOnFinish);
    }

    [Fact]
    public void EditCompletionSettings_Cancel_LeavesProgramViewModelUnchangedAndClosesEditor()
    {
        var vm = CreateViewModel(out _, out _);
        vm.CompletionMode = ProgramCompletionMode.Stop;

        vm.EditCompletionSettingsCommand.Execute(null);
        vm.CompletionSettingsEditor!.Mode = ProgramCompletionMode.Loop;
        vm.CompletionSettingsEditor.CancelCommand.Execute(null);

        Assert.False(vm.IsEditingCompletionSettings);
        Assert.Equal(ProgramCompletionMode.Stop, vm.CompletionMode);
    }

    [Fact]
    public async Task SaveProgramAsync_ThenLoadProgramAsync_RoundTripsCompletionSettings()
    {
        var vm = CreateViewModel(out var transport, out _);
        await vm.Connection.ConnectCommand.Execute();
        transport.SimulateReceivedLine("<Idle|WPos:0,0,0,0|FS:0,0>");
        vm.CaptureKeyPointCommand.Execute(null);
        vm.ProgramName = "Тест";
        vm.CompletionMode = ProgramCompletionMode.PingPong;
        vm.RepeatCount = 3;
        vm.ReturnToStartOnFinish = true;

        var saveTask = vm.SaveProgramCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PendingRename);
        vm.ConfirmRenameCommand.Execute(null);
        await saveTask;

        vm.NewProgramCommand.Execute(null);
        Assert.Equal(ProgramCompletionMode.Stop, vm.CompletionMode);
        Assert.False(vm.ReturnToStartOnFinish);
        Assert.Null(vm.RepeatCount);

        await vm.RefreshLibraryCommand.ExecuteAsync(null);
        await vm.LoadProgramCommand.ExecuteAsync(vm.Library.Single(p => p.Name == "Тест"));

        Assert.Equal(ProgramCompletionMode.PingPong, vm.CompletionMode);
        Assert.Equal(3, vm.RepeatCount);
        Assert.True(vm.ReturnToStartOnFinish);
    }
}
