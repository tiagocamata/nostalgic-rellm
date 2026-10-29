using System.Drawing.Drawing2D;
using static NostalgicRellm.UI.Skin;

namespace NostalgicRellm.UI;

/// <summary>Buffer circular para os gráficos de histórico.</summary>
internal sealed class History(int capacity)
{
    private readonly float[] _data = new float[capacity];
    private int _head, _count;

    public void Push(float v)
    {
        _data[_head] = v;
        _head = (_head + 1) % _data.Length;
        _count = Math.Min(_count + 1, _data.Length);
    }

    public int Count => _count;

    /// <summary>i = 0 é o valor mais recente.</summary>
    public float this[int i] => _data[(_head - 1 - i + _data.Length * 2) % _data.Length];
}

/// <summary>Primitivas de desenho reutilizadas pela barra e pelo popup.</summary>
internal static class Painter
{
    public static readonly StringFormat Left = Make(StringAlignment.Near);
    public static readonly StringFormat Right = Make(StringAlignment.Far);
    public static readonly StringFormat Center = Make(StringAlignment.Center);

    private static StringFormat Make(StringAlignment a) => new(StringFormat.GenericTypographic)
    {
        Alignment = a,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
    };

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Caixa com gradiente vertical, reflexo de vidro e borda (ou relevo).</summary>
    public static void Box(Graphics g, RectangleF r, Color top, Color bottom, Color border, Color gloss, float radius, bool bevel)
    {
        using var path = Round(r, radius);
        using (var fill = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, 90f))
            g.FillPath(fill, path);

        if (gloss.A > 0)
        {
            var glossRect = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height * 0.48f);
            using var glossPath = Round(glossRect, Math.Max(0, radius - 1));
            using var gb = new LinearGradientBrush(new RectangleF(glossRect.X, glossRect.Y - 1, glossRect.Width, glossRect.Height + 2),
                gloss, Color.FromArgb(0, gloss), 90f);
            g.FillPath(gb, glossPath);
        }

        if (bevel)
            Bevel(g, r, raised: true);
        else if (border.A > 0)
        {
            using var pen = new Pen(border);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Relevo clássico: luz em cima/esquerda, sombra embaixo/direita.</summary>
    public static void Bevel(Graphics g, RectangleF r, bool raised)
    {
        var light = Color.FromArgb(110, 255, 255, 255);
        var dark = Color.FromArgb(150, 0, 0, 0);
        using var pl = new Pen(raised ? light : dark);
        using var pd = new Pen(raised ? dark : light);
        g.DrawLine(pl, r.Left, r.Bottom, r.Left, r.Top);
        g.DrawLine(pl, r.Left, r.Top, r.Right, r.Top);
        g.DrawLine(pd, r.Right, r.Top, r.Right, r.Bottom);
        g.DrawLine(pd, r.Right, r.Bottom, r.Left, r.Bottom);
    }

    public static void Panel(Graphics g, RectangleF r, Skin s)
    {
        var p = s.Panel;
        Box(g, r, C(p.Top), C(p.Bottom), C(p.Border), C(p.Gloss), p.Radius, p.Bevel);
        if (s.Background != null)
        {
            using var clip = Round(r, p.Radius);
            var state = g.Save();
            g.SetClip(clip);
            g.DrawImage(s.Background, r);
            g.Restore(state);
        }
    }

    public static void Cell(Graphics g, RectangleF r, Skin s, bool hover)
    {
        var c = s.Cell;
        using var path = Round(r, c.Radius);
        using (var b = new SolidBrush(C(c.Fill))) g.FillPath(b, path);
        if (hover)
            using (var hb = new SolidBrush(C(c.Hover))) g.FillPath(hb, path);
        if (c.Bevel)
            Bevel(g, r, raised: false);
        else
            using (var pen = new Pen(C(c.Border))) g.DrawPath(pen, path);
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Cor da barra: fica na cor "low" até 60% e vai para "high" até 100%.</summary>
    public static Color LoadColor(Skin s, float pct) => Lerp(C(s.Bar.Low), C(s.Bar.High), (pct - 60f) / 40f);

    public static void Bar(Graphics g, RectangleF r, float pct, Skin s)
    {
        float radius = r.Height / 2;
        using (var track = Round(r, radius))
        using (var tb = new SolidBrush(C(s.Bar.Track)))
            g.FillPath(tb, track);

        float w = r.Width * Math.Clamp(pct, 0, 100) / 100f;
        if (w < 1) return;
        var fr = new RectangleF(r.X, r.Y, Math.Max(w, r.Height), r.Height);
        var col = LoadColor(s, pct);
        using var path = Round(fr, radius);
        using (var b = new SolidBrush(col)) g.FillPath(b, path);
        if (s.Bar.Gloss)
        {
            var gr = new RectangleF(fr.X, fr.Y, fr.Width, fr.Height / 2);
            using var gb = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
            using var gp = Round(gr, radius / 2);
            g.FillPath(gb, gp);
        }
    }

    /// <summary>Gráfico de linha estilo GKrellM, um pixel por amostra, mais recente à direita.</summary>
    public static void Graph(Graphics g, RectangleF r, History h, Skin s)
    {
        using (var grid = new Pen(C(s.Graph.Grid)))
        {
            g.DrawLine(grid, r.Left, r.Top + r.Height / 2, r.Right, r.Top + r.Height / 2);
            g.DrawLine(grid, r.Left, r.Top + r.Height / 4, r.Right, r.Top + r.Height / 4);
            g.DrawLine(grid, r.Left, r.Top + r.Height * 3 / 4, r.Right, r.Top + r.Height * 3 / 4);
        }

        int n = Math.Min(h.Count, (int)r.Width);
        if (n < 2) return;
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
            pts[i] = new PointF(r.Right - i, r.Bottom - r.Height * Math.Clamp(h[i], 0, 100) / 100f);

        using var fillPath = new GraphicsPath();
        fillPath.AddLines(pts);
        fillPath.AddLine(pts[^1], new PointF(pts[^1].X, r.Bottom));
        fillPath.AddLine(new PointF(pts[^1].X, r.Bottom), new PointF(pts[0].X, r.Bottom));
        fillPath.CloseFigure();
        var fillTop = C(s.Graph.Fill);
        using (var fb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), fillTop, Color.FromArgb(fillTop.A / 5, fillTop), 90f))
            g.FillPath(fb, fillPath);
        using var pen = new Pen(C(s.Graph.Line), 1f);
        g.DrawLines(pen, pts);
    }

    public static void Text(Graphics g, string text, Font font, Color color, RectangleF r, StringFormat fmt, Skin s)
    {
        var shadow = C(s.Text.Shadow);
        if (shadow.A > 0)
        {
            using var sb = new SolidBrush(shadow);
            g.DrawString(text, font, sb, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), fmt);
        }
        using var b = new SolidBrush(color);
        g.DrawString(text, font, b, r, fmt);
    }

    /// <summary>Botãozinho com LED, como os do GKrellM.</summary>
    public static void Led(Graphics g, RectangleF r, bool on, bool hover, Skin s)
    {
        using (var path = Round(r, 3))
        {
            using var bb = new SolidBrush(Color.FromArgb(hover ? Math.Min(255, C(s.Led.Button).A + 40) : C(s.Led.Button).A, C(s.Led.Button)));
            g.FillPath(bb, path);
        }
        Bevel(g, r, raised: true);

        float d = Math.Min(r.Width, r.Height) * 0.5f;
        var led = new RectangleF(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
        var col = C(on ? s.Led.On : s.Led.Off);
        if (on)
        {
            // Halo
            var halo = RectangleF.Inflate(led, d * 0.6f, d * 0.6f);
            using var hp = new GraphicsPath();
            hp.AddEllipse(halo);
            using var pgb = new PathGradientBrush(hp) { CenterColor = Color.FromArgb(150, col), SurroundColors = [Color.FromArgb(0, col)] };
            g.FillEllipse(pgb, halo);
        }
        using (var lb = new SolidBrush(col)) g.FillEllipse(lb, led);
        var shine = new RectangleF(led.X + d * 0.2f, led.Y + d * 0.12f, d * 0.45f, d * 0.35f);
        using (var sb = new SolidBrush(Color.FromArgb(on ? 170 : 70, 255, 255, 255))) g.FillEllipse(sb, shine);
        using (var op = new Pen(Color.FromArgb(160, 0, 0, 0))) g.DrawEllipse(op, led);
    }
}
