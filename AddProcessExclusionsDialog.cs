namespace PabloDock;

internal sealed class AddProcessExclusionsDialog : Form
{
    private readonly CheckedListBox _processes = new();

    public IReadOnlyList<string> SelectedProcesses =>
        _processes.CheckedItems.Cast<string>().ToArray();

    public AddProcessExclusionsDialog(IReadOnlyList<string> available)
    {
        Text = "Add from current windows";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 370);

        Controls.Add(new Label
        {
            Text = "Select processes to exclude:",
            AutoSize = true,
            Location = new Point(12, 12)
        });

        _processes.Location = new Point(12, 38);
        _processes.Size = new Size(336, 276);
        _processes.CheckOnClick = true;
        _processes.Items.AddRange(available.Cast<object>().ToArray());
        Controls.Add(_processes);

        var add = new Button
        {
            Text = "Add selected", Enabled = false,
            Location = new Point(151, 327), Width = 105
        };
        add.Click += (_, _) =>
        {
            if (_processes.CheckedItems.Count > 0)
            {
                DialogResult = DialogResult.OK;
            }
        };
        _processes.ItemCheck += (_, e) =>
            add.Enabled = _processes.CheckedItems.Count +
                (e.NewValue == CheckState.Checked ? 1 : -1) > 0;
        Controls.Add(add);

        var cancel = new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel,
            Location = new Point(267, 327), Width = 81
        };
        Controls.Add(cancel);
        AcceptButton = add;
        CancelButton = cancel;
    }
}
