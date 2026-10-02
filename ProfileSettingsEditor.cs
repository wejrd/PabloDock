using PabloDock.Models;
using PabloDock.Services;

namespace PabloDock;

internal sealed class ProfileSettingsEditor : UserControl
{
    private sealed class WindowEntry(CapturedWindow window)
    {
        public CapturedWindow Window { get; } = window;
        public string? Path { get; set; } = window.CustomLaunchPath;
        public string? Arguments { get; set; } = window.CustomLaunchArguments;
    }

    private readonly record struct WindowKey(string ProcessName, string ClassName, string Title)
    {
        public static WindowKey From(CapturedWindow window) => new(
            window.ProcessName.ToUpperInvariant(), window.ClassName.ToUpperInvariant(), window.Title);

        public static WindowKey From(DetectedWindow window) => new(
            window.ProcessName.ToUpperInvariant(), window.ClassName.ToUpperInvariant(), window.Title);
    }

    private readonly LayoutProfile _profile;
    private readonly WindowEnumerator _enumerator;
    private readonly LayoutCaptureService _captureService;
    private readonly ProcessExclusionService _exclusionService;
    private readonly DataGridView _windows = new();
    private readonly TextBox _path = new();
    private readonly TextBox _arguments = new();
    private readonly Label _detectedPath = new();
    private readonly HotkeyCaptureBox _hotkeyBox = new();
    private readonly Button _browse = new();
    private readonly Button _removeSelected = new();
    private WindowEntry? _selectedEntry;

    public ProfileSettingsEditor(LayoutProfile profile,
        WindowEnumerator enumerator, LayoutCaptureService captureService,
        ProcessExclusionService exclusionService)
    {
        _profile = profile;
        _enumerator = enumerator;
        _captureService = captureService;
        _exclusionService = exclusionService;
        Size = new Size(920, 370);

        Controls.Add(new Label
        {
            Text = "Profile hotkey", Location = new Point(12, 15), AutoSize = true
        });
        _hotkeyBox.Location = new Point(130, 11);
        _hotkeyBox.Width = 360;
        _hotkeyBox.Hotkey = profile.Hotkey;
        Controls.Add(_hotkeyBox);
        var clearHotkey = new Button { Text = "Clear", Location = new Point(498, 10), Width = 70 };
        clearHotkey.Click += (_, _) => _hotkeyBox.Hotkey = null;
        Controls.Add(clearHotkey);

        Controls.Add(new Label
        {
            Text = "Saved windows", Location = new Point(12, 61), AutoSize = true
        });
        _windows.Location = new Point(12, 85);
        _windows.Size = new Size(555, 220);
        _windows.BackgroundColor = SystemColors.Window;
        _windows.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _windows.AllowUserToAddRows = false;
        _windows.AllowUserToDeleteRows = false;
        _windows.AllowUserToResizeRows = false;
        _windows.RowHeadersVisible = false;
        _windows.MultiSelect = true;
        _windows.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _windows.EditMode = DataGridViewEditMode.EditOnEnter;
        _windows.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Include", MinimumWidth = 62, FillWeight = 12
        });
        _windows.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Window title", MinimumWidth = 180,
            FillWeight = 52, ReadOnly = true
        });
        _windows.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Process", MinimumWidth = 85,
            FillWeight = 16, ReadOnly = true
        });
        _windows.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = "Launch if missing", MinimumWidth = 125, FillWeight = 20
        });
        _windows.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_windows.IsCurrentCellDirty)
            {
                _windows.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _windows.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0)
            {
                SetLaunchAvailability(_windows.Rows[e.RowIndex]);
            }
        };
        _windows.SelectionChanged += (_, _) => SelectWindow();
        Controls.Add(_windows);

        _removeSelected.Text = "Remove selected";
        _removeSelected.AutoSize = true;
        _removeSelected.Location = new Point(12, 312);
        _removeSelected.Click += (_, _) => RemoveSelected();
        Controls.Add(_removeSelected);
        var addCurrent = new Button
        {
            Text = "Add current windows", AutoSize = true, Location = new Point(140, 312)
        };
        addCurrent.Click += (_, _) => AddCurrentWindows();
        Controls.Add(addCurrent);
        Controls.Add(new Label
        {
            Text = "Click Apply changes to save.",
            AutoSize = true, Location = new Point(12, 343)
        });

        Controls.Add(new Label
        {
            Text = "Selected window settings", Location = new Point(580, 61), AutoSize = true
        });
        Controls.Add(new Label
        {
            Text = "Custom executable", Location = new Point(580, 85), AutoSize = true
        });
        _path.Location = new Point(580, 109);
        _path.Width = 240;
        _path.TextChanged += (_, _) => _arguments.Enabled =
            _selectedEntry is not null && !string.IsNullOrWhiteSpace(_path.Text);
        Controls.Add(_path);
        _browse.Text = "Browse...";
        _browse.Location = new Point(828, 108);
        _browse.Width = 78;
        _browse.Click += (_, _) => BrowseExecutable();
        Controls.Add(_browse);

        Controls.Add(new Label
        {
            Text = "Launch arguments", Location = new Point(580, 155), AutoSize = true
        });
        _arguments.Location = new Point(580, 179);
        _arguments.Width = 326;
        Controls.Add(_arguments);

        _detectedPath.Location = new Point(580, 230);
        _detectedPath.Size = new Size(326, 60);
        _detectedPath.AutoEllipsis = true;
        Controls.Add(_detectedPath);

        foreach (var window in profile.Windows)
        {
            AddRow(window);
        }

        if (_windows.Rows.Count > 0)
        {
            _windows.ClearSelection();
            _windows.Rows[0].Selected = true;
            _windows.CurrentCell = _windows.Rows[0].Cells[1];
        }
        SelectWindow();
    }

    public bool TryGetUpdatedProfile(IWin32Window owner, out LayoutProfile updated)
    {
        StoreCurrentEditor();
        var windows = new List<CapturedWindow>(_windows.Rows.Count);
        foreach (DataGridViewRow row in _windows.Rows)
        {
            var entry = (WindowEntry)row.Tag!;
            if (entry.Path is not null &&
                (!Path.IsPathFullyQualified(entry.Path) ||
                 !string.Equals(Path.GetExtension(entry.Path), ".exe",
                     StringComparison.OrdinalIgnoreCase) || !File.Exists(entry.Path)))
            {
                MessageBox.Show(owner,
                    $"Choose an existing .exe for '{entry.Window.Title}', or clear its custom path.",
                    "Invalid launch path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                updated = _profile;
                return false;
            }

            windows.Add(entry.Window with
            {
                IncludeInProfile = row.Cells[0].Value is true,
                LaunchIfMissing = row.Cells[0].Value is true && row.Cells[3].Value is true,
                CustomLaunchPath = entry.Path,
                CustomLaunchArguments = entry.Arguments
            });
        }

        updated = _profile with
        {
            Hotkey = _hotkeyBox.Hotkey,
            Windows = windows
        };
        return true;
    }

    private void AddRow(CapturedWindow window)
    {
        var index = _windows.Rows.Add(window.IncludeInProfile, window.Title,
            window.ProcessName, window.LaunchIfMissing);
        var row = _windows.Rows[index];
        row.Tag = new WindowEntry(window);
        row.Cells[1].ToolTipText = window.Title;
        SetLaunchAvailability(row);
    }

    private static void SetLaunchAvailability(DataGridViewRow row)
    {
        var included = row.Cells[0].Value is true;
        var launchCell = row.Cells[3];
        if (!included)
        {
            launchCell.Value = false;
        }

        launchCell.ReadOnly = !included;
        launchCell.Style.BackColor = included ? Color.Empty : SystemColors.Control;
        launchCell.Style.ForeColor = included ? Color.Empty : SystemColors.GrayText;
    }

    private void SelectWindow()
    {
        StoreCurrentEditor();
        _selectedEntry = _windows.CurrentRow?.Tag as WindowEntry;
        _removeSelected.Enabled = _windows.SelectedRows.Count > 0;
        var hasSelection = _selectedEntry is not null;
        _path.Enabled = hasSelection;
        _browse.Enabled = hasSelection;
        if (_selectedEntry is null)
        {
            _path.Clear();
            _arguments.Clear();
            _arguments.Enabled = false;
            _detectedPath.Text = string.Empty;
            return;
        }

        _path.Text = _selectedEntry.Path ?? string.Empty;
        _arguments.Text = _selectedEntry.Arguments ?? string.Empty;
        _detectedPath.Text =
            $"Detected executable: {_selectedEntry.Window.ExecutablePath ?? "Unavailable"}";
    }

    private void StoreCurrentEditor()
    {
        if (_selectedEntry is null)
        {
            return;
        }

        var path = _path.Text.Trim();
        _selectedEntry.Path = path.Length == 0 ? null : path;
        _selectedEntry.Arguments = path.Length == 0 || string.IsNullOrWhiteSpace(_arguments.Text)
            ? null : _arguments.Text.Trim();
    }

    private void RemoveSelected()
    {
        var selected = _windows.SelectedRows.Cast<DataGridViewRow>()
            .OrderByDescending(row => row.Index).ToArray();
        if (selected.Length == 0 ||
            MessageBox.Show(this,
                $"Remove {selected.Length} saved window(s)? They will be permanently deleted when you click Apply changes.",
                "Remove saved windows", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        StoreCurrentEditor();
        foreach (var row in selected)
        {
            _windows.Rows.RemoveAt(row.Index);
        }
        SelectWindow();
    }

    private void AddCurrentWindows()
    {
        try
        {
            StoreCurrentEditor();
            var savedCounts = _windows.Rows.Cast<DataGridViewRow>()
                .Select(row => WindowKey.From(((WindowEntry)row.Tag!).Window))
                .GroupBy(key => key)
                .ToDictionary(group => group.Key, group => group.Count());
            var available = new List<DetectedWindow>();
            var excluded = _exclusionService.Load().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var window in _enumerator.Enumerate())
            {
                if (window.ProcessId == (uint)Environment.ProcessId ||
                    excluded.Contains(window.ProcessName))
                {
                    continue;
                }

                var key = WindowKey.From(window);
                if (savedCounts.TryGetValue(key, out var count) && count > 0)
                {
                    savedCounts[key] = count - 1;
                }
                else
                {
                    available.Add(window);
                }
            }

            if (available.Count == 0)
            {
                MessageBox.Show(this, "All currently detected windows are already in this profile.",
                    "Add current windows", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new AddCurrentWindowsDialog(available);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var captured = _captureService.Capture(_profile.Name, dialog.SelectedWindows);
            foreach (var window in captured.Windows)
            {
                AddRow(window);
            }
            if (captured.Windows.Count > 0)
            {
                _windows.ClearSelection();
                var last = _windows.Rows[^1];
                last.Selected = true;
                _windows.CurrentCell = last.Cells[1];
            }
            SelectWindow();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not add current windows",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BrowseExecutable()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Select application executable",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            _path.Text = picker.FileName;
        }
    }
}
