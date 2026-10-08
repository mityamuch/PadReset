using PadReset.Core;
using static PadReset.UI.AppTheme;

namespace PadReset.UI;

internal sealed class ModelCatalog : TabPage
{
    public ModelCatalog(ProfileDatabase database)
    {
        Text = "Модели"; BackColor = Surface; Padding = new Padding(20);
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Найти модель: L3250, L805, XP…", BackColor = Background, ForeColor = Color.White };
        var summary = Label($"{database.Models.Count} моделей", 10, Muted);
        summary.Dock = DockStyle.Bottom; summary.Height = 44;
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            MultiSelect = false
        };
        grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Color.White;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(46, 83, 77); grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Background; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Модель", DataPropertyName = "Title", FillWeight = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Счётчики", DataPropertyName = "Count", FillWeight = 50 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Профиль сброса", DataPropertyName = "Support", FillWeight = 150 });
        void Filter() => grid.DataSource = database.Models.Where(p => p.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || p.Model.Contains(search.Text, StringComparison.OrdinalIgnoreCase))
            .Select(p => new { p.Title, Count = p.Counters.Length, Support = p.CanReset ? "Есть" : p.UnsupportedReason ?? "Нет данных сброса" }).ToList();
        search.TextChanged += (_, _) => Filter(); Filter();
        Controls.Add(grid); Controls.Add(summary); Controls.Add(search);
    }
}
