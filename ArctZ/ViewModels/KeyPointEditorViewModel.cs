using System;
using ArctZ.Services.Device;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

/// <summary>Editable draft of a KeyPoint's coordinates, transition time and dwell time, shown in an overlay while editing.</summary>
public partial class KeyPointEditorViewModel : ViewModelBase
{
    private readonly KeyPoint _source;
    private readonly Action<KeyPoint> _onSave;
    private readonly Action _onCancel;

    [ObservableProperty]
    private string? _label;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _z;

    [ObservableProperty]
    private double _a;

    [ObservableProperty]
    private double _transitionSeconds;

    [ObservableProperty]
    private double _dwellSeconds;

    public int Number => _source.Number;

    public KeyPointEditorViewModel(KeyPoint source, Action<KeyPoint> onSave, Action onCancel)
    {
        _source = source;
        _onSave = onSave;
        _onCancel = onCancel;
        Label = source.Label;
        X = source.Pose.X;
        Y = source.Pose.Y;
        Z = source.Pose.Z;
        A = source.Pose.A;
        TransitionSeconds = source.TransitionSeconds;
        DwellSeconds = source.DwellSeconds;
    }

    [RelayCommand]
    private void Save() => _onSave(_source with
    {
        Label = Label,
        Pose = new MachinePose(X, Y, Z, A),
        TransitionSeconds = TransitionSeconds,
        DwellSeconds = DwellSeconds
    });

    [RelayCommand]
    private void Cancel() => _onCancel();
}
