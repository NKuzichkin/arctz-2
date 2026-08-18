using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
        Func<DateTimeOffset>? now = null)
    {
        Connection = connection;
        _storage = storage;
        _compiler = compiler;
        _exitService = exitService;
        _now = now ?? (() => DateTimeOffset.Now);

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
        _pauseResumeSignal?.TrySetResult(true);

        // Stop/Faulted abandon the run outright, so nothing is left to wait for. Paused is
        // deliberately absent: a held machine reports Hold rather than Idle, so the same wait
        // simply continues once the operator resumes — no resume-side bookkeeping needed.
        if (value is PlaybackState.Stopped or PlaybackState.Faulted)
        {
            _motionIdleSignal?.TrySetResult(false);
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

    public Guid? CurrentlyExecutingKeyPointId
    {
        get
        {
            if (PlaybackState is not (PlaybackState.Running or PlaybackState.Paused))
            {
                return null;
            }

            var segmentIndex = CurrentSegmentIndex ?? -1;
            var targetIndex = _currentPassBackward
                ? KeyPoints.Count - 2 - segmentIndex
                : segmentIndex + 1;
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
        if (Connection.Session?.DeviceStatus?.State == MachineState.Idle)
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
        (PlaybackState != PlaybackState.Paused || !_pausedForLinkLoss || Connection.Session.ConnectionState == ConnectionState.Connected);

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
        TotalSegments = Math.Max(0, KeyPoints.Count - 1);

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

        await WaitForMotionToFinishAsync();

        if (PlaybackState != PlaybackState.Running)
        {
            return;
        }

        if (ReturnToStartOnFinish)
        {
            if (!await RunReturnToStartMoveAsync())
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
        var line = $"G1 X{FormatAxis(start.Pose.X)} Y{FormatAxis(start.Pose.Y)} Z{FormatAxis(start.Pose.Z)} A{FormatAxis(start.Pose.A)} F{FormatAxis(start.FeedRateUnitsPerMin)}";
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

        _currentPassBackward = backward;
        CurrentSegmentIndex = null;
        SegmentProgress = 0;

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
