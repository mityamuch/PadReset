using System.Text;
using static PadReset.UI.AppTheme;

namespace PadReset.UI;

public sealed partial class MainForm
{
    private readonly ComboBox devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 480 };
    private readonly Button scan = Button("Найти принтеры");
    private readonly Button connect = Button("Подключить");
    private readonly Button read = Button("Обновить");
    private readonly Button reset = Button("Сбросить счётчики");
    private readonly Button disconnect = Button("Отключить");
    private readonly Button simulation = Button("Симулятор");
    private readonly Button cancel = Button("Отменить");
    private readonly Label status = Label("Подключите принтер по USB.", 10, Muted);
    private readonly Label deviceName = Label("Принтер не подключён", 22, Color.White, true);
    private readonly Label serialLabel = Label("", 10, Muted);
    private readonly Label notice = Label("", 10, Accent);
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 16, 0, 0) };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Background, ForeColor = Muted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 12, FontStyle.Regular, GraphicsUnit.Pixel) };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Bottom, Height = 4, Style = ProgressBarStyle.Marquee, Visible = false };
    private void InitializeLayout()
    {
        Text = "PadReset";
        Size = new Size(1120, 900); MinimumSize = new Size(940, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background; ForeColor = Color.White; Font = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
        AutoScaleMode = AutoScaleMode.None;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 4));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        brand.Controls.Add(Label("PadReset", 26, Accent, true));
        brand.Controls.Add(Label("Epson · USB", 10, Muted));
        header.Controls.Add(brand, 0, 0);
        layout.Controls.Add(header, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel) };
        var main = new TabPage("Принтер и счётчики") { BackColor = Surface, Padding = new Padding(22) };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1 };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        body.Controls.Add(Label("USB-ПОДКЛЮЧЕНИЕ", 10, Muted, true), 0, 0);
        var usbBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        devices.BackColor = Background; devices.ForeColor = Color.White;
        usbBar.Controls.AddRange([devices, scan, connect]);
        body.Controls.Add(usbBar, 0, 1);
        body.Controls.Add(deviceName, 0, 2); body.Controls.Add(serialLabel, 0, 3);
        body.Controls.Add(cards, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        reset.BackColor = Accent; reset.ForeColor = Background;
        actions.Controls.AddRange([read, reset, disconnect, cancel]);
        body.Controls.Add(actions, 0, 5);
        notice.MaximumSize = new Size(950, 58); body.Controls.Add(notice, 0, 6);
        main.Controls.Add(body); tabs.TabPages.Add(main);
        tabs.TabPages.Add(new ModelCatalog(database));
        var journal = new TabPage("Журнал") { BackColor = Surface, Padding = new Padding(20) };
        var logButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        var export = Button("Сохранить…");
        export.Click += (_, _) => { using var dialog = new SaveFileDialog { Filter = "Текстовый файл|*.txt", FileName = "PadReset-log.txt" }; if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllLines(dialog.FileName, sessionLog, Encoding.UTF8); };
        logButtons.Controls.AddRange([export, simulation]); journal.Controls.Add(log); journal.Controls.Add(logButtons); tabs.TabPages.Add(journal);
        layout.Controls.Add(tabs, 0, 1); layout.Controls.Add(status, 0, 2); layout.Controls.Add(progress, 0, 3);
        Controls.Add(layout);
        cards.SizeChanged += (_, _) => { foreach (Control card in cards.Controls) card.Width = Math.Max(600, cards.ClientSize.Width - 28); };
    }
}
