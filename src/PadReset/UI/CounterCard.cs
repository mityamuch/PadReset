using PadReset.Core;
using static PadReset.UI.AppTheme;

namespace PadReset.UI;

internal sealed class CounterCard : UserControl
{
    public CounterCard(CounterReading reading, int width)
    {
        var panel = new TableLayoutPanel { Width = width, Height = 92, BackColor = Background, Padding = new Padding(16, 10, 16, 8), Margin = new Padding(0, 0, 0, 8), RowCount = 3, ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.Controls.Add(Label($"Счётчик №{reading.Number}", 12, Color.White, true), 0, 0);
        var pct = Label($"{reading.Percent:0.0}%", 16, reading.Percent >= 100 ? Color.Salmon : Accent, true);
        panel.Controls.Add(pct, 1, 0);
        var bar = new CounterMeter(reading.Percent / 100.0) { Dock = DockStyle.Fill };
        panel.Controls.Add(bar, 0, 1); panel.SetColumnSpan(bar, 2);
        var detail = Label($"{reading.Value} / {reading.Maximum}" + (reading.HasExtra ? "  ·  основные регистры" : ""), 9, Muted);
        panel.Controls.Add(detail, 0, 2); panel.SetColumnSpan(detail, 2);
        Size = panel.Size; Margin = panel.Margin; panel.Dock = DockStyle.Fill; Controls.Add(panel);
    }
    private sealed class CounterMeter(double fraction) : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var track = new SolidBrush(Color.FromArgb(43, 53, 70));
            using var fill = new SolidBrush(fraction >= 1 ? Color.Salmon : Accent);
            var bounds = new Rectangle(0, 4, Width, Math.Max(4, Height - 8));
            e.Graphics.FillRectangle(track, bounds);
            e.Graphics.FillRectangle(fill, new Rectangle(bounds.X, bounds.Y, (int)(bounds.Width * Math.Clamp(fraction, 0, 1)), bounds.Height));
        }
    }
}
