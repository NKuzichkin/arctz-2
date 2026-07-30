using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

public partial class ProgramViewModel : ViewModelBase
{
    private readonly IProgramStorage _storage;
    private readonly ITrajectoryCompiler _compiler;
    private JoystickAxisInput _leftInput;
    private JoystickAxisInput _rightInput;
    private bool _leftActive;
    private bool _rightActive;

    public ConnectionViewModel Connection { get; }

    [ObservableProperty]
    private Guid? _programId;

    [ObservableProperty]
    private string _programName = "Новая программа";

    [ObservableProperty]
    private KeyPoint? _selectedKeyPoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingKeyPoint))]
    private KeyPointEditorViewModel? _keyPointEditor;

    public bool IsEditingKeyPoint => KeyPointEditor is not null;

    public ObservableCollection<KeyPoint> KeyPoints { get; } = new();

    public ObservableCollection<ProgramLibraryItem> Library { get; } = new();

    [ObservableProperty]
    private bool _isLibraryOpen;

    public ProgramViewModel(ConnectionViewModel connection, IProgramStorage storage, ITrajectoryCompiler compiler)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
        Connection.PropertyChanged += OnConnectionPropertyChanged;
    }

    [RelayCommand]
    private async Task RefreshLibraryAsync()
    {
        Library.Clear();
        foreach (var summary in await _storage.ListAsync())
        {
            Library.Add(new ProgramLibraryItem(summary, summary.Id == ProgramId));
        }
    }

    [RelayCommand]
    private async Task OpenLibraryAsync()
    {
        await RefreshLibraryAsync();
        IsLibraryOpen = true;
    }

    [RelayCommand]
    private void CloseLibrary()
    {
        IsLibraryOpen = false;
    }

    partial void OnProgramIdChanged(Guid? value)
    {
        foreach (var item in Library)
        {
            item.IsLoaded = item.Id == value;
        }
    }

    [RelayCommand]
    private void NewProgram()
    {
        ProgramId = null;
        ProgramName = "Новая программа";
        KeyPoints.Clear();
        SelectedKeyPoint = null;
    }

    [RelayCommand]
    private async Task LoadProgramAsync(ProgramLibraryItem summary)
    {
        var program = await _storage.LoadAsync(summary.Id);

        ProgramId = program.Id;
        ProgramName = program.Name;

        KeyPoints.Clear();
        foreach (var keyPoint in program.KeyPoints)
        {
            KeyPoints.Add(keyPoint);
        }

        SelectedKeyPoint = null;
        IsLibraryOpen = false;
    }

    [ObservableProperty]
    private ConfirmationRequest? _pendingConfirmation;

    private Task<bool> ConfirmAsync(string message)
    {
        var completion = new TaskCompletionSource<bool>();
        PendingConfirmation = new ConfirmationRequest(message, completion);
        return completion.Task;
    }

    [RelayCommand]
    private void ConfirmYes()
    {
        PendingConfirmation?.Completion.TrySetResult(true);
        PendingConfirmation = null;
    }

    [RelayCommand]
    private void ConfirmNo()
    {
        PendingConfirmation?.Completion.TrySetResult(false);
        PendingConfirmation = null;
    }

    [RelayCommand]
    private async Task SaveProgramAsync()
    {
        if (ProgramId is not null)
        {
            var confirmed = await ConfirmAsync(
                $"Сохранить поверх ранее сохранённой программы «{ProgramName}»? Текущие данные на диске будут перезаписаны.");
            if (!confirmed)
            {
                return;
            }
        }

        var hasNameCollision = Library.Any(item =>
            item.Id != ProgramId && string.Equals(item.Name.Trim(), ProgramName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (hasNameCollision)
        {
            var confirmed = await ConfirmAsync(
                $"В библиотеке уже есть программа с именем «{ProgramName}». Сохранить ещё одну с таким же именем?");
            if (!confirmed)
            {
                return;
            }
        }

        var program = BuildProgram();
        await _storage.SaveAsync(program);
        ProgramId = program.Id;
        await RefreshLibraryAsync();
    }

    [RelayCommand]
    private void CaptureKeyPoint()
    {
        var pose = Connection.Session?.DeviceStatus?.WPos;
        if (pose is null)
        {
            return;
        }

        var number = KeyPoints.Count + 1;
        KeyPoints.Add(new KeyPoint(
            Guid.NewGuid(),
            number,
            Label: $"Точка {number}",
            pose.Value,
            DwellSeconds: 0,
            FeedRateUnitsPerMin: 500,
            EaseMode.None,
            ContinuousBlend: false));
    }

    [RelayCommand]
    private void RemoveKeyPoint(KeyPoint keyPoint)
    {
        var index = KeyPoints.IndexOf(keyPoint);
        if (index < 0)
        {
            return;
        }

        KeyPoints.RemoveAt(index);
        RenumberKeyPoints();

        if (SelectedKeyPoint == keyPoint)
        {
            SelectedKeyPoint = null;
        }
    }

    private void RenumberKeyPoints()
    {
        for (var i = 0; i < KeyPoints.Count; i++)
        {
            if (KeyPoints[i].Number != i + 1)
            {
                KeyPoints[i] = KeyPoints[i] with { Number = i + 1 };
            }
        }
    }

    [RelayCommand]
    private void EditKeyPoint(KeyPoint keyPoint)
    {
        KeyPointEditor = new KeyPointEditorViewModel(keyPoint, ApplyKeyPointEdit, () => KeyPointEditor = null);
    }

    private void ApplyKeyPointEdit(KeyPoint updated)
    {
        var index = KeyPoints.IndexOf(KeyPoints.First(k => k.Id == updated.Id));
        KeyPoints[index] = updated;
        KeyPointEditor = null;
    }

    [RelayCommand]
    private void FillKeyPointFromCurrentPosition(KeyPoint keyPoint)
    {
        var pose = Connection.Session?.DeviceStatus?.WPos;
        if (pose is null)
        {
            return;
        }

        var index = KeyPoints.IndexOf(keyPoint);
        if (index < 0)
        {
            return;
        }

        KeyPoints[index] = keyPoint with { Pose = pose.Value };
    }

    [RelayCommand]
    private async Task MoveMachineToKeyPointAsync(KeyPoint keyPoint)
    {
        var session = Connection.Session;
        if (session is null)
        {
            return;
        }

        var pose = keyPoint.Pose;
        var line = $"G1 X{FormatAxis(pose.X)} Y{FormatAxis(pose.Y)} Z{FormatAxis(pose.Z)} A{FormatAxis(pose.A)} F{FormatAxis(keyPoint.FeedRateUnitsPerMin)}";
        await session.SendGCodeAsync(line);
    }

    private static string FormatAxis(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public void OnLeftJoystickDown(JoystickEventArgs e) => OnStickDown(isLeft: true, e);

    public void OnLeftJoystickMove(JoystickEventArgs e) => OnStickMove(isLeft: true, e);

    public void OnLeftJoystickUp(JoystickEventArgs e) => OnStickUp(isLeft: true);

    public void OnRightJoystickDown(JoystickEventArgs e) => OnStickDown(isLeft: false, e);

    public void OnRightJoystickMove(JoystickEventArgs e) => OnStickMove(isLeft: false, e);

    public void OnRightJoystickUp(JoystickEventArgs e) => OnStickUp(isLeft: false);

    private void OnStickDown(bool isLeft, JoystickEventArgs e)
    {
        var wasAnyActive = _leftActive || _rightActive;
        if (isLeft)
        {
            _leftActive = true;
        }
        else
        {
            _rightActive = true;
        }

        if (!wasAnyActive)
        {
            Connection.Session?.BeginJog();
        }

        OnStickMove(isLeft, e);
    }

    private void OnStickMove(bool isLeft, JoystickEventArgs e)
    {
        var input = JoystickInputMapper.ToAxisInput(e);
        if (isLeft)
        {
            _leftInput = input;
        }
        else
        {
            _rightInput = input;
        }

        Connection.Session?.UpdateJog(new DualJoystickState(_leftInput, _rightInput));
    }

    private void OnStickUp(bool isLeft)
    {
        if (isLeft)
        {
            _leftInput = default;
            _leftActive = false;
        }
        else
        {
            _rightInput = default;
            _rightActive = false;
        }

        if (!_leftActive && !_rightActive)
        {
            Connection.Session?.EndJog();
        }
        else
        {
            Connection.Session?.UpdateJog(new DualJoystickState(_leftInput, _rightInput));
        }
    }

    private bool _pausedForLinkLoss;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsProgramLocked))]
    private PlaybackState _playbackState = PlaybackState.Idle;

    public bool IsProgramLocked => PlaybackState is PlaybackState.Running or PlaybackState.Paused;

    [ObservableProperty]
    private int? _currentSegmentIndex;

    [ObservableProperty]
    private double _segmentProgress;

    [ObservableProperty]
    private int? _faultedAtSegmentIndex;

    private IDeviceSession? _subscribedSession;

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConnectionViewModel.Session))
        {
            return;
        }

        if (_subscribedSession is not null)
        {
            _subscribedSession.ConnectionStateChanged -= OnSessionConnectionStateChanged;
        }

        _subscribedSession = Connection.Session;

        if (_subscribedSession is not null)
        {
            _subscribedSession.ConnectionStateChanged += OnSessionConnectionStateChanged;
        }
    }

    private void OnSessionConnectionStateChanged()
    {
        var state = Connection.Session?.ConnectionState;

        if (state == ConnectionState.Reconnecting && PlaybackState == PlaybackState.Running)
        {
            _pausedForLinkLoss = true;
            PlaybackState = PlaybackState.Paused;
        }
        else if (state == ConnectionState.Disconnected && _pausedForLinkLoss)
        {
            _pausedForLinkLoss = false;
            PlaybackState = PlaybackState.Faulted;
        }
        // ConnectionState.Connected after Reconnecting: stays Paused — resuming is an explicit user action.

        PlayCommand.NotifyCanExecuteChanged();
    }

    private bool CanPlay() =>
        Connection.Session is not null &&
        PlaybackState != PlaybackState.Running &&
        (PlaybackState != PlaybackState.Paused || !_pausedForLinkLoss || Connection.Session.ConnectionState == ConnectionState.Connected);

    private bool CanPause() => PlaybackState == PlaybackState.Running && Connection.Session is not null;

    private bool CanStop() => PlaybackState is PlaybackState.Running or PlaybackState.Paused;

    private JibProgram BuildProgram()
    {
        var program = new JibProgram { Id = ProgramId ?? Guid.NewGuid(), Name = ProgramName };
        program.KeyPoints.AddRange(KeyPoints);
        return program;
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (!CanPlay())
        {
            return;
        }

        if (PlaybackState == PlaybackState.Paused)
        {
            _pausedForLinkLoss = false;
            PlaybackState = PlaybackState.Running;
            if (Connection.Session!.ConnectionState == ConnectionState.Connected)
            {
                await Connection.Session.ResumeAsync();
            }

            return;
        }

        // A prior StopAsync issues a feed hold; clear it before dispatching a
        // fresh program so the controller isn't left ignoring motion commands.
        if (Connection.Session!.ConnectionState == ConnectionState.Connected)
        {
            await Connection.Session.ResumeAsync();
        }

        var steps = _compiler.Compile(BuildProgram());
        if (steps.Count == 0)
        {
            return;
        }

        PlaybackState = PlaybackState.Running;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        FaultedAtSegmentIndex = null;

        var dispatched = new (CompiledStep Step, Task<CommandResult> Completion)[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var line = ((GCodeLineCommand)steps[i].Command).Line;
            dispatched[i] = (steps[i], Connection.Session!.SendGCodeAsync(line));
        }

        foreach (var (step, completion) in dispatched)
        {
            var result = await completion;

            if (PlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return;
            }

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
        }

        if (PlaybackState == PlaybackState.Running)
        {
            PlaybackState = PlaybackState.Completed;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task PauseAsync()
    {
        if (!CanPause())
        {
            return Task.CompletedTask;
        }

        PlaybackState = PlaybackState.Paused;
        return Connection.Session!.FeedHoldAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync()
    {
        if (!CanStop())
        {
            return Task.CompletedTask;
        }

        PlaybackState = PlaybackState.Stopped;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        Connection.Session?.AbortPendingCommands();
        return Connection.Session?.FeedHoldAsync() ?? Task.CompletedTask;
    }
}
