using NostalgicRellm.Sensors;
using static NostalgicRellm.UI.Skin;

namespace NostalgicRellm.UI;

/// <summary>
/// Popup translúcido das IAs: primeiro lista Claude e GPT; clicando num deles mostra
/// o uso da sessão e da semana. Só consulta a internet enquanto está aberto.
/// </summary>
internal sealed class AiPopup : LayeredWindow
{
    private const int MaxBlocks = 4;
    private static readonly (AiProvider id, string name)[] Providers = [(AiProvider.Claude, "Claude"), (AiProvider.Gpt, "GPT · Codex")];

    private readonly Skin _skin;
    private readonly float _u;
    private readonly int _w, _h;
    private readonly float _pad, _headerH, _rowH, _blockH;
    private readonly Font _titleFont, _textFont, _smallFont;
    private readonly StringFormat _wrap = new(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisWord };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60_000 };

    private AiProvider? _view;
    private AiUsageResult? _result;
    private bool _loading;
    private AiProvider? _loadingFor;
    private DateTime _fetchedAt;
    private int _hover = -1;
    private bool _hoverBack;

    public AiPopup(Skin skin, float u, Rectangle anchor)
    {
        _skin = skin;
        _u = u;
        KeyPreview = true;
        _pad = 8 * u;
        _headerH = 24 * u;
        _rowH = 30 * u;
        _blockH = 36 * u;
        _w = (int)(260 * u);
        _h = (int)(_headerH + MaxBlocks * _blockH + 22 * u);

        _titleFont = new Font(skin.Font, 11.5f * u, FontStyle.Bold, GraphicsUnit.Pixel);
        _textFont = new Font(skin.Font, 12f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        _smallFont = new Font(skin.Font, 9.5f * u, FontStyle.Regular, GraphicsUnit.Pixel);

        Bounds = PopupBounds(anchor, _w, _h, u);
        TopMost = true;
        _timer.Tick += (_, _) => { if (_view is { } v) _ = LoadAsync(v); };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render();
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
        if (e.KeyCode == Keys.Escape)
        {
            if (_view != null) ShowList();
            else Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Dispose();
        _titleFont.Dispose();
        _textFont.Dispose();
        _smallFont.Dispose();
        _wrap.Dispose();
        base.OnFormClosed(e);
    }

    private void ShowList()
    {
        _view = null;
        _result = null;
        Render();
    }

    private async Task LoadAsync(AiProvider p)
    {
        if (_loading && _loadingFor == p) return;
        _loading = true;
        _loadingFor = p;
        Render();
        try
        {
            var r = await AiUsage.GetAsync(p);
            if (IsDisposed || _view != p) return;
            _result = r;
            _fetchedAt = DateTime.Now;
        }
        finally
        {
            _loading = false;
            if (!IsDisposed) Render();
        }
    }

    // ---------- Desenho ----------

    private RectangleF ListRow(int i) => new(_pad / 2, _headerH + 4 * _u + i * _rowH, _w - _pad, _rowH - 2 * _u);
    private RectangleF BackRect => new(_pad / 2, 2 * _u, 22 * _u, _headerH - 4 * _u);

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
            using (var sep = new Pen(Color.FromArgb(50, C(p.Border))))
                g.DrawLine(sep, _pad, _headerH - 1, _w - _pad, _headerH - 1);

            if (_view is { } v) DrawDetail(g, v);
            else DrawList(g);
        }
        Present(bmp);
    }

    private void DrawList(Graphics g)
    {
        var p = _skin.Popup;
        Painter.Text(g, L.T("ai.title"), _titleFont, C(p.Title), new RectangleF(_pad, 0, _w - 2 * _pad, _headerH), Painter.Left, _skin);
        for (int i = 0; i < Providers.Length; i++)
        {
            var r = ListRow(i);
            if (i == _hover)
            {
                using var path = Painter.Round(r, 3 * _u);
                using var hb = new SolidBrush(C(p.Hover));
                g.FillPath(hb, path);
            }
            Painter.Text(g, Providers[i].name, _textFont, C(p.Text), new RectangleF(r.X + 6 * _u, r.Y, r.Width - 30 * _u, r.Height), Painter.Left, _skin);
            Painter.Text(g, "›", _titleFont, C(p.Title), new RectangleF(r.X, r.Y, r.Width - 8 * _u, r.Height), Painter.Right, _skin);
        }
    }

    private void DrawDetail(Graphics g, AiProvider v)
    {
        var p = _skin.Popup;
        if (_hoverBack)
        {
            using var path = Painter.Round(BackRect, 3 * _u);
            using var hb = new SolidBrush(C(p.Hover));
            g.FillPath(hb, path);
        }
        Painter.Text(g, "‹", _titleFont, C(p.Title), BackRect, Painter.Center, _skin);

        string name = Providers.First(x => x.id == v).name;
        string title = _result?.Plan is { Length: > 0 } plan ? $"{name} · {plan}" : name;
        Painter.Text(g, title, _titleFont, C(p.Title), new RectangleF(BackRect.Right + 4 * _u, 0, _w - BackRect.Right - _pad, _headerH), Painter.Left, _skin);

        var content = new RectangleF(_pad, _headerH + 4 * _u, _w - 2 * _pad, MaxBlocks * _blockH);
        if (_result == null)
        {
            Painter.Text(g, L.T("ai.querying"), _textFont, C(p.Text), new RectangleF(content.X, content.Y, content.Width, _blockH), Painter.Center, _skin);
        }
        else if (_result.Error != null)
        {
            using var b = new SolidBrush(C(p.Text));
            g.DrawString(_result.Error, _textFont, b, content, _wrap);
        }
        else
        {
            for (int i = 0; i < _result.Windows.Count && i < MaxBlocks; i++)
            {
                var w = _result.Windows[i];
                float y = content.Y + i * _blockH;
                var line = new RectangleF(content.X, y, content.Width, 16 * _u);
                Painter.Text(g, w.Name, _textFont, C(p.Text), line, Painter.Left, _skin);
                // Janela que zerou depois da leitura: o valor real é desconhecido, então não mostra um 0% enganoso
                Painter.Text(g, w.Expired ? "—" : $"{w.Percent:0}%", _textFont, C(_skin.Text.Value), line, Painter.Right, _skin);
                Painter.Bar(g, new RectangleF(content.X, y + 18 * _u, content.Width, 4 * _u), w.Expired ? 0 : (float)w.Percent, _skin);
                Painter.Text(g, w.Expired ? L.T("reset.passed") : AiUsage.DescribeReset(w.ResetsAt), _smallFont, C(_skin.Text.Detail),
                    new RectangleF(content.X, y + 23 * _u, content.Width, 11 * _u), Painter.Right, _skin);
            }
        }

        string footer = _loading ? L.T("ai.updating")
            : _result?.ReadAt is { } readAt ? DescribeReadAt(readAt)
            : _result != null ? L.F("ai.updated", _fetchedAt) : "";
        Painter.Text(g, footer, _smallFont, C(_skin.Text.Detail),
            new RectangleF(_pad, _h - 18 * _u, _w - 2 * _pad, 14 * _u), Painter.Right, _skin);
    }

    private static string DescribeReadAt(DateTimeOffset at)
    {
        var local = at.ToLocalTime().DateTime;
        return L.F(local.Date == DateTime.Today ? "ai.readToday" : "ai.readDay", local);
    }

    // ---------- Mouse ----------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        bool back = false;
        if (_view == null)
        {
            for (int i = 0; i < Providers.Length; i++)
                if (ListRow(i).Contains(e.Location)) hover = i;
        }
        else back = BackRect.Contains(e.Location);

        if (hover != _hover || back != _hoverBack)
        {
            _hover = hover;
            _hoverBack = back;
            Render();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _hoverBack = false;
        Render();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (_view == null)
        {
            for (int i = 0; i < Providers.Length; i++)
            {
                if (!ListRow(i).Contains(e.Location)) continue;
                _view = Providers[i].id;
                _hover = -1;
                _ = LoadAsync(Providers[i].id);
                return;
            }
        }
        else if (BackRect.Contains(e.Location))
        {
            _hoverBack = false;
            ShowList();
        }
    }
}
