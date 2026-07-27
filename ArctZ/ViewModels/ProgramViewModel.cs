using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private ProgramMode _mode = ProgramMode.Authoring;

    [ObservableProperty]
    private Guid? _programId;

    [ObservableProperty]
    private string _programName = "Новая программа";

    [ObservableProperty]
    private Waypoint? _selectedWaypoint;

    public ObservableCollection<Waypoint> Waypoints { get; } = new();

    /// <summary>Transitions[i] describes the move from Waypoints[i] to Waypoints[i+1] — kept in sync by CaptureWaypoint/RemoveWaypoint.</summary>
    public ObservableCollection<TransitionSettings> Transitions { get; } = new();

    public ObservableCollection<ProgramSummary> Library { get; } = new();

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
        foreach (var summary in await _storage.ListAsync().ConfigureAwait(false))
        {
            Library.Add(summary);
        }
    }

    [RelayCommand]
    private void NewProgram()
    {
        ProgramId = null;
        ProgramName = "Новая программа";
        Waypoints.Clear();
        Transitions.Clear();
        SelectedWaypoint = null;
    }

    [RelayCommand]
    private async Task LoadProgramAsync(ProgramSummary summary)
    {
        var program = await _storage.LoadAsync(summary.Id).ConfigureAwait(false);

        ProgramId = program.Id;
        ProgramName = program.Name;

        Waypoints.Clear();
        foreach (var waypoint in program.Waypoints)
        {
            Waypoints.Add(waypoint);
        }

        Transitions.Clear();
        foreach (var transition in program.Transitions)
        {
            Transitions.Add(transition);
        }

        SelectedWaypoint = null;
    }

    [RelayCommand]
    private async Task SaveProgramAsync()
    {
        var program = new JibProgram { Id = ProgramId ?? Guid.NewGuid(), Name = ProgramName };
        program.Waypoints.AddRange(Waypoints);
        program.Transitions.AddRange(Transitions);

        await _storage.SaveAsync(program).ConfigureAwait(false);
        ProgramId = program.Id;
        await RefreshLibraryAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void CaptureWaypoint()
    {
        var pose = Connection.Session?.DeviceStatus?.WPos;
        if (pose is null)
        {
            return;
        }

        Waypoints.Add(new Waypoint(Guid.NewGuid(), Label: null, pose.Value));

        if (Waypoints.Count > 1)
        {
            Transitions.Add(new TransitionSettings(FeedRateUnitsPerMin: 500, DwellSeconds: 0, EaseMode.None, ContinuousBlend: false));
        }
    }

    [RelayCommand]
    private void RemoveWaypoint(Waypoint waypoint)
    {
        var index = Waypoints.IndexOf(waypoint);
        if (index < 0)
        {
            return;
        }

        Waypoints.RemoveAt(index);

        if (Transitions.Count > 0)
        {
            var transitionIndexToRemove = Math.Min(Math.Max(0, index - 1), Transitions.Count - 1);
            Transitions.RemoveAt(transitionIndexToRemove);
        }

        if (SelectedWaypoint == waypoint)
        {
            SelectedWaypoint = null;
        }
    }

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
    private PlaybackState _playbackState = PlaybackState.Idle;

    [ObservableProperty]
    private int? _currentSegmentIndex;

    [ObservableProperty]
    private double _segmentProgress;

    [ObservableProperty]
    private int? _faultedAtSegmentIndex;

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.Session) && Connection.Session is not null)
        {
            Connection.Session.ConnectionStateChanged += OnSessionConnectionStateChanged;
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
        program.Waypoints.AddRange(Waypoints);
        program.Transitions.AddRange(Transitions);
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
                await Connection.Session.ResumeAsync().ConfigureAwait(false);
            }

            return;
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
            var result = await completion.ConfigureAwait(false);

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
        return Connection.Session?.FeedHoldAsync() ?? Task.CompletedTask;
    }
}
