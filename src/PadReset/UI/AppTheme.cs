namespace PadReset.UI;

internal static class AppTheme
{
    public static readonly Color Background = Color.FromArgb(18, 23, 33);
    public static readonly Color Surface = Color.FromArgb(29, 36, 49);
    public static readonly Color Muted = Color.FromArgb(160, 174, 195);
    public static readonly Color Accent = Color.FromArgb(81, 220, 171);

    public static Button Button(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 34,
        Padding = new Padding(10, 4, 10, 4),
        BackColor = Color.FromArgb(43, 53, 70),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 0, 10, 0),
        Cursor = Cursors.Hand
    };

    public static Label Label(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color,
        Font = new Font("Segoe UI", size * 4 / 3, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel),
        Margin = new Padding(0, 0, 0, 4)
    };
}
