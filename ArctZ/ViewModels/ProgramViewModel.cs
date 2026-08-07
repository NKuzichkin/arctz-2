using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
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
    private readonly IPeriodicTimer _progressTimer;
    private readonly TimeSpan _progressTickInterval;
    private JoystickAxisInput _leftInput;
    private JoystickAxisInput _rightInput;
    private bool _leftActive;
    private bool _rightActive;

    private double _animStartProgress;
    private double _animTargetProgress;
    private double _animDurationSeconds;
    private double _animElapsedSeconds;
    private double _cumulativeEstimatedSeconds;
    private double _cumulativeActualSeconds;
    private double _durationCalibrationFactor = 1.0;

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

    [ObservableProperty]
    private bool _isSideMenuOpen;

    /// <summary>Time-interpolated view of OverallProgress — moves continuously between
    /// ack-confirmed checkpoints instead of jumping only when a G-code line is acknowledged.</summary>
    [ObservableProperty]
    private double _displayProgress;

    public ProgramViewModel(ConnectionViewModel connection, IProgramStorage storage, ITrajectoryCompiler compiler, IPeriodicTimer progressTimer, TimeSpan progressTickInterval)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
        _progressTimer = progressTimer;
        _progressTickInterval = progressTickInterval;
        _progressTimer.Elapsed += OnProgressTick;
        Connection.PropertyChanged += OnConnectionPropertyChanged;

        // Add/Remove/Move/Reset all need to re-evaluate whether a given point
        // is still first/last, which MoveKeyPointUp/Down's CanExecute depends on.
        KeyPoints.CollectionChanged += (_, _) =>
        {
            MoveKeyPointUpCommand.NotifyCanExecuteChanged();
            MoveKeyPointDownCommand.NotifyCanExecuteChanged();
        };
    }

    private void OnProgressTick()
    {
        _animElapsedSeconds += _progressTickInterval.TotalSeconds;
        var frac = _animDurationSeconds <= 0 ? 1.0 : Math.Clamp(_animElapsedSeconds / _animDurationSeconds, 0, 1);
        DisplayProgress = _animStartProgress + (_animTargetProgress - _animStartProgress) * frac;
    }

    private void BeginStepAnimation(double startProgress, double targetProgress, double durationSeconds)
    {
        _animStartProgress = startProgress;
        _animTargetProgress = targetProgress;
        _animDurationSeconds = durationSeconds;
        _animElapsedSeconds = 0;
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

    [RelayCommand]
    private void ToggleSideMenu()
    {
        IsSideMenuOpen = !IsSideMenuOpen;
    }

    [RelayCommand]
    private void CloseSideMenu()
    {
        IsSideMenuOpen = false;
    }

    [RelayCommand]
    private void OpenGCodeLog()
    {
        Connection.IsGCodeLogOpen = true;
        IsSideMenuOpen = false;
    }

    [RelayCommand]
    private void OpenMockSettings()
    {
        Connection.IsMockSettingsOpen = true;
        IsSideMenuOpen = false;
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

    [ObservableProperty]
    private RenameProgramRequest? _pendingRename;

    private Task<string?> RequestNameAsync(string initialName)
    {
        var completion = new TaskCompletionSource<string?>();
        PendingRename = new RenameProgramRequest(initialName, completion);
        return completion.Task;
    }

    [RelayCommand]
    private void ConfirmRename()
    {
        var name = PendingRename?.Name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        PendingRename?.Completion.TrySetResult(name);
        PendingRename = null;
    }

    [RelayCommand]
    private void CancelRename()
    {
        PendingRename?.Completion.TrySetResult(null);
        PendingRename = null;
    }

    [RelayCommand]
    private async Task RenameProgramAsync()
    {
        var name = await RequestNameAsync(ProgramName);
        if (name is not null)
        {
            ProgramName = name;
        }
    }

    [RelayCommand]
    private async Task SaveProgramAsync()
    {
        if (ProgramId is null)
        {
            var name = await RequestNameAsync(ProgramName);
            if (name is null)
            {
                return;
            }

            ProgramName = name;
        }
        else
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

    private bool HasKnownPose() => Connection.Session?.DeviceStatus?.WPos is not null;

    [RelayCommand(CanExecute = nameof(HasKnownPose))]
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
    private async Task RemoveKeyPointAsync(KeyPoint keyPoint)
    {
        var confirmed = await ConfirmAsync($"Удалить точку «{keyPoint.Label}»? Действие нельзя отменить.");
        if (!confirmed)
        {
            return;
        }

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

    private bool CanMoveKeyPointUp(KeyPoint? keyPoint) => keyPoint is not null && KeyPoints.IndexOf(keyPoint) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveKeyPointUp))]
    private void MoveKeyPointUp(KeyPoint keyPoint)
    {
        var index = KeyPoints.IndexOf(keyPoint);
        if (index <= 0)
        {
            return;
        }

        KeyPoints.Move(index, index - 1);
        RenumberKeyPoints();
    }

    private bool CanMoveKeyPointDown(KeyPoint? keyPoint) => keyPoint is not null && KeyPoints.IndexOf(keyPoint) < KeyPoints.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveKeyPointDown))]
    private void MoveKeyPointDown(KeyPoint keyPoint)
    {
        var index = KeyPoints.IndexOf(keyPoint);
        if (index < 0 || index >= KeyPoints.Count - 1)
        {
            return;
        }

        KeyPoints.Move(index, index + 1);
        RenumberKeyPoints();
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

    private bool HasKnownPoseForKeyPoint(KeyPoint? _) => HasKnownPose();

    [RelayCommand(CanExecute = nameof(HasKnownPoseForKeyPoint))]
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

    private bool IsConnected(KeyPoint? _) => Connection.Session is not null;

    [RelayCommand(CanExecute = nameof(IsConnected))]
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

    private CancellationTokenSource? _terminalStatusResetCts;

    /// <summary>Overridable in tests so the terminal-state auto-reset doesn't require waiting the real delay.</summary>
    internal TimeSpan TerminalStatusResetDelay { get; set; } = TimeSpan.FromSeconds(4);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsProgramLocked))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(CurrentlyExecutingKeyPointId))]
    private PlaybackState _playbackState = PlaybackState.Idle;

    public bool IsProgramLocked => PlaybackState is PlaybackState.Running or PlaybackState.Paused;

    // Единый статус станка и программы — приоритет сверху вниз, первое совпадение побеждает.
    // MachineState.Alarm проверяется первым: авария обычно перекрыта отдельной блокирующей
    // модалкой (ConnectionViewModel.IsAlarmModalVisible), но эта модалка гейтится через
    // LastAlarmCode (устанавливается только пуш-строкой "ALARM:n"), а не через сам
    // MachineState.State из статус-репорта — при подключении к уже аварийному станку,
    // после ResetAlarmAsync (сбрасывает LastAlarmCode оптимистично, не дожидаясь
    // подтверждения от контроллера) или после смены Session модалка может быть не видна,
    // пока станок реально ещё в Alarm — тогда только эта ветка сигнализирует об этом.
    // MachineState.Run проверяется после Home: FluidNC подтверждает G-code-строку по
    // приёму в буфер, а не по завершению физического движения, так что PlaybackState
    // уходит в Completed на доли секунды раньше, чем станок реально останавливается —
    // в этот хвост движения показываем "Выполнение", а не "Завершено".
    // MachineState.Hold проверяется после Stopped: PauseAsync/StopAsync отправляют
    // FeedHoldAsync(), и станок остаётся в Hold, пока не придёт ResumeAsync() (следующий
    // Пуск) — этот Hold переживает автосброс терминальных состояний (Task 3, 4 секунды),
    // так что без отдельной ветки шапка после автосброса покажет "Ожидание", хотя станок
    // всё ещё физически удерживается и не двигается.
    public string StatusLabel
    {
        get
        {
            if (Connection.DeviceStatus?.State == MachineState.Alarm) return "АВАРИЯ";
            if (PlaybackState == PlaybackState.Faulted) return "Ошибка";
            if (PlaybackState == PlaybackState.Running) return "Выполнение";
            if (PlaybackState == PlaybackState.Paused) return "Пауза";
            if (Connection.DeviceStatus?.State == MachineState.Jog) return "Джог";
            if (Connection.DeviceStatus?.State == MachineState.Home) return "Homing";
            if (Connection.DeviceStatus?.State == MachineState.Run) return "Выполнение";
            if (PlaybackState == PlaybackState.Completed) return "Завершено";
            if (PlaybackState == PlaybackState.Stopped) return "Остановлено";
            if (Connection.DeviceStatus?.State == MachineState.Hold) return "Удержание";
            return "Ожидание";
        }
    }

    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        if (value == PlaybackState.Running)
        {
            _progressTimer.Start(_progressTickInterval);
        }
        else
        {
            _progressTimer.Stop();
        }

        Connection.IsPlaybackLocked = IsProgramLocked;

        if (IsProgramLocked && (_leftActive || _rightActive))
        {
            _leftActive = false;
            _rightActive = false;
            _leftInput = default;
            _rightInput = default;
            Connection.Session?.EndJog();
        }

        // Completed/Stopped/Faulted never resolved back to Idle on their own before this — the
        // operator had to press Play again to clear the label. Auto-reset after a delay, but
        // cancel it the moment we leave the terminal state (e.g. Play redispatches a fresh run)
        // so a stale reset can never stomp a freshly-started Running back to Idle.
        _terminalStatusResetCts?.Cancel();
        _terminalStatusResetCts = null;

        if (value is PlaybackState.Completed or PlaybackState.Stopped or PlaybackState.Faulted)
        {
            var cts = new CancellationTokenSource();
            _terminalStatusResetCts = cts;
            _ = ResetToIdleAfterDelayAsync(value, cts.Token);
        }
    }

    private async Task ResetToIdleAfterDelayAsync(PlaybackState terminalState, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TerminalStatusResetDelay, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (PlaybackState == terminalState)
        {
            PlaybackState = PlaybackState.Idle;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallProgress))]
    [NotifyPropertyChangedFor(nameof(CurrentlyExecutingKeyPointId))]
    private int? _currentSegmentIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallProgress))]
    private double _segmentProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallProgress))]
    private int _totalSegments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaultedMessage))]
    private int? _faultedAtSegmentIndex;

    // PlayAsync only resumes from a mid-program pause (PlaybackState.Paused);
    // from Faulted it recompiles and redispatches the whole program from the
    // first key point, so the recovery message needs to say that explicitly
    // rather than leave "what does Play do now" to the operator to guess.
    public string? FaultedMessage => FaultedAtSegmentIndex is { } index
        ? $"Ошибка на сегменте {index + 1} из {TotalSegments}. «Пуск» запустит программу заново с начала."
        : null;

    // CurrentSegmentIndex/SegmentProgress track a single in-flight segment (the
    // move between two adjacent key points), not the whole program — combining
    // them here is what turns "segment N is 100% done" into "the program overall
    // is at X%", so the bar keeps climbing across the whole run instead of
    // reading 100% the instant any one segment's step is acknowledged.
    public double OverallProgress => TotalSegments > 0 && CurrentSegmentIndex is { } index
        ? Math.Clamp((index + SegmentProgress) / TotalSegments, 0, 1)
        : 0;

    public Guid? CurrentlyExecutingKeyPointId
    {
        get
        {
            if (PlaybackState is not (PlaybackState.Running or PlaybackState.Paused))
            {
                return null;
            }

            var targetIndex = (CurrentSegmentIndex ?? -1) + 1;
            return targetIndex >= 0 && targetIndex < KeyPoints.Count
                ? KeyPoints[targetIndex].Id
                : null;
        }
    }

    private IDeviceSession? _subscribedSession;

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.DeviceStatus))
        {
            CaptureKeyPointCommand.NotifyCanExecuteChanged();
            FillKeyPointFromCurrentPositionCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(StatusLabel));
            return;
        }

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

        CaptureKeyPointCommand.NotifyCanExecuteChanged();
        FillKeyPointFromCurrentPositionCommand.NotifyCanExecuteChanged();
        MoveMachineToKeyPointCommand.NotifyCanExecuteChanged();
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
        DisplayProgress = 0;
        FaultedAtSegmentIndex = null;
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);
        _cumulativeEstimatedSeconds = 0;
        _cumulativeActualSeconds = 0;
        _durationCalibrationFactor = 1.0;

        var dispatched = new (CompiledStep Step, Task<CommandResult> Completion)[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var line = ((GCodeLineCommand)steps[i].Command).Line;
            dispatched[i] = (steps[i], Connection.Session!.SendGCodeAsync(line));
        }

        var previousDisplayProgress = 0.0;

        foreach (var (step, completion) in dispatched)
        {
            var targetProgress = TotalSegments > 0
                ? Math.Clamp((step.SegmentIndex + step.SegmentProgress) / TotalSegments, 0, 1)
                : 0;
            var correctedDuration = step.EstimatedDurationSeconds * _durationCalibrationFactor;
            BeginStepAnimation(previousDisplayProgress, targetProgress, correctedDuration);

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

            _cumulativeEstimatedSeconds += step.EstimatedDurationSeconds;
            _cumulativeActualSeconds += _animElapsedSeconds;
            _durationCalibrationFactor = _cumulativeEstimatedSeconds > 0
                ? _cumulativeActualSeconds / _cumulativeEstimatedSeconds
                : 1.0;

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
            DisplayProgress = targetProgress;
            previousDisplayProgress = targetProgress;
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
        DisplayProgress = 0;
        Connection.Session?.AbortPendingCommands();
        return Connection.Session?.FeedHoldAsync() ?? Task.CompletedTask;
    }
}
