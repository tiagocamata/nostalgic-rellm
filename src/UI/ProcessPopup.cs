using NostalgicRellm.Sensors;
using static NostalgicRellm.UI.Skin;

namespace NostalgicRellm.UI;

/// <summary>
/// Popup translúcido sem borda com os 5 processos que mais consomem.
/// O ✕ finaliza o processo (ou o grupo de mesmo nome) com dois cliques, para evitar acidentes.
/// Fecha ao perder o foco ou com Esc.
/// </summary>
internal sealed class ProcessPopup : LayeredWindow
{
    private const int Rows = 5;
    private static readonly TimeSpan ArmTimeout = TimeSpan.FromSeconds(3);

    private readonly ProcKind _kind;
    private readonly Skin _skin;
    private readonly float _u;
    private readonly ProcessSampler _sampler;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Font _titleFont, _textFont, _smallFont;

    private List<ProcEntry>? _items;
    private bool _busy;
    private int _hoverRow = -1;
    private bool _hoverKill;
    private int _armed = -1;
    private DateTime _armedAt;
    private string? _status;

    private readonly int _w, _h;
    private readonly float _headerH, _rowH, _pad;

    public ProcessPopup(ProcKind kind, Skin skin, float u, Rectangle anchor)
    {
        _kind = kind;
        _skin = skin;
        _u = u;
        _sampler = new ProcessSampler(kind);
        KeyPreview = true;

        _pad = 8 * u;
        _headerH = 24 * u;
        _rowH = 22 * u;
        _w = (int)(290 * u);
        _h = (int)(_headerH + Rows * _rowH + 18 * u);

        _titleFont = new Font(skin.Font, 11.5f * u, FontStyle.Bold, GraphicsUnit.Pixel);
        _textFont = new Font(skin.Font, 12f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        _smallFont = new Font(skin.Font, 9.5f * u, FontStyle.Regular, GraphicsUnit.Pixel);

        Bounds = PopupBounds(anchor, _w, _h, u);
        TopMost = true;

        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    private string Title => L.F("proc.title", _kind switch
    {
        ProcKind.Cpu => "CPU",
        ProcKind.Ram => "RAM",
        ProcKind.Gpu => "GPU",
        _ => "VRAM",
    });

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
        _ = RefreshAsync();
        _timer.Start();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Dispose();
        _titleFont.Dispose();
        _textFont.Dispose();
        _smallFont.Dispose();
        base.OnFormClosed(e);
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var items = await Task.Run(() => _sampler.SampleTop(Rows));
            if (IsDisposed) return;
            _items = items;
            if (_armed >= 0 && DateTime.UtcNow - _armedAt > ArmTimeout) _armed = -1;
            Render();
        }
        catch (Exception) { }
        finally { _busy = false; }
    }

    // ---------- Desenho ----------

    private RectangleF RowRect(int i) => new(_pad / 2, _headerH + i * _rowH, _w - _pad, _rowH);

    private RectangleF KillRect(int i)
    {
        var r = RowRect(i);
        float s = _rowH - 6 * _u;
        return new RectangleF(r.Right - s - 4 * _u, r.Y + 3 * _u, s, s);
    }

    private void Render()
    {
        if (IsDisposed) return;
        using var bmp = new Bitmap(_w, _h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Prepare(bmp))
        {
            var p = _skin.Popup;
            var bg = C(p.Background);
            Painter.Box(g, new RectangleF(0.5f, 0.5f, _w - 1.5f, _h - 1.5f), bg, Color.FromArgb(Math.Min(255, bg.A + 20), bg),
                C(p.Border), C(_skin.Panel.Gloss), _skin.Panel.Radius + 2, _skin.Panel.Bevel);

            var titleRect = new RectangleF(_pad, 0, _w - 2 * _pad, _headerH);
            Painter.Text(g, Title, _titleFont, C(p.Title), titleRect, Painter.Left, _skin);

            using (var sep = new Pen(Color.FromArgb(50, C(p.Border))))
                g.DrawLine(sep, _pad, _headerH - 1, _w - _pad, _headerH - 1);

            if (_items == null)
                Painter.Text(g, L.T("proc.measuring"), _textFont, C(p.Text), RowRect(0), Painter.Center, _skin);
            else if (_items.Count == 0)
                Painter.Text(g, L.T("proc.none"), _textFont, C(p.Text), RowRect(0), Painter.Center, _skin);
            else
                for (int i = 0; i < _items.Count; i++) DrawRow(g, i, _items[i]);

            string footer = _status ?? L.T("proc.hint");
            Painter.Text(g, footer, _smallFont, C(_skin.Text.Detail),
                new RectangleF(_pad, _h - 17 * _u, _w - 2 * _pad, 14 * _u), Painter.Right, _skin);
        }
        Present(bmp);
    }

    private void DrawRow(Graphics g, int i, ProcEntry e)
    {
        var p = _skin.Popup;
        var r = RowRect(i);
        if (i == _hoverRow)
        {
            using var path = Painter.Round(r, 3 * _u);
            using var hb = new SolidBrush(C(p.Hover));
            g.FillPath(hb, path);
        }

        bool killable = ProcessSampler.CanKill(e);
        bool armed = i == _armed;
        var kill = KillRect(i);
        float valueRight = kill.Left - 6 * _u;

        string name = e.Pids.Length > 1 ? $"{e.Name} ({e.Pids.Length})" : e.Name;
        Painter.Text(g, name, _textFont, C(p.Text), new RectangleF(r.X + 6 * _u, r.Y, valueRight - r.X - 70 * _u, r.Height), Painter.Left, _skin);
        Painter.Text(g, armed ? L.T("proc.confirm") : e.Text, _textFont, armed ? C(p.Kill) : C(p.Text),
            new RectangleF(valueRight - 80 * _u, r.Y, 80 * _u, r.Height), Painter.Right, _skin);

        if (!killable) return;
        bool hot = armed || (i == _hoverRow && _hoverKill);
        var kc = C(p.Kill);
        using (var path = Painter.Round(kill, 3 * _u))
        {
            using var b = new SolidBrush(Color.FromArgb(armed ? 220 : hot ? 90 : 30, kc));
            g.FillPath(b, path);
        }
        float m = kill.Width * 0.3f;
        using var pen = new Pen(armed ? Color.White : Color.FromArgb(hot ? 255 : 160, kc), 1.6f * _u);
        g.DrawLine(pen, kill.Left + m, kill.Top + m, kill.Right - m, kill.Bottom - m);
        g.DrawLine(pen, kill.Right - m, kill.Top + m, kill.Left + m, kill.Bottom - m);
    }

    // ---------- Mouse ----------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row = -1;
        bool onKill = false;
        int count = _items?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            if (!RowRect(i).Contains(e.Location)) continue;
            row = i;
            onKill = KillRect(i).Contains(e.Location);
        }
        if (row != _hoverRow || onKill != _hoverKill)
        {
            _hoverRow = row;
            _hoverKill = onKill;
            Render();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverRow = -1;
        _hoverKill = false;
        Render();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _items == null) return;
        for (int i = 0; i < _items.Count; i++)
        {
            if (!KillRect(i).Contains(e.Location) || !ProcessSampler.CanKill(_items[i])) continue;

            if (_armed == i && DateTime.UtcNow - _armedAt <= ArmTimeout)
            {
                var target = _items[i];
                int n = ProcessSampler.Kill(target);
                _status = L.F(n > 0 ? "proc.killed" : "proc.killFailed", target.Name);
                _armed = -1;
                _ = RefreshAsync();
            }
            else
            {
                _armed = i;
                _armedAt = DateTime.UtcNow;
            }
            Render();
            return;
        }
    }
}
