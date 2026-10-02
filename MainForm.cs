using PabloDock.Models;
using PabloDock.Native;
using PabloDock.Services;

namespace PabloDock;

public sealed class MainForm : Form
{
    private readonly WindowEnumerator _enumerator;
    private readonly LayoutCaptureService _captureService;
    private readonly LayoutRestoreService _restoreService;
    private readonly ProfileStorageService _storageService;
    private readonly StartupSettingsService _startupSettingsService;
    private readonly ProcessExclusionService _exclusionService;
    private readonly Icon _applicationIcon;
    private readonly ListView _windowList = new();
    private readonly Label _status = new();
    private readonly ComboBox _profileSelector = new();
    private readonly Button _restoreButton = new();
    private readonly Button _settingsButton = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _restoreProfilesMenu = new("Restore profile");
    private StartupSettings _startupSettings;
    private GlobalHotkeyService? _hotkeys;
    private bool _restoreInProgress;
    private bool _exitRequested;
    private bool _startHiddenOnLaunch;
    private bool _initialized;

    public MainForm(
        WindowEnumerator enumerator,
        LayoutCaptureService captureService,
        LayoutRestoreService restoreService,
        ProfileStorageService storageService,
        StartupSettingsService startupSettingsService,
        ProcessExclusionService exclusionService,
        bool startedWithWindows)
    {
        _enumerator = enumerator;
        _captureService = captureService;
        _restoreService = restoreService;
        _storageService = storageService;
        _startupSettingsService = startupSettingsService;
        _exclusionService = exclusionService;
        _startupSettings = startupSettingsService.Load();
        _startHiddenOnLaunch = startedWithWindows && _startupSettings.StartWithWindows;
        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("PabloDock.ico") ??
            throw new InvalidOperationException("The PabloDock icon is missing from the application.");
        using var resourceIcon = new Icon(iconStream);
        _applicationIcon = (Icon)resourceIcon.Clone();
        Icon = _applicationIcon;
        Text = "PabloDock";
        MinimumSize = new Size(850, 400);
        Size = new Size(1000, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var refreshButton = new Button
        {
            Text = "Refresh",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(10, 3, 3, 3)
        };
        refreshButton.Click += (_, _) => RefreshAll();

        var saveButton = new Button
        {
            Text = "Save layout",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        saveButton.Click += (_, _) => SaveLayout();

        _profileSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileSelector.Width = 190;
        _profileSelector.Anchor = AnchorStyles.Left;
        _profileSelector.Margin = new Padding(3, 5, 3, 3);
        _profileSelector.SelectedIndexChanged += (_, _) => UpdateProfileButtons();

        _settingsButton.Text = "Settings";
        _settingsButton.AutoSize = true;
        _settingsButton.Anchor = AnchorStyles.Left;
        _settingsButton.Click += (_, _) => ShowSettings();

        _restoreButton.Text = "Restore layout";
        _restoreButton.AutoSize = true;
        _restoreButton.Anchor = AnchorStyles.Left;
        _restoreButton.Enabled = false;
        _restoreButton.Click += async (_, _) => await RestoreLayoutAsync();

        _status.AutoSize = true;
        _status.Anchor = AnchorStyles.Left;
        _status.Margin = new Padding(12, 9, 3, 3);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8),
            WrapContents = false
        };
        toolbar.Controls.Add(_profileSelector);
        toolbar.Controls.Add(_restoreButton);
        toolbar.Controls.Add(saveButton);
        toolbar.Controls.Add(_status);
        toolbar.Controls.Add(refreshButton);
        toolbar.Controls.Add(_settingsButton);

        _windowList.Dock = DockStyle.Fill;
        _windowList.View = View.Details;
        _windowList.FullRowSelect = true;
        _windowList.GridLines = true;
        _windowList.HideSelection = false;
        _windowList.Columns.Add("Window title", 450);
        _windowList.Columns.Add("Process", 140);
        _windowList.Columns.Add("State", 90);
        _windowList.Resize += (_, _) => ResizeWindowColumns();

        Controls.Add(_windowList);
        Controls.Add(toolbar);
        ResizeWindowColumns();
        Shown += (_, _) => InitializeOnce();
        InitializeTray();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && _startHiddenOnLaunch)
        {
            _startHiddenOnLaunch = false;
            ShowInTaskbar = false;
            base.SetVisibleCore(false);
            _ = Handle;
            BeginInvoke((MethodInvoker)(() => _ = StartHiddenAsync()));
            return;
        }

        base.SetVisibleCore(value);
    }

    private void InitializeOnce()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        RefreshAll();
        RegisterSavedHotkeys();
    }

    private async Task StartHiddenAsync()
    {
        InitializeOnce();
        if (_startupSettings.RestoreProfileAfterStartup &&
            !string.IsNullOrWhiteSpace(_startupSettings.ProfileName))
        {
            await RestoreProfileAsync(_startupSettings.ProfileName);
        }
    }

    private void InitializeTray()
    {
        _trayMenu.Items.Add("Open PabloDock", null, (_, _) => OpenMainWindow());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _restoreProfilesMenu.DropDownOpening += (_, _) => RefreshTrayProfiles();
        _trayMenu.Items.Add(_restoreProfilesMenu);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon.Icon = _applicationIcon;
        _trayIcon.Text = "PabloDock";
        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.DoubleClick += (_, _) => OpenMainWindow();
        _trayIcon.Visible = true;
    }

    private void RefreshTrayProfiles()
    {
        foreach (ToolStripItem item in _restoreProfilesMenu.DropDownItems.Cast<ToolStripItem>().ToArray())
        {
            item.Dispose();
        }

        _restoreProfilesMenu.DropDownItems.Clear();
        try
        {
            var names = _storageService.ListProfileNames();
            if (names.Count == 0)
            {
                _restoreProfilesMenu.DropDownItems.Add(new ToolStripMenuItem("(No profiles)")
                {
                    Enabled = false
                });
                return;
            }

            foreach (var name in names)
            {
                var item = new ToolStripMenuItem(name) { Enabled = !_restoreInProgress };
                item.Click += async (_, _) => await RestoreProfileAsync(name);
                _restoreProfilesMenu.DropDownItems.Add(item);
            }
        }
        catch (Exception exception)
        {
            _restoreProfilesMenu.DropDownItems.Add(new ToolStripMenuItem(
                $"Could not list profiles: {exception.Message}") { Enabled = false });
        }
    }

    private void OpenMainWindow()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        BringToFront();
        Activate();
    }

    internal void ShowFromSecondInstance()
    {
        _startHiddenOnLaunch = false;
        OpenMainWindow();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _trayIcon.Visible = false;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing && !e.Cancel)
        {
            e.Cancel = true;
            Hide();
        }

        if (!e.Cancel)
        {
            _hotkeys?.Dispose();
            _hotkeys = null;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        _applicationIcon.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == User32.WmHotkey &&
            _hotkeys?.GetProfileName(message.WParam.ToInt32()) is { } profileName)
        {
            BeginInvoke((MethodInvoker)(() => _ = RestoreProfileAsync(profileName)));
            return;
        }

        base.WndProc(ref message);
    }

    private void RegisterSavedHotkeys()
    {
        if (_hotkeys is not null)
        {
            return;
        }

        _hotkeys = new GlobalHotkeyService(Handle);
        var errors = new List<string>();
        foreach (var name in _storageService.ListProfileNames())
        {
            try
            {
                var hotkey = _storageService.Load(name).Hotkey;
                if (hotkey is not null && !_hotkeys.TrySet(name, hotkey, out var error))
                {
                    errors.Add(error ?? $"Could not register the hotkey for '{name}'.");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{name}: {exception.Message}");
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), "Hotkey registration failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshAll()
    {
        RefreshWindows();
        RefreshProfiles();
    }

    private void RefreshProfiles(string? selectName = null)
    {
        try
        {
            selectName ??= _profileSelector.SelectedItem as string;
            var names = _storageService.ListProfileNames();
            _profileSelector.Items.Clear();
            _profileSelector.Items.AddRange(names.Cast<object>().ToArray());
            if (selectName is not null)
            {
                _profileSelector.SelectedItem = selectName;
            }

            if (_profileSelector.SelectedIndex < 0 && _profileSelector.Items.Count > 0)
            {
                _profileSelector.SelectedIndex = 0;
            }

            UpdateProfileButtons();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not list profiles",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSettings()
    {
        if (_restoreInProgress)
        {
            return;
        }

        _hotkeys ??= new GlobalHotkeyService(Handle);
        using var settings = new SettingsForm(_enumerator, _captureService, _storageService,
            _startupSettingsService, _startupSettings, _hotkeys, RefreshProfiles,
            _applicationIcon, _profileSelector.SelectedItem as string, _exclusionService);
        settings.ShowDialog(this);
        _startupSettings = settings.CurrentStartupSettings;
        RefreshProfiles();
    }

    private void RefreshWindows()
    {
        try
        {
            var windows = _enumerator.Enumerate();
            ShowWindows(windows);
        }
        catch (Exception exception)
        {
            _status.Text = "Refresh failed";
            MessageBox.Show(this, exception.Message, "Window discovery error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveLayout()
    {
        using var dialog = new ProfileNameDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var windows = _enumerator.Enumerate();
            var profile = _captureService.Capture(dialog.ProfileName, windows);
            var path = _storageService.Save(profile);
            ShowWindows(windows);
            RefreshProfiles(dialog.ProfileName);
            MessageBox.Show(this, $"Saved {profile.Windows.Count} windows to:\n{path}",
                "Layout saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save layout",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateProfileButtons()
    {
        var enabled = !_restoreInProgress && _profileSelector.SelectedItem is string;
        _restoreButton.Enabled = enabled;
        _settingsButton.Enabled = !_restoreInProgress;
    }

    private Task RestoreLayoutAsync()
    {
        return _profileSelector.SelectedItem is string name
            ? RestoreProfileAsync(name)
            : Task.CompletedTask;
    }

    private async Task RestoreProfileAsync(string name)
    {
        if (_restoreInProgress)
        {
            return;
        }

        _restoreInProgress = true;
        try
        {
            UpdateProfileButtons();
            var profile = _storageService.Load(name);
            var running = _enumerator.Enumerate();
            var result = await _restoreService.RestoreAsync(profile, running, _enumerator);
            if (IsDisposed)
            {
                return;
            }
            var summary = $"Restored: {result.Restored}\nMissing: {result.Missing.Count}" +
                $"\nAmbiguous: {result.Ambiguous.Count}\nFailed: {result.Failed.Count}" +
                $"\nLaunched: {result.Launched.Count}" +
                $"\nLaunch failed: {result.LaunchFailed.Count}" +
                $"\nWindow did not appear: {result.WindowDidNotAppear.Count}";
            var details = new List<string>();
            details.AddRange(result.Launched.Select(target => $"Launched: {target}"));
            details.AddRange(result.LaunchFailed.Select(reason => $"Launch failed: {reason}"));
            details.AddRange(result.WindowDidNotAppear.Select(reason => $"Window did not appear: {reason}"));
            details.AddRange(result.Missing.Select(title => $"Missing: {title}"));
            details.AddRange(result.Ambiguous.Select(title => $"Ambiguous: {title}"));
            details.AddRange(result.Failed.Select(failure => $"Failed: {failure}"));
            if (details.Count > 0)
            {
                summary += "\n\n" + string.Join("\n", details);
            }

            var hasProblems = result.Missing.Count != 0 || result.Ambiguous.Count != 0 ||
                result.Failed.Count != 0 || result.LaunchFailed.Count != 0 ||
                result.WindowDidNotAppear.Count != 0;
            if (!Visible)
            {
                var shortSummary = $"Restored: {result.Restored}, Missing: {result.Missing.Count}, " +
                    $"Ambiguous: {result.Ambiguous.Count}, Failed: {result.Failed.Count}.";
                var firstIssue = result.LaunchFailed.FirstOrDefault() ??
                    result.WindowDidNotAppear.FirstOrDefault() ??
                    result.Failed.FirstOrDefault() ??
                    result.Missing.FirstOrDefault() ??
                    result.Ambiguous.FirstOrDefault();
                if (hasProblems && firstIssue is not null)
                {
                    shortSummary += " " + firstIssue;
                }

                _trayIcon.ShowBalloonTip(5000, "PabloDock restore",
                    shortSummary[..Math.Min(shortSummary.Length, 200)],
                    hasProblems ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            else
            {
                MessageBox.Show(this, summary, "Layout restore result",
                    MessageBoxButtons.OK,
                    hasProblems ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            RefreshWindows();
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                if (!Visible)
                {
                    var error = exception.Message;
                    _trayIcon.ShowBalloonTip(5000, "Could not restore layout",
                        error[..Math.Min(error.Length, 200)], ToolTipIcon.Error);
                }
                else
                {
                    MessageBox.Show(this, exception.Message, "Could not restore layout",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        finally
        {
            _restoreInProgress = false;
            if (!IsDisposed)
            {
                UpdateProfileButtons();
            }
        }
    }

    private void ShowWindows(IReadOnlyList<DetectedWindow> windows)
    {
        _windowList.BeginUpdate();
        try
        {
            _windowList.Items.Clear();
            foreach (var window in windows)
            {
                _windowList.Items.Add(CreateRow(window));
            }
        }
        finally
        {
            _windowList.EndUpdate();
        }

        _status.Text = $"{windows.Count} windows found";
    }

    private static ListViewItem CreateRow(DetectedWindow window)
    {
        var item = new ListViewItem(window.Title);
        item.SubItems.Add(window.ProcessName);
        item.SubItems.Add(window.State.ToString());
        return item;
    }

    private void ResizeWindowColumns()
    {
        var available = _windowList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
        _windowList.Columns[0].Width = Math.Max(250, available - 140 - 90);
    }
}
