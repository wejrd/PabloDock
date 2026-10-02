using PabloDock.Models;
using PabloDock.Services;

namespace PabloDock;

internal sealed class SettingsForm : Form
{
    private readonly WindowEnumerator _enumerator;
    private readonly LayoutCaptureService _captureService;
    private readonly ProfileStorageService _storageService;
    private readonly StartupSettingsService _startupSettingsService;
    private readonly ProcessExclusionService _exclusionService;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly Action<string?> _profilesChanged;
    private readonly Icon _settingsIcon;
    private readonly CheckBox _startWithWindows = new();
    private readonly CheckBox _restoreAfterStartup = new();
    private readonly ComboBox _startupProfileSelector = new();
    private readonly ListBox _profiles = new();
    private readonly Panel _editorHost = new();
    private readonly Button _rename = new();
    private readonly Button _duplicate = new();
    private readonly Button _update = new();
    private readonly Button _delete = new();
    private readonly Button _saveProfileSettings = new();
    private readonly ListBox _excludedProcesses = new();
    private readonly TextBox _newExclusion = new();
    private readonly Button _removeExclusion = new();
    private ProfileSettingsEditor? _editor;

    public StartupSettings CurrentStartupSettings { get; private set; }

    public SettingsForm(
        WindowEnumerator enumerator,
        LayoutCaptureService captureService,
        ProfileStorageService storageService,
        StartupSettingsService startupSettingsService,
        StartupSettings startupSettings,
        GlobalHotkeyService hotkeys,
        Action<string?> profilesChanged,
        Icon icon,
        string? selectedProfile,
        ProcessExclusionService exclusionService)
    {
        _enumerator = enumerator;
        _captureService = captureService;
        _storageService = storageService;
        _startupSettingsService = startupSettingsService;
        _exclusionService = exclusionService;
        CurrentStartupSettings = startupSettings;
        _hotkeys = hotkeys;
        _profilesChanged = profilesChanged;
        _settingsIcon = (Icon)icon.Clone();
        Icon = _settingsIcon;
        Text = "PabloDock Settings";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1150, 500);
        Size = new Size(1150, 550);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var general = new TabPage("General");
        var profiles = new TabPage("Profiles");
        var exclusions = new TabPage("Exclusions");
        tabs.TabPages.Add(general);
        tabs.TabPages.Add(profiles);
        tabs.TabPages.Add(exclusions);
        Controls.Add(tabs);

        BuildGeneralTab(general);
        BuildProfilesTab(profiles);
        BuildExclusionsTab(exclusions);
        RefreshProfiles(selectedProfile);
        RefreshExclusions();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _settingsIcon.Dispose();
    }

    private void BuildGeneralTab(TabPage tab)
    {
        var startup = new GroupBox
        {
            Text = "Startup", Location = new Point(16, 16), Size = new Size(540, 215)
        };
        tab.Controls.Add(startup);

        _startWithWindows.Text = "Start PabloDock with Windows";
        _startWithWindows.AutoSize = true;
        _startWithWindows.Location = new Point(16, 28);
        _startWithWindows.Checked = CurrentStartupSettings.StartWithWindows;
        _startWithWindows.CheckedChanged += (_, _) => UpdateStartupControls();
        startup.Controls.Add(_startWithWindows);

        _restoreAfterStartup.Text = "Restore profile after startup";
        _restoreAfterStartup.AutoSize = true;
        _restoreAfterStartup.Location = new Point(16, 59);
        _restoreAfterStartup.Checked = CurrentStartupSettings.RestoreProfileAfterStartup;
        _restoreAfterStartup.CheckedChanged += (_, _) => UpdateStartupControls();
        startup.Controls.Add(_restoreAfterStartup);

        startup.Controls.Add(new Label
        {
            Text = "Profile:", AutoSize = true, Location = new Point(40, 96)
        });
        _startupProfileSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _startupProfileSelector.Location = new Point(97, 92);
        _startupProfileSelector.Width = 260;
        startup.Controls.Add(_startupProfileSelector);

        startup.Controls.Add(new Label
        {
            Text = "When started by Windows, PabloDock opens hidden in the tray.",
            AutoSize = true, Location = new Point(16, 132)
        });

        var save = new Button
        {
            Text = "Save startup settings", AutoSize = true, Location = new Point(16, 166)
        };
        save.Click += (_, _) => SaveStartupSettings();
        startup.Controls.Add(save);
        UpdateStartupControls();
    }

    private void BuildProfilesTab(TabPage tab)
    {
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(columns);

        var left = new Panel { Dock = DockStyle.Fill };
        columns.Controls.Add(left, 0, 0);
        _profiles.Dock = DockStyle.Fill;
        _profiles.SelectedIndexChanged += (_, _) => LoadProfileEditor();
        left.Controls.Add(_profiles);

        var profileActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            Padding = new Padding(3),
            WrapContents = true
        };
        ConfigureAction(_rename, "Rename", RenameProfile);
        ConfigureAction(_duplicate, "Duplicate", DuplicateProfile);
        ConfigureAction(_delete, "Delete", DeleteProfile);
        profileActions.Controls.AddRange([_rename, _duplicate, _delete]);
        left.Controls.Add(profileActions);

        var right = new Panel { Dock = DockStyle.Fill };
        columns.Controls.Add(right, 1, 0);

        var updateBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(6),
            WrapContents = false
        };
        ConfigureAction(_update, "Update layout from current windows", UpdateProfileLayout);
        updateBar.Controls.Add(_update);

        var applyBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(6),
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft
        };
        ConfigureAction(_saveProfileSettings, "Apply changes", SaveProfileSettings);
        applyBar.Controls.Add(_saveProfileSettings);
        right.Controls.Add(_editorHost);
        right.Controls.Add(applyBar);
        right.Controls.Add(updateBar);
        _editorHost.Dock = DockStyle.Fill;
    }

    private void BuildExclusionsTab(TabPage tab)
    {
        tab.Controls.Add(new Label
        {
            Text = "Processes excluded from new layouts and Add current windows",
            AutoSize = true, Location = new Point(16, 16)
        });

        _excludedProcesses.Location = new Point(16, 45);
        _excludedProcesses.Size = new Size(330, 240);
        _excludedProcesses.SelectedIndexChanged += (_, _) =>
            _removeExclusion.Enabled = _excludedProcesses.SelectedIndex >= 0;
        tab.Controls.Add(_excludedProcesses);

        var addCurrent = new Button
        {
            Text = "Add from current windows", AutoSize = true,
            Location = new Point(16, 299)
        };
        addCurrent.Click += (_, _) => AddFromCurrentWindows();
        tab.Controls.Add(addCurrent);

        _newExclusion.Location = new Point(16, 340);
        _newExclusion.Width = 230;
        _newExclusion.PlaceholderText = "Process name (e.g. Discord)";
        _newExclusion.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                AddExclusion();
                e.SuppressKeyPress = true;
            }
        };
        tab.Controls.Add(_newExclusion);

        var add = new Button
        {
            Text = "Add manually", Location = new Point(254, 339), AutoSize = true
        };
        add.Click += (_, _) => AddExclusion();
        tab.Controls.Add(add);

        _removeExclusion.Text = "Remove selected";
        _removeExclusion.Location = new Point(16, 380);
        _removeExclusion.AutoSize = true;
        _removeExclusion.Enabled = false;
        _removeExclusion.Click += (_, _) => RemoveExclusion();
        tab.Controls.Add(_removeExclusion);
    }

    private void RefreshExclusions()
    {
        try
        {
            _excludedProcesses.Items.Clear();
            _excludedProcesses.Items.AddRange(_exclusionService.Load().Cast<object>().ToArray());
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not load exclusions",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddExclusion()
    {
        try
        {
            var name = ProcessExclusionService.Normalize(_newExclusion.Text);
            var names = _excludedProcesses.Items.Cast<string>().ToList();
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"'{name}' is already excluded.", "Process exclusion",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            names.Add(name);
            _exclusionService.Save(names);
            _newExclusion.Clear();
            RefreshExclusions();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not add exclusion",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddFromCurrentWindows()
    {
        try
        {
            var excluded = _excludedProcesses.Items.Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var available = _enumerator.Enumerate()
                .Where(window => window.ProcessId != (uint)Environment.ProcessId)
                .Select(window => ProcessExclusionService.Normalize(window.ProcessName))
                .Where(name => !excluded.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (available.Length == 0)
            {
                MessageBox.Show(this, "All currently detected processes are already excluded.",
                    "Process exclusions", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new AddProcessExclusionsDialog(available);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _exclusionService.Save(_excludedProcesses.Items.Cast<string>()
                .Concat(dialog.SelectedProcesses));
            RefreshExclusions();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not add exclusions",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveExclusion()
    {
        if (_excludedProcesses.SelectedItem is not string name)
        {
            return;
        }

        try
        {
            var names = _excludedProcesses.Items.Cast<string>()
                .Where(item => !string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            _exclusionService.Save(names);
            RefreshExclusions();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not remove exclusion",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ConfigureAction(Button button, string text, Action action)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Click += (_, _) => action();
    }

    private void RefreshProfiles(string? selectName = null, string? startupName = null)
    {
        try
        {
            selectName ??= _profiles.SelectedItem as string;
            startupName ??= _startupProfileSelector.SelectedItem as string ??
                CurrentStartupSettings.ProfileName;
            var names = _storageService.ListProfileNames();
            _profiles.Items.Clear();
            _profiles.Items.AddRange(names.Cast<object>().ToArray());
            _startupProfileSelector.Items.Clear();
            _startupProfileSelector.Items.AddRange(names.Cast<object>().ToArray());
            if (startupName is not null)
            {
                _startupProfileSelector.SelectedItem = startupName;
            }

            if (selectName is not null)
            {
                _profiles.SelectedItem = selectName;
            }

            if (_profiles.SelectedIndex < 0 && _profiles.Items.Count > 0)
            {
                _profiles.SelectedIndex = 0;
            }

            UpdateProfileControls();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not list profiles",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadProfileEditor()
    {
        _editor?.Dispose();
        _editor = null;
        _editorHost.Controls.Clear();
        if (_profiles.SelectedItem is string name)
        {
            try
            {
                _editor = new ProfileSettingsEditor(_storageService.Load(name),
                    _enumerator, _captureService, _exclusionService);
                _editorHost.Controls.Add(_editor);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Could not load profile",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        UpdateProfileControls();
    }

    private void UpdateProfileControls()
    {
        var selected = _profiles.SelectedItem is string;
        _rename.Enabled = selected;
        _duplicate.Enabled = selected;
        _update.Enabled = selected;
        _delete.Enabled = selected;
        _saveProfileSettings.Enabled = _editor is not null;
    }

    private void UpdateStartupControls()
    {
        _restoreAfterStartup.Enabled = _startWithWindows.Checked;
        _startupProfileSelector.Enabled = _startWithWindows.Checked &&
            _restoreAfterStartup.Checked;
    }

    private void SaveStartupSettings()
    {
        try
        {
            var settings = new StartupSettings
            {
                StartWithWindows = _startWithWindows.Checked,
                RestoreProfileAfterStartup = _startWithWindows.Checked &&
                    _restoreAfterStartup.Checked,
                ProfileName = _startupProfileSelector.SelectedItem as string
            };
            if (settings.RestoreProfileAfterStartup)
            {
                _ = _storageService.Load(settings.ProfileName ?? string.Empty);
            }

            _startupSettingsService.Save(settings);
            CurrentStartupSettings = settings;
            MessageBox.Show(this, "Startup settings saved.", "PabloDock",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not save startup settings",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProfileSettings()
    {
        if (_profiles.SelectedItem is not string name || _editor is null ||
            !_editor.TryGetUpdatedProfile(this, out var updated))
        {
            return;
        }

        try
        {
            var profile = _storageService.Load(name);
            if (updated.Hotkey != profile.Hotkey)
            {
                if (updated.Hotkey is not null)
                {
                    foreach (var otherName in _storageService.ListProfileNames())
                    {
                        if (!string.Equals(otherName, name, StringComparison.OrdinalIgnoreCase) &&
                            _storageService.Load(otherName).Hotkey == updated.Hotkey)
                        {
                            MessageBox.Show(this,
                                $"{updated.Hotkey} is already assigned to profile '{otherName}'.",
                                "Duplicate hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                }

                if (!_hotkeys.TrySet(name, updated.Hotkey, out var error))
                {
                    MessageBox.Show(this, error, "Could not register hotkey",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    _storageService.Update(updated);
                }
                catch
                {
                    _hotkeys.TrySet(name, profile.Hotkey, out _);
                    throw;
                }
            }
            else
            {
                _storageService.Update(updated);
            }

            LoadProfileEditor();
            MessageBox.Show(this, "Profile settings saved.",
                "Profile updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not update profile",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenameProfile()
    {
        if (_profiles.SelectedItem is not string currentName)
        {
            return;
        }

        using var dialog = new ProfileNameDialog("Rename profile", currentName);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var newName = dialog.ProfileName;
            var selectedForStartup = string.Equals(_startupProfileSelector.SelectedItem as string,
                currentName, StringComparison.OrdinalIgnoreCase);
            _storageService.Rename(currentName, newName);
            if (string.Equals(CurrentStartupSettings.ProfileName, currentName,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var updatedStartup = CurrentStartupSettings with { ProfileName = newName };
                    _startupSettingsService.UpdateProfileReference(updatedStartup);
                    CurrentStartupSettings = updatedStartup;
                }
                catch
                {
                    _storageService.Rename(newName, currentName);
                    throw;
                }
            }

            _hotkeys.RenameProfile(currentName, newName);
            RefreshProfiles(newName, selectedForStartup ? newName : null);
            _profilesChanged(newName);
            MessageBox.Show(this, $"Renamed '{currentName}' to '{newName}'.",
                "Profile renamed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not rename profile",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DuplicateProfile()
    {
        if (_profiles.SelectedItem is not string currentName)
        {
            return;
        }

        using var dialog = new ProfileNameDialog("Duplicate profile", currentName + " copy");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var source = _storageService.Load(currentName);
            _storageService.Save(source with { Name = dialog.ProfileName, Hotkey = null });
            RefreshProfiles(dialog.ProfileName);
            _profilesChanged(dialog.ProfileName);
            MessageBox.Show(this,
                $"Duplicated '{currentName}'. The copy has no hotkey; you can assign one here.",
                "Profile duplicated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not duplicate profile",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateProfileLayout()
    {
        if (_profiles.SelectedItem is not string name ||
            MessageBox.Show(this,
                $"Replace the saved window layout in '{name}' with the current windows?",
                "Update profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var existing = _storageService.Load(name);
            var windows = _enumerator.Enumerate();
            var captured = _captureService.Capture(name, windows);
            var updatedWindows = TransferWindowSettings(existing.Windows, captured.Windows);
            _storageService.Update(captured with
            {
                Hotkey = existing.Hotkey,
                Windows = updatedWindows
            });
            LoadProfileEditor();
            _profilesChanged(name);
            MessageBox.Show(this, $"Updated '{name}' with {updatedWindows.Length} windows.",
                "Profile updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not update profile",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static CapturedWindow[] TransferWindowSettings(
        IReadOnlyList<CapturedWindow> previous, IReadOnlyList<CapturedWindow> current)
    {
        var updated = current.ToArray();
        var oldUsed = new bool[previous.Count];
        var newUsed = new bool[current.Count];

        static bool SameApplication(CapturedWindow left, CapturedWindow right) =>
            string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ClassName, right.ClassName, StringComparison.OrdinalIgnoreCase);

        for (var index = 0; index < current.Count; index++)
        {
            var matches = Enumerable.Range(0, previous.Count).Where(old =>
                !oldUsed[old] && SameApplication(previous[old], current[index]) &&
                string.Equals(previous[old].Title, current[index].Title,
                    StringComparison.Ordinal)).ToArray();
            var sameCurrentCount = current.Count(window =>
                SameApplication(window, current[index]) &&
                string.Equals(window.Title, current[index].Title, StringComparison.Ordinal));
            if (matches.Length == 1 && sameCurrentCount == 1)
            {
                CopySettings(matches[0], index);
            }
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (newUsed[index])
            {
                continue;
            }

            var oldMatches = Enumerable.Range(0, previous.Count).Where(old =>
                !oldUsed[old] && SameApplication(previous[old], current[index])).ToArray();
            var newMatches = Enumerable.Range(0, current.Count).Where(next =>
                !newUsed[next] && SameApplication(current[next], current[index])).ToArray();
            if (oldMatches.Length == 1 && newMatches.Length == 1)
            {
                CopySettings(oldMatches[0], index);
            }
        }

        return updated;

        void CopySettings(int oldIndex, int newIndex)
        {
            var saved = previous[oldIndex];
            updated[newIndex] = current[newIndex] with
            {
                IncludeInProfile = saved.IncludeInProfile,
                LaunchIfMissing = saved.LaunchIfMissing,
                CustomLaunchPath = saved.CustomLaunchPath,
                CustomLaunchArguments = saved.CustomLaunchArguments
            };
            oldUsed[oldIndex] = true;
            newUsed[newIndex] = true;
        }
    }

    private void DeleteProfile()
    {
        if (_profiles.SelectedItem is not string name ||
            MessageBox.Show(this, $"Delete profile '{name}'? This cannot be undone.",
                "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var profile = _storageService.Load(name);
            _storageService.Delete(name);
            if (string.Equals(CurrentStartupSettings.ProfileName, name,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var updatedStartup = CurrentStartupSettings with
                    {
                        RestoreProfileAfterStartup = false,
                        ProfileName = null
                    };
                    _startupSettingsService.UpdateProfileReference(updatedStartup);
                    CurrentStartupSettings = updatedStartup;
                    _restoreAfterStartup.Checked = false;
                }
                catch
                {
                    _storageService.Save(profile);
                    throw;
                }
            }

            _hotkeys.TrySet(name, null, out _);
            RefreshProfiles();
            _profilesChanged(null);
            MessageBox.Show(this, $"Deleted '{name}'.", "Profile deleted",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not delete profile",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
