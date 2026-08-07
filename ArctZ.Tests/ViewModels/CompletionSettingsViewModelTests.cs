using System.Collections.Generic;
using ArctZ.Services.Program;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class CompletionSettingsViewModelTests
{
    private static (CompletionSettingsViewModel Vm,
        List<(ProgramCompletionMode Mode, int? RepeatCount, bool ReturnToStartOnFinish)> Saved,
        List<int> Cancelled) Create(
        ProgramCompletionMode mode = ProgramCompletionMode.Stop,
        int? repeatCount = null,
        bool returnToStartOnFinish = false)
    {
        var saved = new List<(ProgramCompletionMode, int?, bool)>();
        var cancelled = new List<int>();
        var vm = new CompletionSettingsViewModel(
            mode, repeatCount, returnToStartOnFinish,
            (m, r, rts) => saved.Add((m, r, rts)),
            () => cancelled.Add(1));
        return (vm, saved, cancelled);
    }

    [Fact]
    public void Constructor_FiniteRepeatCount_IsRepeatUnlimitedFalse()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.Loop, repeatCount: 5);

        Assert.False(vm.IsRepeatUnlimited);
        Assert.Equal(5, vm.RepeatCount);
    }

    [Fact]
    public void Constructor_NullRepeatCount_IsRepeatUnlimitedTrue()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.PingPong, repeatCount: null);

        Assert.True(vm.IsRepeatUnlimited);
    }

    [Fact]
    public void ShowRepeatCount_FalseForStop_TrueForLoopAndPingPong()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.Stop);
        Assert.False(vm.ShowRepeatCount);

        vm.Mode = ProgramCompletionMode.Loop;
        Assert.True(vm.ShowRepeatCount);

        vm.Mode = ProgramCompletionMode.PingPong;
        Assert.True(vm.ShowRepeatCount);
    }

    [Fact]
    public void SwitchingToLoop_ClampsRepeatCountUpToLoopMinimum()
    {
        var (vm, _, _) = Create(ProgramCompletionMode.PingPong, repeatCount: 1);

        vm.Mode = ProgramCompletionMode.Loop;

        Assert.Equal(CompletionSettingsViewModel.LoopMinRepeatCount, vm.RepeatCount);
    }

    [Fact]
    public void Save_ClampsRepeatCountAboveMaximum()
    {
        var (vm, saved, _) = Create(ProgramCompletionMode.Loop, repeatCount: 2);
        vm.RepeatCount = 999;

        vm.SaveCommand.Execute(null);

        Assert.Equal((ProgramCompletionMode.Loop, (int?)CompletionSettingsViewModel.MaxRepeatCount, false), saved[0]);
    }

    [Fact]
    public void Save_Unlimited_PassesNullRepeatCount()
    {
        var (vm, saved, _) = Create(ProgramCompletionMode.Loop, repeatCount: 2);
        vm.IsRepeatUnlimited = true;

        vm.SaveCommand.Execute(null);

        Assert.Null(saved[0].RepeatCount);
    }

    [Fact]
    public void Save_StopMode_PassesNullRepeatCountRegardless()
    {
        var (vm, saved, _) = Create(ProgramCompletionMode.Stop);

        vm.SaveCommand.Execute(null);

        Assert.Equal(ProgramCompletionMode.Stop, saved[0].Mode);
        Assert.Null(saved[0].RepeatCount);
    }

    [Fact]
    public void Cancel_InvokesCancelCallback()
    {
        var (vm, _, cancelled) = Create();

        vm.CancelCommand.Execute(null);

        Assert.Single(cancelled);
    }
}
