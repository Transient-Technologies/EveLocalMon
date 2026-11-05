using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using EveLocalMon.App.Controls;
using EveLocalMon.App.Services;
using EveLocalMon.App.Theme;
using EveLocalMon.Core.Alerts;
using EveLocalMon.Core.Audio;
using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Net;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Settings;
using EveLocalMon.Core.Status;
using EveLocalMon.Platform.Windows;
using CorePixelRect = EveLocalMon.Core.Imaging.PixelRect;

namespace EveLocalMon.App.Views;

/// <summary>
/// The shell (doc/specs/M4-SPEC.md §5): title bar, status header, tabs and footer. It owns the services
/// and the run state and wires them to the three tab views; the views only draw. Every change saves
/// when it is made, so there is no Save button.
/// </summary>
public sealed class MainWindow : Window
{
    private enum Tab
    {
        Monitor,
        Alerts,
        Calibration,
    }

    private readonly SettingsStore _settingsStore = new();
    private readonly MonitorLoop _loop = new();
    private readonly HttpClient _httpClient = new();
    private readonly WinmmSoundPlayer _soundPlayer = new();
    private readonly AlertPresenter _presenter;
    private readonly AlertDispatcher _dispatcher;
    private readonly SessionLog _sessionLog = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly string _version;

    private readonly TitleBar _titleBar;
    private readonly Border _stateLine = new() { Height = 2 };
    private readonly StatusHeader _header = new();
    private readonly MonitorTab _monitorTab = new();
    private readonly AlertsTab _alertsTab = new();
    private readonly CalibrationTab _calibrationTab = new();
    private readonly TabButton[] _tabButtons;
    private readonly TextBlock _regionText = Ui.Label("");
    private readonly Control _regionControls;
    private readonly Control _alertsHint = Ui.Label("Changes apply and save immediately");
    private readonly TextBlock _footerMessage = Ui.Label("");
    private readonly StateDot _calibrationDot = new(6);
    private readonly TextBlock _calibrationText = Ui.Label("");

    private AppSettings _settings;
    private TemplateSet _templates;
    private AlertState _alertState = new(new AlertStateOptions());
    private Tab _tab = Tab.Monitor;
    private Frame? _lastFrame;
    private DateTimeOffset? _lastTick;
    private string? _captureError;
    private string? _lastLoggedError;
    private string _calibrationSource = "none";
    private bool _wasMuted;

    public MainWindow()
    {
        _version = typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "0";

        Title = "EveLocalMon";
        Width = 1120;
        Height = 740;
        MinWidth = 900;
        MinHeight = 600;
        WindowDecorations = WindowDecorations.BorderOnly;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://EveLocalMon/Assets/EveLocalMon.png")));

        _settings = _settingsStore.Load(out string? warning);
        string templatesDirectory = _settings.TemplatesDirectory ?? TemplateSeeder.EnsureSeeded();
        _templates = LoadTemplates(templatesDirectory);

        _presenter = new AlertPresenter(this, () => _settings.Region, DescribeForPopup);
        _dispatcher = new AlertDispatcher(_soundPlayer, _presenter, new NtfyClient(_httpClient)) { Settings = _settings.Alerts };
        _dispatcher.ChannelFailed += message => Dispatcher.UIThread.Post(() => OnChannelFailed(message));

        _titleBar = new TitleBar(_version) { IsPinned = _settings.Ui.KeepOnTop };
        Topmost = _settings.Ui.KeepOnTop;
        _titleBar.PinToggled += OnPinToggled;

        _regionControls = BuildRegionControls();
        _tabButtons = [new TabButton("Monitor"), new TabButton("Alerts"), new TabButton("Calibration")];
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            Tab tab = (Tab)i;
            _tabButtons[i].Click += (_, _) => ShowTab(tab);
        }

        WireViews();
        Content = BuildLayout();

        _monitorTab.SetLog(_sessionLog);
        _alertsTab.Value = _settings.Alerts;
        _header.IntervalMs = _settings.PollIntervalMs;
        _calibrationTab.Zoom = _settings.Ui.EffectiveCalibrationZoom;
        _calibrationTab.SetCalibration(_settings.Calibration);
        _calibrationSource = _settings.Calibration is null ? "none" : "saved";
        if (_settings.Calibration is not null)
        {
            _calibrationTab.SetDetection(null, null, "Using the calibration saved last time. Press Re-detect to measure again.");
        }

        _loop.Updated += OnMonitorUpdate;
        _loop.Ticked += at => _lastTick = at;
        _clock.Tick += (_, _) => OnClock();
        _clock.Start();
        Closing += (_, _) => Shutdown();

        ShowTab(Tab.Monitor);
        RefreshAll();

        if (warning is not null)
        {
            SetFooter(warning);
            _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Error, "Settings", warning);
        }
        else
        {
            SetFooter(_settings.Region is null
                ? "No region selected yet — click Select region… and drag around EVE's Local member list."
                : $"Region {Describe(_settings.Region.Value)} loaded. Press Start watching.");
        }

        // One frame up front so the preview and guides are not empty before the first Start.
        Opened += (_, _) => ShowStaticFrame();
    }

    private void WireViews()
    {
        _header.StartStopClicked += ToggleMonitoring;
        _header.MuteClicked += OnHeaderMuteClicked;
        _header.IntervalChanged += OnIntervalChanged;

        _alertsTab.Changed += OnAlertSettingsChanged;
        _alertsTab.TestRequested += channel => _ = TestChannelAsync(channel);
        _alertsTab.MuteRequested += OnMuteRequested;
        _alertsTab.VolumePreviewRequested += volume => PlayChime(volume);

        _calibrationTab.Applied += OnCalibrationApplied;
        _calibrationTab.RedetectRequested += RedetectCalibration;
        _calibrationTab.ZoomChanged += zoom =>
        {
            _settings = _settings with { Ui = _settings.Ui with { CalibrationZoom = zoom } };
            Save();
        };
        _calibrationTab.DirtyChanged += dirty => _tabButtons[(int)Tab.Calibration].Text = dirty ? "Calibration •" : "Calibration";
    }

    private Control BuildLayout()
    {
        var tabStrip = new Border
        {
            Height = 36,
            Background = Palette.TabStrip.Brush,
            BorderBrush = Palette.Divider.Brush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Children = { _tabButtons[0], _tabButtons[1], _tabButtons[2] } },
                    Placed(new Panel { Children = { _regionControls, _alertsHint }, VerticalAlignment = VerticalAlignment.Center }, 1),
                },
            },
        };

        var footer = new Border
        {
            Height = 26,
            Background = Palette.Panel.Brush,
            BorderBrush = Palette.Divider.Brush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    _footerMessage,
                    Placed(Ui.Row(6, _calibrationDot, _calibrationText), 1),
                },
            },
        };

        var body = new Panel { Children = { _monitorTab, _alertsTab, _calibrationTab } };

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto") };
        Add(_titleBar, 0);
        Add(_stateLine, 1);
        Add(_header, 2);
        Add(tabStrip, 3);
        Add(body, 4);
        Add(footer, 5);
        return layout;

        void Add(Control control, int row)
        {
            Grid.SetRow(control, row);
            layout.Children.Add(control);
        }
    }

    private Control BuildRegionControls()
    {
        Button select = Ui.Button(IconKind.Region, "Select region…", dense: true, onClick: () => _ = SelectRegionAsync());
        return Ui.Row(12, _regionText, select);
    }

    private static Control Placed(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    // ---- tabs ----------------------------------------------------------------------------------

    private void ShowTab(Tab tab)
    {
        _tab = tab;
        _monitorTab.IsVisible = tab == Tab.Monitor;
        _alertsTab.IsVisible = tab == Tab.Alerts;
        _calibrationTab.IsVisible = tab == Tab.Calibration;
        _header.Compact = tab != Tab.Monitor;
        _regionControls.IsVisible = tab != Tab.Alerts;
        _alertsHint.IsVisible = tab == Tab.Alerts;

        for (int i = 0; i < _tabButtons.Length; i++)
        {
            _tabButtons[i].IsActive = i == (int)tab;
        }

        if (tab == Tab.Calibration)
        {
            if (_lastFrame is null)
            {
                ShowStaticFrame();
            }
            else
            {
                _calibrationTab.SetFrame(_lastFrame, RenderScaling);
            }
        }
    }

    // ---- monitoring ----------------------------------------------------------------------------

    private void ToggleMonitoring()
    {
        if (_loop.IsRunning)
        {
            StopMonitoring();
            return;
        }

        if (_settings.Region is not { } region)
        {
            SetFooter("Select a region first.");
            RefreshAll();
            return;
        }

        StartLoop(region);
        _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Watching started", $"Region {Describe(region)} · every {_settings.PollIntervalMs} ms");
        RefreshAll();
    }

    private void StartLoop(CorePixelRect region)
    {
        // Fresh alert state per run: stale debounce history from a previous session must not fire an
        // alert the moment monitoring resumes, and each run begins in "Checking".
        _alertState = new AlertState(new AlertStateOptions());
        _captureError = null;
        _lastLoggedError = null;
        _lastTick = null;
        _loop.Start(region, _settings.Calibration, _templates, _settings.PollIntervalMs);
    }

    private void StopMonitoring()
    {
        _loop.Stop();
        _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Watching stopped");
        RefreshAll();
    }

    private void RestartIfRunning()
    {
        if (_loop.IsRunning && _settings.Region is { } region)
        {
            StartLoop(region);
        }
    }

    private void OnMonitorUpdate(MonitorUpdate update)
    {
        if (update.Error is not null)
        {
            _captureError = update.Error;
            if (_lastLoggedError != update.Error)
            {
                _lastLoggedError = update.Error;
                _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Error, "Capture failed", update.Error);
            }

            RefreshAll();
            return;
        }

        _captureError = null;
        _lastLoggedError = null;
        _lastFrame = update.Frame;

        _monitorTab.ShowFrame(update.Frame, update.Snapshot, _settings.Calibration, _templates, RenderScaling);
        if (_tab == Tab.Calibration)
        {
            _calibrationTab.SetFrame(update.Frame, RenderScaling);
        }

        if (update.Snapshot is { } snapshot)
        {
            IReadOnlyList<AlertEvent> events = _alertState.Update(snapshot, DateTimeOffset.Now);
            if (events.Count > 0)
            {
                _ = DispatchAsync(events);
            }
        }

        RefreshAll();
    }

    private async Task DispatchAsync(IReadOnlyList<AlertEvent> events)
    {
        try
        {
            IReadOnlyList<DispatchOutcome> outcomes = await _dispatcher.DispatchAsync(events);
            for (int i = 0; i < events.Count; i++)
            {
                AlertMessage message = AlertMessageFactory.Create(events[i]);
                _sessionLog.Add(message.At, KindFor(message.Kind), message.Title, $"{message.Body}\n{outcomes[i].Describe()}");
                RecordNtfyOutcome(outcomes[i].Ntfy, message.At);
            }
        }
        catch (Exception ex)
        {
            _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Error, "Alert dispatch failed", ex.Message);
        }
    }

    private static SessionEntryKind KindFor(AlertKind kind) => kind switch
    {
        AlertKind.ThreatIncreased => SessionEntryKind.Threat,
        AlertKind.Unreadable or AlertKind.Overflow or AlertKind.MemberCountWhileUnreadable => SessionEntryKind.Unreadable,
        _ => SessionEntryKind.Clear,
    };

    private void RecordNtfyOutcome(ChannelResult result, DateTimeOffset at)
    {
        switch (result)
        {
            case ChannelResult.Sent:
                _alertsTab.SetNtfyStatus($"last send delivered {at.ToLocalTime():HH:mm}", NtfyHealth.Delivered);
                break;
            case ChannelResult.Failed:
                _alertsTab.SetNtfyStatus($"last send failed {at.ToLocalTime():HH:mm}", NtfyHealth.Failed);
                break;
        }
    }

    private void OnChannelFailed(string message)
    {
        _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Error, "Channel failed", message);
        SetFooter(message);
    }

    /// <summary>The popup's grey third line, read from the same debounced view the header shows.</summary>
    private string DescribeForPopup(AlertMessage message)
    {
        StableView view = _alertState.Stable;
        string rows = view.Readability switch
        {
            Readability.Full => "all rows readable",
            Readability.Overflow => view.MemberCount is { } total ? $"{view.VisibleRows} of {total} rows readable" : $"{view.VisibleRows} rows readable",
            _ => "member list not visible",
        };

        return view.MemberCount is { } members && view.Readability != Readability.NotVisible ? $"{members} in Local · {rows}" : rows;
    }

    // ---- header, footer, clock -----------------------------------------------------------------

    private void RefreshAll()
    {
        var status = MonitorStatus.Compute(
            _loop.IsRunning,
            _settings.Region is not null,
            _settings.Calibration is not null,
            _alertState.Stable,
            _captureError);

        _header.Apply(status);
        _header.SetRunning(_loop.IsRunning);
        _stateLine.Background = Palette.For(status.State).Brush;
        _titleBar.Subtitle = _loop.IsRunning ? $"v{_version} · watching Local" : $"v{_version} · stopped";

        _regionText.Text = _settings.Region is { } region ? $"Region {Describe(region)}" : "No region selected";
        RefreshMute();
        RefreshFooterRight();
    }

    private void RefreshMute()
    {
        DateTimeOffset? until = _dispatcher.IsMuted ? _dispatcher.MutedUntil : null;
        TimeSpan? remaining = until is { } end ? end - DateTimeOffset.UtcNow : null;
        _header.SetMute(remaining);
        _alertsTab.SetMute(until);
        _monitorTab.SetChannels(_settings.Alerts.Threats, muted: until is not null);

        if (_wasMuted && until is null)
        {
            _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Mute ended", "Sound and ntfy are back on.");
        }

        _wasMuted = until is not null;
    }

    private void RefreshFooterRight()
    {
        bool has = _settings.Calibration is not null;
        _calibrationDot.SetState(MonitorState.Stopped);
        _calibrationDot.Background = has ? Palette.Ok.Brush : null;
        _calibrationDot.BorderBrush = has ? Palette.Ok.Brush : Palette.TextLabel.Brush;
        _calibrationDot.BoxShadow = default;
        _calibrationText.Text = $"{DescribeCalibration()} · {_templates.Icons.Count} templates loaded";
    }

    private string DescribeCalibration() => _calibrationSource switch
    {
        "none" => "No calibration",
        "manual" => "Calibration set (manual)",
        "saved" => "Calibration loaded from settings",
        var other => $"Calibration {other} (auto-detected)",
    };

    private void OnClock()
    {
        // The loop only publishes an update when a frame changed, so "last scan" comes from the tick
        // event; a quiet Local keeps counting up, and a stalled loop turns the figure yellow.
        if (_loop.IsRunning && _lastTick is { } tick)
        {
            TimeSpan since = DateTimeOffset.Now - tick;
            bool stalled = since > TimeSpan.FromMilliseconds(Math.Max(1000, 3.0 * _settings.PollIntervalMs));
            _header.SetLastScan(since, stalled);
        }
        else
        {
            _header.SetLastScan(null, stalled: false);
        }

        RefreshMute();
    }

    private void SetFooter(string message) => _footerMessage.Text = message;

    // ---- region and calibration ----------------------------------------------------------------

    private async Task SelectRegionAsync()
    {
        bool wasRunning = _loop.IsRunning;
        _loop.Stop();

        // Get out of our own screenshot before the overlay goes up.
        WindowState = WindowState.Minimized;
        await Task.Delay(200);

        CorePixelRect? picked = await RegionPickerWindow.PickAsync(this);

        WindowState = WindowState.Normal;
        Activate();

        if (picked is not { } region || region.Width < 20 || region.Height < 20)
        {
            SetFooter("Region selection cancelled; nothing changed.");
            if (wasRunning && _settings.Region is { } previous)
            {
                StartLoop(previous);
            }

            RefreshAll();
            return;
        }

        _settings = _settings with { Region = region };
        string detail = $"Region {Describe(region)}";
        if (!RegionHeuristics.IsPlausibleListWidth(region.Width))
        {
            detail += $"\nThat is {region.Width} px wide — a member list is about 150 px. If the rows look wrong, re-drag around just the names column.";
        }

        _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Region picked", detail);
        RedetectCalibration();
        Save();

        if (wasRunning)
        {
            StartLoop(region);
        }

        RefreshAll();
    }

    private void RedetectCalibration()
    {
        if (_settings.Region is not { } region)
        {
            SetFooter("Select a region first.");
            return;
        }

        try
        {
            Frame frame = CaptureOnce(region);
            _lastFrame = frame;
            DetectionResult detection = CalibrationDetector.Detect(frame);
            DateTimeOffset now = DateTimeOffset.Now;

            if (detection.Calibration is not { } calibration)
            {
                _calibrationTab.SetDetection(detection, now, $"from region {region.Width} × {region.Height}");
                _calibrationTab.SetFrame(frame, RenderScaling);
                _monitorTab.ShowFrame(frame, null, null, _templates, RenderScaling);
                _sessionLog.Add(now, SessionEntryKind.Error, "Auto-detect failed", detection.Notes.FirstOrDefault() ?? "No list found.");
                SetFooter("Auto-detect failed. Re-drag around just the member list, or set the numbers by hand.");
                return;
            }

            _settings = _settings with { Calibration = calibration };
            _calibrationSource = detection.Confidence.ToString().ToLowerInvariant();

            Snapshot snapshot = LocalListReader.Read(frame, calibration, _templates);
            string header = snapshot.HeaderMemberCount is { } count
                ? $"· header reads {count}, matching {snapshot.OccupiedFullRows} occupied rows"
                : "· header count unreadable";

            _calibrationTab.SetCalibration(calibration);
            _calibrationTab.SetDetection(detection, now, $"from region {region.Width} × {region.Height} {header}");
            _calibrationTab.SetFrame(frame, RenderScaling);
            _monitorTab.ShowFrame(frame, snapshot, calibration, _templates, RenderScaling);

            _sessionLog.Add(now, SessionEntryKind.Info, "Calibration detected", $"Confidence {detection.Confidence} · pitch {calibration.RowPitch} px");
            SetFooter(detection.Confidence == DetectionConfidence.Good
                ? $"Detected calibration for region {Describe(region)}."
                : $"Calibration confidence is {detection.Confidence} — check the numbers on the Calibration tab before trusting the counts.");
            RestartIfRunning();
        }
        catch (Exception ex)
        {
            _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Error, "Capture failed", ex.Message);
            SetFooter($"Capture failed: {ex.Message}");
        }

        RefreshAll();
    }

    private void OnCalibrationApplied(Calibration calibration)
    {
        _settings = _settings with { Calibration = calibration };
        _calibrationSource = "manual";
        _calibrationTab.SetCalibration(calibration);
        Save();
        _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Calibration applied", "Edited by hand.");

        if (_loop.IsRunning)
        {
            RestartIfRunning();
        }
        else
        {
            ShowStaticFrame();
        }

        SetFooter("Calibration applied and saved.");
        RefreshAll();
    }

    /// <summary>One capture, for the preview and guides while not watching. Silent on failure: this is
    /// a convenience, and the real error surfaces when the user presses Start.</summary>
    private void ShowStaticFrame()
    {
        if (_settings.Region is not { } region || _loop.IsRunning)
        {
            return;
        }

        try
        {
            Frame frame = CaptureOnce(region);
            _lastFrame = frame;
            Snapshot? snapshot = _settings.Calibration is { } calibration ? LocalListReader.Read(frame, calibration, _templates) : null;
            _monitorTab.ShowFrame(frame, snapshot, _settings.Calibration, _templates, RenderScaling);
            _calibrationTab.SetFrame(frame, RenderScaling);
        }
        catch (Exception)
        {
            // Left blank; see summary.
        }
    }

    // ---- settings, alerts ----------------------------------------------------------------------

    private void OnIntervalChanged(int ms)
    {
        _settings = _settings with { PollIntervalMs = ms };
        Save();
        _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Scan interval", $"Every {ms} ms");
        if (_loop.IsRunning && _settings.Region is { } region)
        {
            _loop.Start(region, _settings.Calibration, _templates, ms);
        }
    }

    private void OnPinToggled(bool pinned)
    {
        Topmost = pinned;
        _settings = _settings with { Ui = _settings.Ui with { KeepOnTop = pinned } };
        Save();
    }

    private void OnAlertSettingsChanged(AlertSettings alerts)
    {
        _settings = _settings with { Alerts = alerts };
        _dispatcher.Settings = alerts;
        Save();
        _monitorTab.SetChannels(alerts.Threats, _dispatcher.IsMuted);
        SetFooter($"Alert settings saved · {DateTimeOffset.Now:HH:mm:ss}");
    }

    private void OnHeaderMuteClicked()
    {
        if (_dispatcher.IsMuted)
        {
            OnMuteRequested(null);
        }
        else
        {
            OnMuteRequested(TimeSpan.FromMinutes(15));
        }
    }

    private void OnMuteRequested(TimeSpan? duration)
    {
        if (duration is { } window)
        {
            _dispatcher.MuteFor(window);
            _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Muted", $"For {Describe(window)} — sound and ntfy are silent; popups still appear.");
        }
        else
        {
            _dispatcher.Unmute();
            _sessionLog.Add(DateTimeOffset.Now, SessionEntryKind.Info, "Unmuted");
        }

        _wasMuted = _dispatcher.IsMuted;
        RefreshMute();
    }

    private void PlayChime(int volume)
    {
        try
        {
            _soundPlayer.Play(AlertTone.BuildWav(volume));
        }
        catch (Exception ex)
        {
            SetFooter($"Sound: {ex.Message}");
        }
    }

    private async Task TestChannelAsync(string channel)
    {
        var message = new AlertMessage(
            AlertKind.ThreatIncreased,
            "Hostile in Local",
            "1 red — 1 threat visible (test alert)",
            AlertPriority.Urgent,
            DateTimeOffset.Now);

        AlertSettings alerts = _alertsTab.Value;

        try
        {
            switch (channel)
            {
                case "sound":
                    PlayChime(alerts.VolumePercent);
                    Log("Test: sound", "Played the built-in chime.");
                    break;
                case "popup":
                    _presenter.Present(message);
                    Log("Test: popup", "Shown near the watched region.");
                    break;
                case "ntfy":
                    if (string.IsNullOrWhiteSpace(alerts.NtfyTopic))
                    {
                        SetFooter("ntfy: set a topic first (press Generate).");
                        return;
                    }

                    bool sent = await new NtfyClient(_httpClient).SendAsync(
                        message with { Title = "EVE: " + message.Title },
                        new NtfyOptions(alerts.NtfyServer, alerts.NtfyTopic));
                    RecordNtfyOutcome(sent ? ChannelResult.Sent : ChannelResult.Failed, DateTimeOffset.Now);
                    Log(
                        sent ? "Test: ntfy delivered" : "Test: ntfy failed",
                        sent ? $"Sent to {alerts.NtfyServer.TrimEnd('/')}/{alerts.NtfyTopic}" : "Check the server and topic.",
                        sent ? SessionEntryKind.Info : SessionEntryKind.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Test: {channel} failed", ex.Message, SessionEntryKind.Error);
        }

        void Log(string title, string detail, SessionEntryKind kind = SessionEntryKind.Info) =>
            _sessionLog.Add(DateTimeOffset.Now, kind, title, detail);
    }

    private void Save()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            SetFooter($"Could not save settings: {ex.Message}");
        }
    }

    private void Shutdown()
    {
        _clock.Stop();
        _loop.Dispose();
        _soundPlayer.Dispose();
        _httpClient.Dispose();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static Frame CaptureOnce(CorePixelRect region)
    {
        using var capture = new GdiScreenCapture();
        return capture.Capture(region);
    }

    private static TemplateSet LoadTemplates(string directory)
    {
        try
        {
            return TemplateStore.Load(directory);
        }
        catch (Exception)
        {
            return new TemplateSet([], new Dictionary<char, bool[,]>());
        }
    }

    private static string Describe(CorePixelRect region) => $"{region.Width} × {region.Height} at {region.X}, {region.Y}";

    private static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 60 ? $"{span.TotalHours:0.#} hour{(span.TotalHours == 1 ? string.Empty : "s")}" : $"{(int)span.TotalMinutes} min";
}
