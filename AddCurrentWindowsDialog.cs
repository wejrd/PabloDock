using PabloDock.Models;

namespace PabloDock;

internal sealed class AddCurrentWindowsDialog : Form
{
    private readonly ListView _windows = new();

    public IReadOnlyList<DetectedWindow> SelectedWindows =>
        _windows.CheckedItems.Cast<ListViewItem>()
            .Select(item => (DetectedWindow)item.Tag!).ToArray();

    public AddCurrentWindowsDialog(IReadOnlyList<DetectedWindow> available)
    {
        Text = "Add current windows";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(650, 390);

        Controls.Add(new Label
        {
            Text = "Select windows to add to this profile:",
            AutoSize = true,
            Location = new Point(12, 12)
        });
        _windows.Location = new Point(12, 38);
        _windows.Size = new Size(626, 300);
        _windows.View = View.Details;
        _windows.CheckBoxes = true;
        _windows.FullRowSelect = true;
        _windows.Columns.Add("Window title", 350);
        _windows.Columns.Add("Process", 120);
        _windows.Columns.Add("Class", 130);
        Controls.Add(_windows);

        var add = new Button
        {
            Text = "Add selected", Enabled = false,
            Location = new Point(461, 350), Width = 95
        };
        add.Click += (_, _) =>
        {
            if (_windows.CheckedItems.Count > 0)
            {
                DialogResult = DialogResult.OK;
            }
        };
        _windows.ItemChecked += (_, _) => add.Enabled = _windows.CheckedItems.Count > 0;
        Controls.Add(add);

        var cancel = new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel,
            Location = new Point(563, 350), Width = 75
        };
        Controls.Add(cancel);
        AcceptButton = add;
        CancelButton = cancel;

        foreach (var window in available)
        {
            var item = new ListViewItem(window.Title) { Tag = window };
            item.SubItems.Add(window.ProcessName);
            item.SubItems.Add(window.ClassName);
            _windows.Items.Add(item);
        }
    }
}
