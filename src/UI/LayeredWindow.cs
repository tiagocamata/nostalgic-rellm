namespace NostalgicRellm.UI;

/// <summary>
/// Janela sem borda com transparência por pixel (WS_EX_LAYERED + UpdateLayeredWindow).
/// As subclasses desenham num Bitmap e chamam <see cref="Present"/>; WM_PAINT não é usado.
/// </summary>
internal abstract class LayeredWindow : Form
{
    protected LayeredWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }

    /// <summary>Quando true, clicar na janela não rouba o foco do aplicativo em uso.</summary>
    protected virtual bool NoActivate => false;

    protected override bool ShowWithoutActivation => NoActivate;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
            if (NoActivate) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected void Present(Bitmap bmp)
    {
        if (Width != bmp.Width || Height != bmp.Height)
            Size = new Size(bmp.Width, bmp.Height);
        Native.PresentLayered(Handle, bmp, Left, Top);
    }

    /// <summary>Posição de um popup junto à célula: abre para cima se a célula estiver na metade de baixo da tela.</summary>
    protected static Rectangle PopupBounds(Rectangle anchor, int w, int h, float u)
    {
        var scr = Screen.PrimaryScreen!.Bounds;
        int gap = (int)(4 * u);
        int y = anchor.Top > scr.Top + scr.Height / 2 ? anchor.Top - h - gap : anchor.Bottom + gap;
        int x = Math.Clamp(anchor.Left, scr.Left, scr.Right - w);
        return new Rectangle(x, y, w, h);
    }

    protected static Graphics Prepare(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        return g;
    }
}
