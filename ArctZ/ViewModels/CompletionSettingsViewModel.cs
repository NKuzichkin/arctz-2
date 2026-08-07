using System;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

/// <summary>Editable draft of a program's completion behavior, shown in an overlay while editing.</summary>
public partial class CompletionSettingsViewModel : ViewModelBase
{
    public const int LoopMinRepeatCount = 2;
    public const int PingPongMinRepeatCount = 1;
    public const int MaxRepeatCount = 50;

    private readonly Action<ProgramCompletionMode, int?, bool> _onSave;
    private readonly Action _onCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRepeatCount))]
    private ProgramCompletionMode _mode;

    [ObservableProperty]
    private int _repeatCount;

    [ObservableProperty]
    private bool _isRepeatUnlimited;

    [ObservableProperty]
    private bool _returnToStartOnFinish;

    public bool ShowRepeatCount => Mode != ProgramCompletionMode.Stop;

    public CompletionSettingsViewModel(
        ProgramCompletionMode mode,
        int? repeatCount,
        bool returnToStartOnFinish,
        Action<ProgramCompletionMode, int?, bool> onSave,
        Action onCancel)
    {
        _onSave = onSave;
        _onCancel = onCancel;
        Mode = mode;
        IsRepeatUnlimited = repeatCount is null;
        RepeatCount = repeatCount ?? MinRepeatCountFor(mode);
        ReturnToStartOnFinish = returnToStartOnFinish;
    }

    private static int MinRepeatCountFor(ProgramCompletionMode mode) =>
        mode == ProgramCompletionMode.Loop ? LoopMinRepeatCount : PingPongMinRepeatCount;

    partial void OnModeChanged(ProgramCompletionMode value)
    {
        var min = MinRepeatCountFor(value);
        if (RepeatCount < min)
        {
            RepeatCount = min;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var min = MinRepeatCountFor(Mode);
        var clamped = Math.Clamp(RepeatCount, min, MaxRepeatCount);
        int? effectiveCount = Mode == ProgramCompletionMode.Stop || IsRepeatUnlimited ? null : clamped;
        _onSave(Mode, effectiveCount, ReturnToStartOnFinish);
    }

    [RelayCommand]
    private void Cancel() => _onCancel();
}
