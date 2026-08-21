using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.App;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Commands;
using ArctZ.Services.Diagnostics;
using ArctZ.Services.Program;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI;

namespace ArctZ.ViewModels;

public partial class ProgramViewModel : ViewModelBase
{
    private readonly IProgramStorage _storage;
    private readonly ITrajectoryCompiler _compiler;
    private readonly IAppExitService _exitService;
    private readonly Func<DateTimeOffset> _now;
    private readonly DateTimeOffset _startedAt;
    private readonly IPeriodicTimer _progressTimer;
    private readonly TimeSpan _progressTimerInterval;
    private TimeProgressTracker? _progressTracker;
    private ProgramExecutionLog? _executionLog;
    private DateTimeOffset? _pausedAt;
    private JoystickAxisInput _leftInput;
    private JoystickAxisInput _rightInput;
    private bool _leftActive;
    private bool _rightActive;

    [ObservableProperty]
    private double _joystickSpeedPercent = 100;

    [ObservableProperty]
    private bool _isNarrowJoystickLayout;

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

    // Session-only: not part of the saved program, keyed by KeyPoint.Id (stable across edits —
    // EditKeyPoint/FillKeyPointFromCurrentPosition use `with`, which preserves Id).
    private readonly Dictionary<Guid, List<KeyPointMessage>> _keyPointMessages = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingKeyPointMessages))]
    [NotifyPropertyChangedFor(nameof(KeyPointMessagesTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedKeyPointMessages))]
    [NotifyPropertyChangedFor(nameof(HasNoKeyPointMessages))]
    private KeyPoint? _keyPointMessagesTarget;

    public bool IsShowingKeyPointMessages => KeyPointMessagesTarget is not null;

    public string KeyPointMessagesTitle => KeyPointMessagesTarget is { } point
        ? $"Сообщения — {point.Label}"
        : string.Empty;

    public IReadOnlyList<KeyPointMessage> SelectedKeyPointMessages => KeyPointMessagesTarget is { } point
        ? GetKeyPointMessages(point.Id)
        : Array.Empty<KeyPointMessage>();

    public bool HasNoKeyPointMessages => SelectedKeyPointMessages.Count == 0;

    public ObservableCollection<KeyPoint> KeyPoints { get; } = new();

    public ObservableCollection<ProgramLibraryItem> Library { get; } = new();

    [ObservableProperty]
    private bool _isLibraryOpen;

    [ObservableProperty]
    private bool _isSideMenuOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAboutOpen))]
    private AboutViewModel? _about;

    public bool IsAboutOpen => About is not null;

    /// <summary>Приложение уже закрывается и ждёт остановки станка. Пока флаг взведён,
    /// экран закрыт оверлеем: ожидание занимает до <see cref="DeviceStopTimeout"/>, и без
    /// него интерфейс выглядел бы зависшим. Состояние временное — снимается по завершении
    /// остановки, см. <see cref="ShutdownAsync"/>.</summary>
    [ObservableProperty]
    private bool _isShuttingDown;

    /// <summary>Станок остановлен и связь разорвана. В отличие от <see cref="IsShuttingDown"/>
    /// переживает завершение остановки: закрытие окна на Desktop идёт двумя заходами — первый
    /// отменяется ради остановки станка, второй должен по этому признаку пройти.</summary>
    public bool IsShutdownComplete { get; private set; }

    [ObservableProperty]
    private ProgramCompletionMode _completionMode = ProgramCompletionMode.Stop;

    [ObservableProperty]
    private bool _returnToStartOnFinish;

    [ObservableProperty]
    private int? _repeatCount;

    [ObservableProperty]
    private bool _isDirty;

    private bool _suppressDirtyTracking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingCompletionSettings))]
    private CompletionSettingsViewModel? _completionSettingsEditor;

    public bool IsEditingCompletionSettings => CompletionSettingsEditor is not null;

    public ProgramViewModel(
        ConnectionViewModel connection,
        IProgramStorage storage,
        ITrajectoryCompiler compiler,
        IAppExitService exitService,
        Func<DateTimeOffset>? now = null,
        IPeriodicTimer? progressTimer = null,
        TimeSpan? progressTimerInterval = null)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
        _exitService = exitService;
        _now = now ?? (() => DateTimeOffset.Now);
        _progressTimer = progressTimer ?? new SystemPeriodicTimer();
        _progressTimerInterval = progressTimerInterval ?? TimeSpan.FromMilliseconds(200);
        _progressTimer.Elapsed += OnProgressTimerElapsed;

        // This view model is a singleton built during startup, so its construction is the
        // closest thing to "the app started" available without a platform-specific process API.
        _startedAt = _now();
        Connection.PropertyChanged += OnConnectionPropertyChanged;

        // Add/Remove/Move/Reset all need to re-evaluate whether a given point
        // is still first/last, which MoveKeyPointUp/Down's CanExecute depends on.
        KeyPoints.CollectionChanged += (_, _) =>
        {
            MoveKeyPointUpCommand.NotifyCanExecuteChanged();
            MoveKeyPointDownCommand.NotifyCanExecuteChanged();
            MarkDirtyIfTracking();
        };
    }

    private void MarkDirtyIfTracking()
    {
        if (!_suppressDirtyTracking)
        {
            IsDirty = true;
        }
    }

    private void OnProgressTimerElapsed()
    {
        _progressTracker?.OnClockTick(_now());
    }

    /// <summary>Test hook: lets tests drive a progress-tracker clock tick without a real/manual
    /// periodic timer in play (this coordinator test constructs its own ProgramViewModel without
    /// wiring a ManualPeriodicTimer).</summary>
    internal void OnClockTickForTests() => OnProgressTimerElapsed();

    partial void OnProgramNameChanged(string value) => MarkDirtyIfTracking();

    partial void OnCompletionModeChanged(ProgramCompletionMode value) => MarkDirtyIfTracking();

    partial void OnReturnToStartOnFinishChanged(bool value) => MarkDirtyIfTracking();

    partial void OnRepeatCountChanged(int? value) => MarkDirtyIfTracking();

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

    [RelayCommand]
    private void OpenAbout()
    {
        About = new AboutViewModel(DiagnosticsReportBuilder.Build(CaptureDiagnostics()));
        IsSideMenuOpen = false;
    }

    [RelayCommand]
    private void CloseAbout()
    {
        About = null;
    }

    /// <summary>Freezes everything the report describes at the instant the dialog opens,
    /// so a machine that keeps talking cannot change the text out from under the reader.</summary>
    private DiagnosticsSnapshot CaptureDiagnostics() => new(
        HardwareInfo.Capture(_storage.Location),
        BuildInfo.Current,
        _now() - _startedAt,
        Connection.ConnectionStateLabel,
        Connection.SelectedEndpoint?.DisplayName,
        Connection.FirmwareBanner,
        ProgramName,
        KeyPoints.Count,
        StatusLabel,
        IsDirty,
        Connection.ErrorLog.Snapshot(),
        Connection.ExchangeLog.Snapshot());

    /// <summary>Сколько ждать от станка подтверждения, что его буфер пуст. Восемь status-отчётов
    /// при штатном опросе в 250 мс — с запасом на дребезг связи, но так, чтобы выход не подвисал
    /// заметно, когда устройство молчит.</summary>
    internal static readonly TimeSpan DeviceStopTimeout = TimeSpan.FromSeconds(2);

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (await ShutdownAsync())
        {
            _exitService.Exit();
        }
    }

    /// <summary>
    /// Приводит станок в безопасное состояние перед закрытием приложения. Вызывается из всех
    /// путей выхода (пункт меню и закрытие окна), поэтому останавливает устройство безусловно —
    /// шла программа, шёл джог или машина простаивала.
    /// </summary>
    /// <param name="confirmIfRunning">False на пути принудительного закрытия приложения
    /// (смахивание из недавних на Android): спросить там некого, а станок остановить
    /// обязательно.</param>
    /// <returns>False, если пользователь отказался от выхода; устройство при этом не тронуто.</returns>
    public async Task<bool> ShutdownAsync(bool confirmIfRunning = true)
    {
        IsSideMenuOpen = false;
        IsShutdownComplete = false;

        if (confirmIfRunning && IsProgramLocked)
        {
            var confirmed = await ConfirmAsync("Сейчас выполняется программа. Всё равно выйти из приложения?");
            if (!confirmed)
            {
                return false;
            }
        }

        // Гасит цикл диспетчеризации в PlayAsync до того, как он успеет дослать в очередь
        // очередной шаг поверх уже отправленной остановки.
        PlaybackState = PlaybackState.Stopped;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;

        IsShuttingDown = true;

        try
        {
            if (Connection.Session is { } session)
            {
                await session.StopAndDrainAsync(DeviceStopTimeout);
            }

            await Connection.DisconnectForShutdownAsync();
        }
        finally
        {
            // Снимается обязательно: на Android смахивание из недавних не убивает процесс —
            // StopSelf() лишь отпускает сервис, — и следующий запуск приложения Android отдаёт
            // тому же процессу вместе с этой ViewModel. Оставленный флаг закрыл бы новый запуск
            // оверлеем остановки навсегда.
            IsShuttingDown = false;
        }

        IsShutdownComplete = true;

        return true;
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
        _suppressDirtyTracking = true;
        try
        {
            ProgramId = null;
            ProgramName = "Новая программа";
            CompletionMode = ProgramCompletionMode.Stop;
            ReturnToStartOnFinish = false;
            RepeatCount = null;
            KeyPoints.Clear();
            _keyPointMessages.Clear();
            SelectedKeyPoint = null;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        IsDirty = false;
    }

    [RelayCommand]
    private async Task LoadProgramAsync(ProgramLibraryItem summary)
    {
        var program = await _storage.LoadAsync(summary.Id);

        _suppressDirtyTracking = true;
        try
        {
            ProgramId = program.Id;
            ProgramName = program.Name;
            CompletionMode = program.CompletionMode;
            ReturnToStartOnFinish = program.ReturnToStartOnFinish;
            RepeatCount = program.RepeatCount;

            KeyPoints.Clear();
            _keyPointMessages.Clear();
            foreach (var keyPoint in program.KeyPoints)
            {
                KeyPoints.Add(keyPoint);
            }

            SelectedKeyPoint = null;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        IsDirty = false;
        IsLibraryOpen = false;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBlockingDialogVisible))]
    private ConfirmationRequest? _pendingConfirmation;

    // A caller (e.g. PlayAsync's EnsureProgramSavedAsync) can be asked to show a dialog while
    // another one raised by an unrelated command is still pending — most commonly via the
    // Android foreground-notification "Продолжить" action, which invokes PlayCommand.Execute
    // directly and bypasses both CanExecute and the header's IsEnabled gate. Waiting for the
    // in-flight request instead of overwriting the field avoids orphaning its TaskCompletionSource
    // (see CLAUDE.md's async-dialog note and problems_20_08_2026.md #2).
    private async Task<bool> ConfirmAsync(string message)
    {
        while (PendingConfirmation is { } existing)
        {
            await existing.Completion.Task;
        }

        var completion = new TaskCompletionSource<bool>();
        PendingConfirmation = new ConfirmationRequest(message, completion);
        return await completion.Task;
    }

    // Clearing the field before TrySetResult (rather than after, as it read before this fix)
    // matters once a second caller can be queued on ConfirmAsync's while-loop above: without a
    // SynchronizationContext (e.g. in tests), TrySetResult runs its awaiters synchronously and
    // inline, so a queued waiter would otherwise observe the stale non-null field and spin.
    [RelayCommand]
    private void ConfirmYes()
    {
        var pending = PendingConfirmation;
        PendingConfirmation = null;
        pending?.Completion.TrySetResult(true);
    }

    [RelayCommand]
    private void ConfirmNo()
    {
        var pending = PendingConfirmation;
        PendingConfirmation = null;
        pending?.Completion.TrySetResult(false);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBlockingDialogVisible))]
    private RenameProgramRequest? _pendingRename;

    private async Task<string?> RequestNameAsync(string initialName)
    {
        while (PendingRename is { } existing)
        {
            await existing.Completion.Task;
        }

        var completion = new TaskCompletionSource<string?>();
        PendingRename = new RenameProgramRequest(initialName, completion);
        return await completion.Task;
    }

    [RelayCommand]
    private void ConfirmRename()
    {
        var name = PendingRename?.Name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var pending = PendingRename;
        PendingRename = null;
        pending?.Completion.TrySetResult(name);
    }

    [RelayCommand]
    private void CancelRename()
    {
        var pending = PendingRename;
        PendingRename = null;
        pending?.Completion.TrySetResult(null);
    }

    // TCS-gated dialogs (ConfirmAsync/RequestNameAsync above) must block header actions
    // (Пуск/Пауза/Стоп/Отключить) the same way Connection's own modals do — otherwise a
    // header command can open a second TCS-gated dialog while one is already pending,
    // orphaning the first TaskCompletionSource forever (see CLAUDE.md's async-dialog note).
    public bool IsAnyBlockingDialogVisible =>
        Connection.IsAnyModalVisible || PendingConfirmation is not null || PendingRename is not null;

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

        await PersistProgramAsync();
    }

    private async Task<bool> PersistProgramAsync()
    {
        var hasNameCollision = Library.Any(item =>
            item.Id != ProgramId && string.Equals(item.Name.Trim(), ProgramName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (hasNameCollision)
        {
            var confirmed = await ConfirmAsync(
                $"В библиотеке уже есть программа с именем «{ProgramName}». Сохранить ещё одну с таким же именем?");
            if (!confirmed)
            {
                return false;
            }
        }

        var program = BuildProgram();
        await _storage.SaveAsync(program);
        ProgramId = program.Id;
        IsDirty = false;
        await RefreshLibraryAsync();
        return true;
    }

    private async Task<bool> EnsureProgramSavedAsync()
    {
        if (ProgramId is null)
        {
            var name = await RequestNameAsync(ProgramName);
            if (name is null)
            {
                return false;
            }

            ProgramName = name;
            return await PersistProgramAsync();
        }

        if (IsDirty)
        {
            var confirmed = await ConfirmAsync(
                $"В программе «{ProgramName}» есть несохранённые изменения. Сохранить перед запуском?");
            if (!confirmed)
            {
                return false;
            }

            return await PersistProgramAsync();
        }

        return true;
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
            TransitionSeconds: InverseTimeMove.DefaultTransitionSeconds,
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
        _keyPointMessages.Remove(keyPoint.Id);

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

    [RelayCommand]
    private void ShowKeyPointMessages(KeyPoint keyPoint) => KeyPointMessagesTarget = keyPoint;

    [RelayCommand]
    private void CloseKeyPointMessages() => KeyPointMessagesTarget = null;

    public IReadOnlyList<KeyPointMessage> GetKeyPointMessages(Guid keyPointId) =>
        _keyPointMessages.TryGetValue(keyPointId, out var messages) ? messages : Array.Empty<KeyPointMessage>();

    private void AddKeyPointMessage(Guid keyPointId, KeyPointMessage message)
    {
        if (!_keyPointMessages.TryGetValue(keyPointId, out var messages))
        {
            messages = new List<KeyPointMessage>();
            _keyPointMessages[keyPointId] = messages;
        }

        if (!messages.Contains(message))
        {
            messages.Add(message);
        }
    }

    private void OnSegmentTimeOverage(int segmentIndex, double actualSeconds, double estimatedSeconds)
    {
        if (Services.Program.JibProgram.TargetKeyPoint(KeyPoints, segmentIndex, _currentPassBackward) is not { } keyPointId)
        {
            return;
        }

        AddKeyPointMessage(keyPointId, new KeyPointMessage(MessageLevel.Warning,
            $"Превышение фактического времени перемещения ({actualSeconds:F0} сек.) над установленным ({estimatedSeconds:F0} сек)"));

        var pointLabel = KeyPoints.FirstOrDefault(k => k.Id == keyPointId)?.Label;
        _executionLog?.LogTimeOverage(
            pointLabel, actualSeconds, estimatedSeconds, PhysicalOverallProgress, 1.0 - PhysicalPointRemainingFraction, _now());
    }

    [RelayCommand]
    private void EditCompletionSettings()
    {
        CompletionSettingsEditor = new CompletionSettingsViewModel(
            CompletionMode,
            RepeatCount,
            ReturnToStartOnFinish,
            ApplyCompletionSettingsEdit,
            () => CompletionSettingsEditor = null);
    }

    private void ApplyCompletionSettingsEdit(ProgramCompletionMode mode, int? repeatCount, bool returnToStartOnFinish)
    {
        CompletionMode = mode;
        RepeatCount = repeatCount;
        ReturnToStartOnFinish = returnToStartOnFinish;
        CompletionSettingsEditor = null;
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
        var line = InverseTimeMove.Line(pose, keyPoint.TransitionSeconds);
        await session.SendGCodeAsync(line);
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

        Connection.Session?.UpdateJog(new DualJoystickState(ScaledLeftInput(), ScaledRightInput()));
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
            Connection.Session?.UpdateJog(new DualJoystickState(ScaledLeftInput(), ScaledRightInput()));
        }
    }

    partial void OnJoystickSpeedPercentChanged(double value)
    {
        if (_leftActive || _rightActive)
        {
            Connection.Session?.UpdateJog(new DualJoystickState(ScaledLeftInput(), ScaledRightInput()));
        }
    }

    private JoystickAxisInput ScaledLeftInput() => JoystickSpeedScaler.Scale(_leftInput, JoystickSpeedPercent);

    private JoystickAxisInput ScaledRightInput() => JoystickSpeedScaler.Scale(_rightInput, JoystickSpeedPercent);

    private bool _pausedForLinkLoss;
    private bool _currentPassBackward;
    private Guid? _lastLoggedPhysicalKeyPointId;
    private bool _ackDesyncLogged;

    // Written on the PlayAsync continuation thread, read on the transport's reader thread
    // (OnSessionDeviceStatusChanged) — volatile so a read there can never observe a stale/torn
    // reference from a write on the other thread.
    private volatile TaskCompletionSource<bool>? _motionIdleSignal;

    // Written on the PlayAsync continuation thread (armed inside ResolveRunningStateAsync when a
    // pass/cycle boundary observes Paused) and resolved by OnPlaybackStateChanged on every
    // subsequent transition. Mirrors _motionIdleSignal's role but for a different gap: a boundary
    // Paused has no pending await keeping PlayAsync's coroutine alive, unlike a mid-pass Pause
    // (which the already-in-flight ack await absorbs for free — see
    // Pause_DuringTheMotionTail_DoesNotCancelTheWait_AndResumeCompletesTheRun).
    private volatile TaskCompletionSource<bool>? _pauseResumeSignal;

    /// <summary>
    /// Test hook: true once PlayAsync is waiting for the machine to report Idle. Tests drive a fake
    /// transport with no status poller behind it, so a status report fired before the wait is armed
    /// would be dropped with no second report to recover from — the real app re-polls every 100ms.
    /// </summary>
    internal bool IsAwaitingMotionIdle => _motionIdleSignal is not null;

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
        // Captured before ClearProgressTracker() (below) can null out _progressTracker — logging
        // after that point would record 0% for a run stopped/faulted mid-motion instead of its
        // actual progress at that instant. See
        // docs/superpowers/specs/2026-08-21-program-execution-log-design.md.
        var loggedOverallProgress = PhysicalOverallProgress;
        var loggedStepProgress = 1.0 - PhysicalPointRemainingFraction;

        _pauseResumeSignal?.TrySetResult(true);

        // Stop/Faulted abandon the run outright, so nothing is left to wait for. Paused is
        // deliberately absent: a held machine reports Hold rather than Idle, so the same wait
        // simply continues once the operator resumes — no resume-side bookkeeping needed.
        if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _motionIdleSignal?.TrySetResult(false);
            ClearProgressTracker();
        }

        if (value == PlaybackState.Paused)
        {
            _pausedAt = _now();
            _executionLog?.LogPauseStarted(loggedOverallProgress, loggedStepProgress, _now());
        }
        else if (value == PlaybackState.Running && _pausedAt is { } pausedAt)
        {
            _progressTracker?.ShiftForPause(_now() - pausedAt);
            _pausedAt = null;
            _executionLog?.LogPauseEnded(loggedOverallProgress, loggedStepProgress, _now());
        }
        else if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _pausedAt = null;
        }

        if (value == PlaybackState.Running)
        {
            _progressTimer.Start(_progressTimerInterval);
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
            _executionLog?.LogProgramEnded(StatusLabel, loggedOverallProgress, loggedStepProgress, _now());

            var cts = new CancellationTokenSource();
            _terminalStatusResetCts = cts;
            _ = ResetToIdleAfterDelayAsync(value, cts.Token);
        }
    }

    /// <summary>
    /// The controller acknowledges a G-code line when it buffers it, not when the move finishes,
    /// so the last ack can land tens of seconds before the machine physically stops. Waiting for
    /// the first status report that says Idle is what makes PlaybackState.Completed mean "the
    /// program actually finished" — the joystick unlock keys off it. No timeout: any timeout
    /// would declare Completed while the machine might still be moving, which is the exact
    /// defect this fixes. The escape hatches
    /// are Stop (enabled throughout the wait) and the link-loss path, which faults the run.
    /// </summary>
    private async Task WaitForMotionToFinishAsync()
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _motionIdleSignal = signal;

        try
        {
            await signal.Task;
        }
        finally
        {
            // Only clear the signal if it is still this run's own signal. Stop/Faulted resolve
            // the wait via TrySetResult but the awaiting continuation (RunContinuationsAsynchronously)
            // may not run until after a subsequent Play has already armed a new signal for a new
            // run — an unconditional clear here would strand that newer run waiting forever.
            Interlocked.CompareExchange(ref _motionIdleSignal, null, signal);
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

    public Guid? CurrentlyExecutingKeyPointId => PlaybackState is PlaybackState.Running or PlaybackState.Paused
        ? Services.Program.JibProgram.TargetKeyPoint(KeyPoints, CurrentSegmentIndex, _currentPassBackward)
        : null;

    public double PhysicalOverallProgress => _progressTracker?.OverallFraction ?? 0;

    /// <summary>Text of the most recently STARTED run's log — survives that run's own completion,
    /// replaced only by the next cold Play start. Null until the first Play of the session.</summary>
    public string? ExecutionLogText => _executionLog?.Text;

    public double PhysicalPointRemainingFraction => _progressTracker switch
    {
        null => 1.0,
        var tracker => 1.0 - tracker.CurrentStepFraction,
    };

    public bool PhysicalPointHasTimeWarning => _progressTracker?.CurrentPointHasWarning ?? false;

    public Guid? PhysicallyExecutingKeyPointId => _progressTracker is null
        ? null
        : Services.Program.JibProgram.TargetKeyPoint(KeyPoints, _progressTracker.CurrentSegmentIndex, _currentPassBackward);

    private void OnProgressTrackerChanged()
    {
        OnPropertyChanged(nameof(PhysicalOverallProgress));
        OnPropertyChanged(nameof(PhysicalPointRemainingFraction));
        OnPropertyChanged(nameof(PhysicalPointHasTimeWarning));
        OnPropertyChanged(nameof(PhysicallyExecutingKeyPointId));

        LogPhysicalMovementTransitionIfChanged();
        LogAckDesyncIfNewlyDetected();
    }

    /// <summary>Movement start/end events are driven by the physically active key point (not
    /// ack) — see docs/superpowers/specs/2026-08-21-program-execution-log-design.md. A transition
    /// produces an "ended" line for the point being left (if any) immediately followed by a
    /// "started" line for the point being entered (if any), both stamped with the same instant.</summary>
    private void LogPhysicalMovementTransitionIfChanged()
    {
        var current = PhysicallyExecutingKeyPointId;
        if (current == _lastLoggedPhysicalKeyPointId)
        {
            return;
        }

        // A transition straight to null only happens via ClearProgressTracker() (Stopped/Faulted),
        // which nulls _progressTracker BEFORE this fires — PhysicalOverallProgress/step-progress
        // would already read 0 here, not the run's actual progress at the moment it was stopped.
        // Nothing to log: OnPlaybackStateChanged's "Программа завершена" bookend already captures
        // and reports the correct progress for this same instant (captured before the clear).
        if (current is null)
        {
            _lastLoggedPhysicalKeyPointId = null;
            return;
        }

        var now = _now();
        var overallProgress = PhysicalOverallProgress;
        var stepProgress = 1.0 - PhysicalPointRemainingFraction;

        if (_lastLoggedPhysicalKeyPointId is { } previousId
            && KeyPoints.FirstOrDefault(k => k.Id == previousId) is { } previousPoint)
        {
            _executionLog?.LogMovementEnded(previousPoint.Label, overallProgress, stepProgress, now);
        }

        if (KeyPoints.FirstOrDefault(k => k.Id == current) is { } currentPoint)
        {
            _executionLog?.LogMovementStarted(currentPoint.Label, overallProgress, stepProgress, now);
        }

        _lastLoggedPhysicalKeyPointId = current;
    }

    /// <summary>Edge-triggered: logs once when the ack-confirmed segment gets more than one point
    /// ahead of the physically active one, then stays silent until the gap closes back to ≤1 and
    /// widens past the threshold again. No "recovered" line is logged — see the design doc.</summary>
    private void LogAckDesyncIfNewlyDetected()
    {
        if (_progressTracker is not { } tracker
            || CurrentSegmentIndex is not { } ackIndex
            || tracker.CurrentSegmentIndex is not { } physicalIndex)
        {
            return;
        }

        if (ackIndex - physicalIndex > 1)
        {
            if (!_ackDesyncLogged)
            {
                _executionLog?.LogAckDesync(
                    ackIndex, physicalIndex, PhysicalOverallProgress, 1.0 - PhysicalPointRemainingFraction, _now());
                _ackDesyncLogged = true;
            }
        }
        else
        {
            _ackDesyncLogged = false;
        }
    }

    private void ClearProgressTracker()
    {
        if (_progressTracker is null)
        {
            return;
        }

        DetachProgressTracker();
        _progressTracker = null;
        OnProgressTrackerChanged();
    }

    /// <summary>
    /// Unsubscribes the current tracker and flushes its active segment's overage (covers the last
    /// point of a pass, which never gets a "next segment" transition to report it naturally).
    /// Callers that immediately assign a new pass must call this BEFORE updating
    /// <see cref="_currentPassBackward"/> — the flush resolves the outgoing point's Id using
    /// whatever direction the pass that's ending actually ran in, not the next one.
    /// </summary>
    private void DetachProgressTracker()
    {
        if (_progressTracker is null)
        {
            return;
        }

        _progressTracker.FlushCurrentSegment(_now());
        _progressTracker.Changed -= OnProgressTrackerChanged;
        _progressTracker.SegmentTimeOverage -= OnSegmentTimeOverage;
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

        if (e.PropertyName == nameof(ConnectionViewModel.IsAnyModalVisible))
        {
            OnPropertyChanged(nameof(IsAnyBlockingDialogVisible));
            return;
        }

        if (e.PropertyName != nameof(ConnectionViewModel.Session))
        {
            return;
        }

        if (_subscribedSession is not null)
        {
            _subscribedSession.ConnectionStateChanged -= OnSessionConnectionStateChanged;
            _subscribedSession.DeviceStatusChanged -= OnSessionDeviceStatusChanged;
        }

        _subscribedSession = Connection.Session;

        if (_subscribedSession is not null)
        {
            _subscribedSession.ConnectionStateChanged += OnSessionConnectionStateChanged;
            _subscribedSession.DeviceStatusChanged += OnSessionDeviceStatusChanged;
        }

        CaptureKeyPointCommand.NotifyCanExecuteChanged();
        FillKeyPointFromCurrentPositionCommand.NotifyCanExecuteChanged();
        MoveMachineToKeyPointCommand.NotifyCanExecuteChanged();
    }

    // Deliberately driven by the session's own per-report event rather than
    // ConnectionViewModel.DeviceStatus: that mirrored property is [Reactive] and so skips
    // notification when a report is structurally identical to the previous one. A program whose
    // moves cover zero distance produces exactly that — the machine never leaves Idle and WPos
    // never changes — which would strand PlayAsync waiting for a notification that never comes.
    private void OnSessionDeviceStatusChanged()
    {
        var status = Connection.Session?.DeviceStatus;
        if (status is { } value && PlaybackState != PlaybackState.Paused)
        {
            _progressTracker?.OnPositionUpdated(value.WPos, _now());
        }

        if (status?.State == MachineState.Idle)
        {
            _motionIdleSignal?.TrySetResult(true);
        }
    }

    /// <summary>
    /// DeviceSession поднимает ConnectionStateChanged из того потока, где менялось состояние: на
    /// Desktop подключение завершается синхронно на UI-потоке, а на Android — в пуле потоков.
    /// Тело обработчика трогает UI-привязанные команды и свойства, поэтому из фонового потока
    /// падало исключением. Событие многоадресное — упавший обработчик отменяет вызов всех
    /// следующих подписчиков, из-за чего ConnectionViewModel не получал Connected и экран
    /// навсегда оставался в "Подключение" (исключение при этом гасил SerialEventQueue, так что в
    /// логах не было ни следа). Маршалим на главный поток — в тестах MainThreadScheduler
    /// подменён на ImmediateScheduler, поэтому поведение там остаётся синхронным.
    /// </summary>
    private void OnSessionConnectionStateChanged() =>
        RxSchedulers.MainThreadScheduler.Schedule(ApplySessionConnectionState);

    private void ApplySessionConnectionState()
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

    private bool _startingPlayback;

    private bool CanPlay() =>
        Connection.Session is not null &&
        !_startingPlayback &&
        PlaybackState != PlaybackState.Running &&
        Connection.Session.ConnectionState == ConnectionState.Connected;

    private bool CanPause() => PlaybackState == PlaybackState.Running && Connection.Session is not null;

    private bool CanStop() => PlaybackState is PlaybackState.Running or PlaybackState.Paused;

    private JibProgram BuildProgram()
    {
        var program = new JibProgram
        {
            Id = ProgramId ?? Guid.NewGuid(),
            Name = ProgramName,
            CompletionMode = CompletionMode,
            ReturnToStartOnFinish = ReturnToStartOnFinish,
            RepeatCount = RepeatCount
        };
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

        _startingPlayback = true;
        PlayCommand.NotifyCanExecuteChanged();

        IReadOnlyList<CompiledStep> forwardSteps;
        IReadOnlyList<CompiledStep>? backwardSteps;
        try
        {
            if (!await EnsureProgramSavedAsync())
            {
                return;
            }

            // A prior StopAsync issues a feed hold; clear it before dispatching a
            // fresh program so the controller isn't left ignoring motion commands.
            if (Connection.Session!.ConnectionState == ConnectionState.Connected)
            {
                await Connection.Session.ResumeAsync();
            }

            var forwardProgram = BuildProgram();
            forwardSteps = _compiler.Compile(forwardProgram);
            if (forwardSteps.Count == 0)
            {
                return;
            }

            backwardSteps = CompletionMode == ProgramCompletionMode.PingPong
                ? _compiler.Compile(ReversedProgram(forwardProgram))
                : null;

            PlaybackState = PlaybackState.Running;
        }
        finally
        {
            _startingPlayback = false;
            PlayCommand.NotifyCanExecuteChanged();
        }

        CurrentSegmentIndex = null;
        SegmentProgress = 0;
        FaultedAtSegmentIndex = null;
        // With ReturnToStartOnFinish, the program is treated as one extra step (1-2-3-1 for a
        // 3-point program) throughout progress tracking, not just at dispatch time.
        TotalSegments = KeyPoints.Count + (ReturnToStartOnFinish ? 1 : 0);
        _executionLog = new ProgramExecutionLog(ProgramName, KeyPoints.Count, _now());

        var cycle = 0;
        while (true)
        {
            if (!await RunPassAsync(forwardSteps, backward: false))
            {
                return;
            }

            if (backwardSteps is not null)
            {
                if (!await RunPassAsync(backwardSteps, backward: true))
                {
                    return;
                }
            }

            cycle++;

            var isLastCycle = CompletionMode == ProgramCompletionMode.Stop
                || (RepeatCount is int repeatLimit && cycle >= repeatLimit);
            if (isLastCycle)
            {
                break;
            }

            if (CompletionMode == ProgramCompletionMode.Loop)
            {
                if (!await RunReturnToStartMoveAsync())
                {
                    return;
                }
            }
        }

        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        // Extending the tracker's estimate BEFORE this wait (rather than after, once the return
        // move is actually dispatched) matters: without it, the still-15s-for-a-3-point-pass
        // estimate saturates OverallFraction at 100% for however long this wait takes (real motion
        // routinely overruns the naive per-point estimate — see JogTrace/hardware notes elsewhere),
        // and the bar sits pinned at 100% before the return move even starts. Extending here gives
        // it headroom so it only actually reaches 100% once the return move itself finishes.
        var returnSteps = ReturnToStartOnFinish ? BuildReturnToStartSteps() : null;
        if (returnSteps is not null)
        {
            _progressTracker?.Extend(returnSteps, _now());
            OnProgressTrackerChanged();
        }

        await WaitForMotionToFinishAsync();

        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        if (returnSteps is not null)
        {
            if (!await RunReturnToStartStepAsync(returnSteps))
            {
                return;
            }

            await WaitForMotionToFinishAsync();

            if (PlaybackState != PlaybackState.Running)
            {
                return;
            }
        }

        PlaybackState = PlaybackState.Completed;
    }

    /// <summary>
    /// Compiles the ReturnToStartOnFinish move as the program's own synthetic (N+1)th step — a
    /// real move from wherever the last pass ended to the first key point, using that point's own
    /// ease/dwell/transition settings (so it truly replays key point 1, not a bare line) — and
    /// remaps its segment index to KeyPoints.Count so it participates in TotalSegments/progress
    /// tracking/highlighting exactly like any other step. JibProgram.Segments() always emits a
    /// leading zero-distance self-move segment (index 0) before the real move (index 1); only the
    /// real move is kept.
    /// </summary>
    private IReadOnlyList<CompiledStep> BuildReturnToStartSteps()
    {
        var from = _currentPassBackward ? KeyPoints[0] : KeyPoints[^1];
        var miniProgram = new JibProgram();
        miniProgram.KeyPoints.Add(from);
        miniProgram.KeyPoints.Add(KeyPoints[0]);

        return _compiler.Compile(miniProgram)
            .Where(step => step.SegmentIndex == 1)
            .Select(step => step with { SegmentIndex = KeyPoints.Count })
            .ToList();
    }

    private static JibProgram ReversedProgram(JibProgram source)
    {
        var reversed = new JibProgram { Id = source.Id, Name = source.Name };
        reversed.KeyPoints.AddRange(source.KeyPoints.AsEnumerable().Reverse());
        return reversed;
    }

    /// <summary>
    /// Called at the start and end of every pass/return-move dispatch to decide whether the cycle
    /// loop should keep going. Stopped/Faulted resolve immediately to false — the run is
    /// abandoned, nothing to wait for. Paused means the operator clicked Pause exactly at this
    /// boundary, where — unlike mid-pass — there is no pending await to keep this coroutine alive
    /// naturally; returning false here would silently strand the run (PlaybackState stuck on
    /// Paused, IsProgramLocked never clears, no coroutine left for a later Play click's resume
    /// branch to wake). So this waits for whatever ends the pause instead: a resume (a fresh Play
    /// click's resume branch sets PlaybackState back to Running) or an abandon (Stop/Fault).
    /// OnPlaybackStateChanged resolves _pauseResumeSignal on every transition, so the signal is
    /// armed before being checked — any transition either lands before arming (caught by the
    /// PlaybackState re-check right after arming) or after (caught by the resolve).
    /// </summary>
    private async Task<bool> ResolveRunningStateAsync()
    {
        while (PlaybackState == PlaybackState.Paused)
        {
            var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pauseResumeSignal = signal;

            if (PlaybackState != PlaybackState.Paused)
            {
                break;
            }

            await signal.Task;
        }

        return PlaybackState == PlaybackState.Running;
    }

    private async Task<bool> RunReturnToStartMoveAsync()
    {
        if (!await ResolveRunningStateAsync())
        {
            return false;
        }

        var start = KeyPoints[0];
        var line = InverseTimeMove.Line(start.Pose, start.TransitionSeconds);
        var result = await Connection.Session!.SendGCodeAsync(line);

        if (PlaybackState == PlaybackState.Stopped)
        {
            return false;
        }

        if (result.Outcome != CommandOutcome.Acknowledged)
        {
            PlaybackState = PlaybackState.Faulted;
            return false;
        }

        return await ResolveRunningStateAsync();
    }

    private async Task<bool> RunPassAsync(IReadOnlyList<CompiledStep> steps, bool backward)
    {
        if (!await ResolveRunningStateAsync())
        {
            return false;
        }

        // Must run before _currentPassBackward is overwritten below — see DetachProgressTracker's
        // doc comment for why (it flushes the OLD tracker using the OLD pass's direction).
        DetachProgressTracker();

        _currentPassBackward = backward;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;

        var startingPose = Connection.Session?.DeviceStatus?.WPos ?? MachinePose.Zero;
        _progressTracker = new TimeProgressTracker(steps, startingPose, _now());
        _progressTracker.Changed += OnProgressTrackerChanged;
        _progressTracker.SegmentTimeOverage += OnSegmentTimeOverage;
        _lastLoggedPhysicalKeyPointId = null; // a fresh pass starts its own movement-transition tracking, even if its first point is the same KeyPoint the previous pass ended on (PingPong)
        _ackDesyncLogged = false;
        OnProgressTrackerChanged();

        return await DispatchStepsAsync(steps);
    }

    /// <summary>
    /// Dispatches the ReturnToStartOnFinish move as the current pass's own trailing (N+1)th step
    /// rather than a new pass. The caller must have already extended _progressTracker with these
    /// same <paramref name="steps"/> (see TimeProgressTracker.Extend) — done separately, and
    /// earlier than this dispatch, so PhysicalOverallProgress has the bigger total estimate in
    /// place for the wait that precedes this call too, not just for the move itself; see the
    /// call site in PlayAsync for why that timing matters.
    /// </summary>
    private async Task<bool> RunReturnToStartStepAsync(IReadOnlyList<CompiledStep> steps)
    {
        if (!await ResolveRunningStateAsync())
        {
            return false;
        }

        return await DispatchStepsAsync(steps);
    }

    private async Task<bool> DispatchStepsAsync(IReadOnlyList<CompiledStep> steps)
    {
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
                return false;
            }

            if (result.Outcome != CommandOutcome.Acknowledged)
            {
                PlaybackState = PlaybackState.Faulted;
                FaultedAtSegmentIndex = step.SegmentIndex;
                return false;
            }

            CurrentSegmentIndex = step.SegmentIndex;
            SegmentProgress = step.SegmentProgress;
        }

        return await ResolveRunningStateAsync();
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
