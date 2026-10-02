namespace PabloDock;

internal sealed class ProfileNameDialog : Form
{
    private readonly TextBox _nameBox = new();

    public string ProfileName => _nameBox.Text.Trim();

    public ProfileNameDialog(string title = "Save layout", string? initialName = null)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 120);

        var label = new Label
        {
            Text = "Profile name:",
            AutoSize = true,
            Location = new Point(12, 14)
        };

        _nameBox.Location = new Point(12, 36);
        _nameBox.Width = 336;
        _nameBox.MaxLength = 100;
        _nameBox.Text = initialName ?? string.Empty;

        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Enabled = ProfileName.Length > 0,
            Location = new Point(192, 82),
            Width = 75
        };
        _nameBox.TextChanged += (_, _) => saveButton.Enabled = ProfileName.Length > 0;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(273, 82),
            Width = 75
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.AddRange([label, _nameBox, saveButton, cancelButton]);
    }
}
