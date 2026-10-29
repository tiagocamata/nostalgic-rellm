using System.Diagnostics;
using NostalgicRellm.Sensors;
using static NostalgicRellm.UI.Skin;

namespace NostalgicRellm.UI;

/// <summary>
/// Barra horizontal translúcida, da altura da barra de tarefas, com as métricas.
/// Fica sempre na frente; pode ser recolhida até os LEDs ou ancorada no topo da tela
/// reservando a faixa (AppBar), e some enquanto um app em tela cheia está ativo.
/// </summary>
internal sealed class MainBar : LayeredWindow
{
    private enum CellKind { LedCollapse, LedDock, Cpu, Ram, Gpu, Vram, Ai, Disk }

    private sealed record Cell(CellKind Kind, RectangleF Rect, int DiskIndex = -1)
    {
        public ProcKind? Proc => Kind switch
        {
            CellKind.Cpu => ProcKind.Cpu,
            CellKind.Ram => ProcKind.Ram,
            CellKind.Gpu => ProcKind.Gpu,
            CellKind.Vram => ProcKind.Vram,
            _ => null,
        };

        public bool IsLed => Kind is CellKind.LedCollapse or CellKind.LedDock;
    }

    private const float HotTemp = 80f;

    private readonly Settings _settings = Settings.Load();
    private Skin _skin;
    private Snapshot? _snap;
    private readonly History _cpuH = new(400), _ramH = new(400), _gpuH = new(400), _vramH = new(400);

    private List<Cell> _cells = [];
    private int _hover = -1;
    private float _u = 1f;
    private Font? _labelFont, _valueFont, _detailFont;
    private int _layoutDisks = -1, _layoutHeight = -1;
    private bool _layoutGpu = true;
    private bool _taskbarOnTop;
    private float _dockLedX;

    private Thread? _sampler;
    private volatile bool _running;

    private Form? _popup;
    private CellKind? _popupKind;

    private readonly ContextMenuStrip _menu = new();
    private readonly ToolTip _tip = new() { ShowAlways = true };
    private Point _downAt;
    private bool _pressed, _dragging;

    private readonly int _appBarMsg = Native.RegisterWindowMessage("NostalgicRellm.AppBar");
    private bool _appBarRegistered;
    private bool _hiddenForFullscreen;

    public MainBar()
    {
        Text = "Nostalgic Rellm";
        Skin.ExtractDefaults();
        _skin = LoadSkinOrFirst(_settings.Skin);
        BuildMenu();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    protected override bool NoActivate => true;

    private Skin LoadSkinOrFirst(string name)
    {
        var all = Skin.Available().ToList();
        if (!all.Contains(name)) name = all.FirstOrDefault() ?? name;
        _settings.Skin = name;
        return Skin.Load(name);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Relayout();
        ApplyMode();
        Render();

        _running = true;
        _sampler = new Thread(SampleLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Sensores" };
        _sampler.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _running = false;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _popup?.Close();
        UnregisterAppBar();
        _settings.Save();
        base.OnFormClosing(e);
    }

    // ---------- Coleta ----------

    private void SampleLoop()
    {
        using var hw = new HardwareMonitor();
        try { hw.Open(); } catch (Exception) { /* segue com o que der: RAM e discos não dependem da LHM */ }

        var sw = new Stopwatch();
        while (_running)
        {
            sw.Restart();
            try
            {
                var s = hw.Sample();
                if (IsHandleCreated) BeginInvoke(() => OnSnapshot(s));
            }
            catch (Exception) when (!_running || IsDisposed) { return; }
            catch (Exception) { }

            int wait = 1000 - (int)sw.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    private void OnSnapshot(Snapshot s)
    {
        if (IsDisposed) return;
        _snap = s;
        _cpuH.Push(s.CpuLoad ?? 0);
        _ramH.Push(s.RamLoad);
        _gpuH.Push(s.GpuLoad ?? 0);
        _vramH.Push(s.VramLoad ?? 0);

        // Jogo, vídeo ou apresentação em tela cheia: a barra sai da frente
        bool fullscreen = Native.IsFullscreenAppActive(Screen.PrimaryScreen!.Bounds);
        if (fullscreen != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = fullscreen;
            if (fullscreen)
            {
                ClosePopup();
                Hide();
            }
            else Show();
        }
        if (_hiddenForFullscreen) return;

        if (s.Disks.Count != _layoutDisks || HasGpu != _layoutGpu)
        {
            Relayout();
            ApplyMode();
        }
        Render();

        // A barra de tarefas também é "sempre por cima" e volta à frente quando clicada
        if (!_menu.Visible) ApplyZOrder();
    }

    // ---------- Layout ----------

    private int BarHeight()
    {
        if (_settings.Height > 0) return _settings.Height;
        // Pergunta direto à barra de tarefas: a área útil da tela já desconta a nossa própria faixa quando ancorada
        var abd = new Native.APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>() };
        int h = 0;
        if (Native.SHAppBarMessage(Native.ABM_GETTASKBARPOS, ref abd) != IntPtr.Zero && abd.uEdge is Native.ABE_TOP or Native.ABE_BOTTOM)
        {
            h = abd.rc.Bottom - abd.rc.Top;
            _taskbarOnTop = abd.uEdge == Native.ABE_TOP;
        }
        // Barra de tarefas lateral: usa 40 px lógicos
        if (h <= 0 || h > 200) h = (int)(40 * DeviceDpi / 96f);
        return h;
    }

    private void Relayout()
    {
        int h = BarHeight();
        _u = h / 48f;
        float pad = 3 * _u, gap = 3 * _u;
        float cellH = h - 2 * pad;
        float x = pad;
        var cells = new List<Cell>();

        void Add(CellKind k, float width, int disk = -1)
        {
            cells.Add(new Cell(k, new RectangleF(x, pad, width, cellH), disk));
            x += width + gap;
        }

        // Coluna dos LEDs: em cima recolhe/expande, embaixo ancora no topo
        float ledW = 18 * _u, half = (cellH - 2 * _u) / 2;
        cells.Add(new Cell(CellKind.LedCollapse, new RectangleF(x, pad, ledW, half)));
        cells.Add(new Cell(CellKind.LedDock, new RectangleF(x, pad + half + 2 * _u, ledW, half)));
        x += ledW + gap;

        int disks = _snap?.Disks.Count ?? 0;
        bool gpu = HasGpu;
        Add(CellKind.Cpu, 96 * _u);
        Add(CellKind.Ram, 96 * _u);
        if (gpu)
        {
            Add(CellKind.Gpu, 96 * _u);
            Add(CellKind.Vram, 96 * _u);
        }
        Add(CellKind.Ai, 40 * _u);
        for (int i = 0; i < disks; i++) Add(CellKind.Disk, 104 * _u, i);

        float groupW = x - gap + pad;
        float collapsedW = pad + ledW + pad;
        int screenW = Screen.PrimaryScreen!.Bounds.Width;
        // Ancorada, o grupo de células fica centralizado; as laterais ficam livres para métricas futuras
        _dockLedX = Math.Max(0, (screenW - groupW) / 2);

        int width;
        if (_settings.Collapsed)
        {
            cells = cells.Where(c => c.IsLed).ToList();
            width = (int)Math.Ceiling(collapsedW);
        }
        else if (_settings.Docked)
        {
            cells = cells.Select(c => c with { Rect = c.Rect with { X = c.Rect.X + _dockLedX } }).ToList();
            width = screenW;
        }
        else width = (int)Math.Ceiling(groupW);

        _cells = cells;
        _layoutDisks = disks;
        _layoutGpu = gpu;
        _hover = -1;

        if (h != _layoutHeight || _labelFont == null)
        {
            _layoutHeight = h;
            var style = _skin.Text.Bold ? FontStyle.Bold : FontStyle.Regular;
            _labelFont?.Dispose(); _valueFont?.Dispose(); _detailFont?.Dispose();
            _labelFont = new Font(_skin.Font, 10.5f * _u, style, GraphicsUnit.Pixel);
            _valueFont = new Font(_skin.Font, 16f * _u, style, GraphicsUnit.Pixel);
            _detailFont = new Font(_skin.Font, 10f * _u, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        if (Width != width || Height != h) Size = new Size(width, h);
    }

    /// <summary>PC sem placa de vídeo reconhecida: as células de GPU e VRAM somem. Até a primeira leitura, assume que há.</summary>
    private bool HasGpu => _snap == null || _snap.GpuName != null;

    // ---------- Desenho ----------

    private void Render()
    {
        if (_cells.Count == 0 || Width <= 0 || Height <= 0) return;
        using var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Prepare(bmp))
        {
            Painter.Panel(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), _skin);
            for (int i = 0; i < _cells.Count; i++) DrawCell(g, _cells[i], i == _hover);
        }
        Present(bmp);
    }

    private void DrawCell(Graphics g, Cell c, bool hover)
    {
        var r = c.Rect;
        if (c.Kind == CellKind.LedCollapse)
        {
            Painter.Led(g, r, !_settings.Collapsed, hover, _skin);
            return;
        }
        if (c.Kind == CellKind.LedDock)
        {
            Painter.Led(g, r, _settings.Docked, hover, _skin);
            return;
        }
        if (c.Kind == CellKind.Ai)
        {
            Painter.Cell(g, r, _skin, hover);
            Painter.Text(g, L.T("cell.ai"), _valueFont!, C(_skin.Text.Label), r, Painter.Center, _skin);
            return;
        }

        var s = _snap;
        string label, detail = "", value = "--", subValue = "";
        float? pct = null;
        bool hot = false;
        History? history = null;

        switch (c.Kind)
        {
            case CellKind.Cpu:
                label = "CPU";
                pct = s?.CpuLoad;
                detail = s?.CpuTemp is { } ct ? $"{ct:0}°C" : "--°C";
                hot = s?.CpuTemp >= HotTemp;
                history = _cpuH;
                break;
            case CellKind.Ram:
                label = "RAM";
                pct = s?.RamLoad;
                if (s != null) detail = $"{s.RamUsedGb:0.0}/{s.RamTotalGb:0}G";
                history = _ramH;
                break;
            case CellKind.Gpu:
                label = "GPU";
                pct = s?.GpuLoad;
                detail = s?.GpuTemp is { } gt ? $"{gt:0}°C" : "--°C";
                hot = s?.GpuTemp >= HotTemp;
                history = _gpuH;
                break;
            case CellKind.Vram:
                label = "VRAM";
                pct = s?.VramLoad;
                if (s is { VramTotalMb: > 0 }) detail = $"{s.VramUsedMb / 1024:0.0}/{s.VramTotalMb / 1024:0}G";
                history = _vramH;
                break;
            default:
                var d = s != null && c.DiskIndex < s.Disks.Count ? s.Disks[c.DiskIndex] : null;
                label = d == null || string.IsNullOrWhiteSpace(d.Label) ? L.T("cell.disk") : d.Label;
                detail = d?.Letter ?? "";
                pct = d?.Percent;
                if (d != null) subValue = L.F("cell.free", Math.Round(d.FreeBytes / 1073741824.0));
                break;
        }
        if (pct is { } p) value = $"{p:0}%";

        bool clickable = c.Proc != null;
        Painter.Cell(g, r, _skin, hover && clickable);

        float ix = r.X + 5 * _u, iw = r.Width - 10 * _u;
        bool graphs = _settings.ShowGraphs && history != null;
        if (graphs)
        {
            var gr = RectangleF.Inflate(r, -1.5f * _u, -1.5f * _u);
            var state = g.Save();
            using (var clip = Painter.Round(gr, _skin.Cell.Radius)) g.SetClip(clip);
            Painter.Graph(g, gr, history!, _skin);
            g.Restore(state);
        }

        var labelRect = new RectangleF(ix, r.Y + 2 * _u, iw, 12 * _u);
        var valueRect = new RectangleF(ix, r.Y + 13 * _u, iw, 19 * _u);

        Painter.Text(g, label, _labelFont!, C(_skin.Text.Label), labelRect, Painter.Left, _skin);
        Painter.Text(g, detail, _detailFont!, C(hot ? _skin.Text.Hot : _skin.Text.Detail), labelRect, Painter.Right, _skin);
        Painter.Text(g, value, _valueFont!, C(_skin.Text.Value), valueRect, Painter.Left, _skin);
        if (subValue.Length > 0)
            Painter.Text(g, subValue, _detailFont!, C(_skin.Text.Detail), valueRect, Painter.Right, _skin);

        if (!graphs)
            Painter.Bar(g, new RectangleF(ix, r.Bottom - 8 * _u, iw, 4 * _u), pct ?? 0, _skin);
    }

    // ---------- Modos: solta, recolhida, ancorada ----------

    /// <summary>Posiciona a janela conforme o modo e reserva (ou libera) a faixa do topo.</summary>
    private void ApplyMode()
    {
        var scr = Screen.PrimaryScreen!.Bounds;
        if (_settings.Docked && !_settings.Collapsed)
        {
            RegisterAppBar();
            ReserveTop();
        }
        else
        {
            UnregisterAppBar();
            if (_settings.Docked)
                // Recolhida no topo: só os LEDs, no mesmo lugar onde ficam quando expandida
                Location = new Point(scr.Left + (int)_dockLedX, scr.Top);
            else
            {
                Location = new Point(_settings.X ?? scr.Left + (int)(56 * _u), _settings.Y ?? (_taskbarOnTop ? scr.Top : scr.Bottom - Height));
                ClampToPrimary();
            }
        }
        ApplyZOrder();
    }

    private Native.APPBARDATA NewAppBarData() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>(),
        hWnd = Handle,
        uCallbackMessage = _appBarMsg,
        uEdge = Native.ABE_TOP,
    };

    private void RegisterAppBar()
    {
        if (_appBarRegistered) return;
        var abd = NewAppBarData();
        Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
        _appBarRegistered = true;
    }

    private void UnregisterAppBar()
    {
        if (!_appBarRegistered) return;
        var abd = NewAppBarData();
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
        _appBarRegistered = false;
    }

    /// <summary>Pede ao Windows a faixa do topo: janelas maximizadas passam a começar abaixo dela.</summary>
    private void ReserveTop()
    {
        var scr = Screen.PrimaryScreen!.Bounds;
        int h = Height;
        var abd = NewAppBarData();
        abd.rc = new Native.RECT { Left = scr.Left, Top = scr.Top, Right = scr.Right, Bottom = scr.Top + h };
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        abd.rc.Bottom = abd.rc.Top + h;
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
        Bounds = new Rectangle(abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, h);
    }

    private void ClampToPrimary()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        int x = Math.Clamp(Left, b.Left, Math.Max(b.Left, b.Right - Width));
        int y = Math.Clamp(Top, b.Top, Math.Max(b.Top, b.Bottom - Height));
        // Ímã nas bordas de cima e de baixo
        int snap = (int)(16 * _u);
        if (Math.Abs(y - (b.Bottom - Height)) < snap) y = b.Bottom - Height;
        else if (Math.Abs(y - b.Top) < snap) y = b.Top;
        if (x != Left || y != Top) Location = new Point(x, y);
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            _layoutHeight = -1;
            Relayout();
            ApplyMode();
            Render();
        });
    }

    private void ApplyZOrder()
    {
        const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    protected override void WndProc(ref Message m)
    {
        // Clique na barra com um popup ativo: entrega o clique sem ativar a barra (senão o primeiro clique se perde)
        if (m.Msg == Native.WM_MOUSEACTIVATE)
        {
            m.Result = Native.MA_NOACTIVATE;
            return;
        }
        // Outra AppBar ou a barra de tarefas mudou: renegocia a faixa reservada
        if (_appBarMsg != 0 && m.Msg == _appBarMsg && (int)m.WParam == Native.ABN_POSCHANGED && _appBarRegistered)
            ReserveTop();
        base.WndProc(ref m);
    }

    private void SetCollapsed(bool collapsed)
    {
        _settings.Collapsed = collapsed;
        ChangeMode();
    }

    private void SetDocked(bool docked)
    {
        _settings.Docked = docked;
        ChangeMode();
    }

    private void ChangeMode()
    {
        _settings.Save();
        ClosePopup();
        _tip.Hide(this);
        Relayout();
        ApplyMode();
        Render();
    }

    // ---------- Mouse ----------

    private int HitTest(Point p)
    {
        for (int i = 0; i < _cells.Count; i++)
            if (_cells[i].Rect.Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _downAt = e.Location;
        _pressed = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // Arrastar só quando solta; ancorada a posição é fixa
        if (_pressed && e.Button == MouseButtons.Left && !_settings.Docked)
        {
            var drag = SystemInformation.DragSize;
            if (!_dragging && (Math.Abs(e.X - _downAt.X) > drag.Width || Math.Abs(e.Y - _downAt.Y) > drag.Height))
            {
                _dragging = true;
                ClosePopup();
                _tip.Hide(this);
            }
            if (_dragging)
            {
                var c = Cursor.Position;
                Location = new Point(c.X - _downAt.X, c.Y - _downAt.Y);
                return;
            }
        }

        int hit = HitTest(e.Location);
        if (hit != _hover)
        {
            _hover = hit;
            ShowTip(hit);
            Render();
        }
    }

    private void ShowTip(int hit)
    {
        var cell = hit >= 0 ? _cells[hit] : null;
        string? text = cell?.Kind switch
        {
            CellKind.LedCollapse => L.T(_settings.Collapsed ? "tip.expand" : "tip.collapse"),
            CellKind.LedDock => L.T(_settings.Docked ? "tip.undock" : "tip.dock"),
            // Sem temperatura da CPU: explica o que falta (driver PawnIO e administrador)
            CellKind.Cpu when _snap is { CpuTemp: null } => L.T("tip.cpuTemp"),
            _ => null,
        };
        if (cell == null || text == null)
        {
            _tip.Hide(this);
            return;
        }
        int y = Top > Screen.PrimaryScreen!.Bounds.Top + Screen.PrimaryScreen.Bounds.Height / 2 ? -(int)(26 * _u) : Height + (int)(4 * _u);
        _tip.Show(text, this, (int)cell.Rect.X, y, 1500);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _tip.Hide(this);
        if (_hover != -1)
        {
            _hover = -1;
            Render();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            ClosePopup();
            // Sem ativar, o menu não fecha ao clicar em outro aplicativo
            Activate();
            _menu.Show(Cursor.Position);
            return;
        }
        if (e.Button != MouseButtons.Left || !_pressed) return;
        _pressed = false;
        if (_dragging)
        {
            _dragging = false;
            ClampToPrimary();
            _settings.X = Left;
            _settings.Y = Top;
            _settings.Save();
            return;
        }

        int hit = HitTest(e.Location);
        if (hit < 0) return;
        var cell = _cells[hit];
        switch (cell.Kind)
        {
            case CellKind.LedCollapse: SetCollapsed(!_settings.Collapsed); break;
            case CellKind.LedDock: SetDocked(!_settings.Docked); break;
            case CellKind.Ai: TogglePopup(cell, () => new AiPopup(_skin, _u, ScreenRectOf(cell))); break;
            default:
                if (cell.Proc is { } kind) TogglePopup(cell, () => new ProcessPopup(kind, _skin, _u, ScreenRectOf(cell)));
                break;
        }
    }

    private Rectangle ScreenRectOf(Cell cell) => RectangleToScreen(Rectangle.Round(cell.Rect));

    private void TogglePopup(Cell cell, Func<Form> create)
    {
        bool same = _popup != null && _popupKind == cell.Kind;
        ClosePopup();
        if (same) return;

        var popup = create();
        popup.FormClosed += (_, _) =>
        {
            if (_popup == popup)
            {
                _popup = null;
                _popupKind = null;
            }
        };
        _popup = popup;
        _popupKind = cell.Kind;
        popup.Show();
        popup.Activate();
    }

    private void ClosePopup()
    {
        var p = _popup;
        _popup = null;
        _popupKind = null;
        p?.Close();
    }

    // ---------- Menu ----------

    private void BuildMenu()
    {
        var skins = new ToolStripMenuItem();
        var graphs = new ToolStripMenuItem { CheckOnClick = true };
        var collapse = new ToolStripMenuItem { CheckOnClick = true };
        var dock = new ToolStripMenuItem { CheckOnClick = true };
        var connections = new ToolStripMenuItem();
        var language = new ToolStripMenuItem();
        var openFolder = new ToolStripMenuItem();
        var exit = new ToolStripMenuItem();

        connections.Click += (_, _) =>
        {
            using var dlg = new ConnectionsDialog();
            dlg.ShowDialog();
        };

        graphs.CheckedChanged += (_, _) =>
        {
            if (_settings.ShowGraphs == graphs.Checked) return;
            _settings.ShowGraphs = graphs.Checked;
            _settings.Save();
            Render();
        };
        collapse.CheckedChanged += (_, _) => { if (_settings.Collapsed != collapse.Checked) SetCollapsed(collapse.Checked); };
        dock.CheckedChanged += (_, _) => { if (_settings.Docked != dock.Checked) SetDocked(dock.Checked); };
        openFolder.Click += (_, _) => Process.Start("explorer.exe", Skin.SkinsRoot);
        exit.Click += (_, _) => Close();

        _menu.Items.AddRange([skins, graphs, collapse, dock, new ToolStripSeparator(), connections, language, openFolder, new ToolStripSeparator(), exit]);
        _menu.Opening += (_, _) =>
        {
            // Textos relidos a cada abertura: trocar de idioma vale na hora
            skins.Text = L.T("menu.skin");
            graphs.Text = L.T("menu.graphs");
            collapse.Text = L.T("menu.collapsed");
            dock.Text = L.T("menu.docked");
            connections.Text = L.T("menu.connections");
            language.Text = L.T("menu.language");
            openFolder.Text = L.T("menu.skinsFolder");
            exit.Text = L.T("menu.exit");

            language.DropDownItems.Clear();
            foreach (var (code, name) in L.Available())
            {
                var item = new ToolStripMenuItem(name) { Checked = code == L.Code };
                item.Click += (_, _) => ApplyLanguage(code);
                language.DropDownItems.Add(item);
            }

            graphs.Checked = _settings.ShowGraphs;
            collapse.Checked = _settings.Collapsed;
            dock.Checked = _settings.Docked;
            skins.DropDownItems.Clear();
            foreach (string name in Skin.Available())
            {
                var item = new ToolStripMenuItem(name) { Checked = name == _settings.Skin };
                item.Click += (_, _) => ApplySkin(name);
                skins.DropDownItems.Add(item);
            }
        };
        _menu.Closed += (_, _) => ApplyZOrder();
    }

    private void ApplyLanguage(string code)
    {
        _settings.Language = code;
        _settings.Save();
        L.Load(code);
        Render();
    }

    private void ApplySkin(string name)
    {
        _skin.Background?.Dispose();
        _skin = Skin.Load(name);
        _settings.Skin = name;
        _settings.Save();
        _labelFont?.Dispose();
        _labelFont = null; // recria as fontes (a skin pode trocar a família)
        Relayout();
        ApplyMode();
        Render();
    }
}
