using NostalgicRellm.Sensors;

namespace NostalgicRellm.UI;

/// <summary>
/// Mostra de onde vêm os números de cada IA e liga/desliga a linha de status do Claude Code.
/// Nenhuma senha ou token é digitado aqui.
/// </summary>
internal sealed class ConnectionsDialog : Form
{
    private const int Width96 = 440;

    private readonly Label _gptStatus = Wrapped();
    private readonly Label _claudeApi = Wrapped();
    private readonly Label _claudeStatus = Wrapped();
    private readonly Label _claudeLast = Wrapped();
    private readonly Button _connect = new() { AutoSize = true };
    private readonly Button _disconnect = new() { AutoSize = true, Text = L.T("conn.disconnect") };

    public ConnectionsDialog()
    {
        Text = L.T("conn.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };

        stack.Controls.Add(Heading(L.T("conn.gpt")));
        stack.Controls.Add(_gptStatus);

        var claudeHeading = Heading(L.T("conn.claude"));
        claudeHeading.Margin = new Padding(0, 16, 0, 4);
        stack.Controls.Add(claudeHeading);
        stack.Controls.Add(_claudeApi);
        var statusLineHeading = new Label { Text = L.T("conn.statusLine"), AutoSize = true, Margin = new Padding(0, 10, 0, 2), ForeColor = SystemColors.GrayText };
        stack.Controls.Add(statusLineHeading);
        stack.Controls.Add(_claudeStatus);
        stack.Controls.Add(_claudeLast);
        var note = Wrapped();
        note.Text = L.T("conn.claudeNote");
        note.ForeColor = SystemColors.GrayText;
        stack.Controls.Add(note);

        var claudeButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        claudeButtons.Controls.AddRange([_connect, _disconnect]);
        stack.Controls.Add(claudeButtons);

        var close = new Button { Text = L.T("conn.close"), AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        var bottom = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = Width96, Height = 40, Margin = new Padding(0, 16, 0, 0) };
        bottom.Controls.Add(close);
        stack.Controls.Add(bottom);

        Controls.Add(stack);
        AcceptButton = close;
        CancelButton = close;

        _connect.Click += (_, _) => ChangeClaude(connect: true);
        _disconnect.Click += (_, _) => ChangeClaude(connect: false);
        RefreshState();
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Label Wrapped() => new()
    {
        AutoSize = true,
        MaximumSize = new Size(Width96, 0),
        Margin = new Padding(0, 0, 0, 4),
    };

    private void RefreshState()
    {
        _gptStatus.Text = CodexCli.Find() != null ? L.T("conn.gptOk") : L.T("conn.gptMissing");

        _claudeApi.Text = ClaudeApi.SignedInPlan() is { } plan
            ? L.F("conn.claudeSignedIn", plan.Length > 0 ? plan : "?")
            : L.T("conn.claudeSignedOut");

        var state = ClaudeStatusLine.GetState();
        _claudeStatus.Text = state.Kind switch
        {
            ClaudeStatusLine.StateKind.Ours => L.T("conn.claudeOk"),
            ClaudeStatusLine.StateKind.OursElsewhere => L.F("conn.claudeMoved", state.Command),
            ClaudeStatusLine.StateKind.Other => L.T("conn.claudeOther"),
            ClaudeStatusLine.StateKind.None => L.T("conn.claudeOff"),
            _ => L.T("conn.claudeMissing"),
        };
        _claudeLast.Text = ClaudeStatusLine.LastReading() is { } at
            ? L.F("conn.claudeLast", at.ToLocalTime().DateTime)
            : L.T("conn.claudeNever");

        _connect.Text = state.Kind == ClaudeStatusLine.StateKind.OursElsewhere ? L.T("conn.fix") : L.T("conn.connect");
        _connect.Visible = state.Kind is not (ClaudeStatusLine.StateKind.Ours or ClaudeStatusLine.StateKind.NoClaudeCode);
        _disconnect.Visible = state.Kind is ClaudeStatusLine.StateKind.Ours or ClaudeStatusLine.StateKind.OursElsewhere;
    }

    private void ChangeClaude(bool connect)
    {
        var state = ClaudeStatusLine.GetState();
        string file = ClaudeStatusLine.SettingsFile;
        string question = !connect ? L.F("conn.confirmDisconnect", file)
            : state.Kind == ClaudeStatusLine.StateKind.Other ? L.F("conn.confirmReplace", file, state.Command)
            : L.F("conn.confirmConnect", file);
        if (MessageBox.Show(this, question, L.T("conn.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            if (connect) ClaudeStatusLine.Connect();
            else ClaudeStatusLine.Disconnect();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.F("conn.error", ex.Message), L.T("conn.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshState();
    }
}
