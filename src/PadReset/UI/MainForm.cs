using PadReset.Core;
using PadReset.Windows;
using static PadReset.UI.AppTheme;

namespace PadReset.UI;

public sealed partial class MainForm : Form
{
    private readonly ProfileDatabase database;
    private PrinterSession? session;
    private bool busy;
    private CancellationTokenSource? operation;
    private readonly string dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadReset");
    private readonly List<string> sessionLog = [];

    public MainForm(ProfileDatabase database, bool preview = false)
    {
        this.database = database;
        InitializeLayout();
        WireEvents();
        WriteLog($"PadReset {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}");
        SetButtons();
        if (preview)
        {
            session = PrinterSession.Simulate(database, WriteLog);
            DisplaySession();
            ShowReadings(session.ReadCounters(CancellationToken.None));
            SetButtons();
        }
    }

    private void WireEvents()
    {
        scan.Click += async (_, _) => await Run("Поиск…", _ =>
        {
            var found = UsbTransport.Enumerate();
            Ui(() =>
            {
                devices.Items.Clear();
                devices.Items.AddRange(found.Cast<object>().ToArray());
                if (found.Count > 0) devices.SelectedIndex = 0;
                status.Text = found.Count == 0 ? "Принтер не найден. Проверьте USB и драйвер." : "Выберите принтер.";
            });
            WriteLog($"USB-интерфейсов Epson: {found.Count}.");
        });
        connect.Click += async (_, _) =>
        {
            if (devices.SelectedItem is not UsbDevice device) { status.Text = "Выберите принтер."; return; }
            await Run("Подключение…", token =>
            {
                Disconnect();
                session = PrinterSession.Open(database, device, WriteLog, token);
                DisplaySession();
                ShowReadings(session.ReadCounters(token));
                WriteLog($"Подключён {session.Profile.Model} · {session.Serial}");
            });
        };
        read.Click += async (_, _) => await Run("Чтение…", token => ShowReadings(session!.ReadCounters(token)));
        disconnect.Click += async (_, _) => await Run("Отключение…", _ => Disconnect());
        simulation.Click += async (_, _) => await Run("Симулятор…", token =>
        {
            Disconnect();
            session = PrinterSession.Simulate(database, WriteLog);
            DisplaySession();
            ShowReadings(session.ReadCounters(token));
        });
        cancel.Click += (_, _) => { operation?.Cancel(); status.Text = "Отмена…"; };
        reset.Click += async (_, _) => await Reset();
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; status.Text = "Дождитесь завершения или отмените операцию."; }
        };
        FormClosed += (_, _) => session?.Dispose();
    }

    private async Task Reset()
    {
        if (session == null) return;
        string prompt = session.IsSimulation ? "Сбросить тестовые счётчики?" :
            $"{session.Profile.Title} · {session.Serial}\n\nСбросить счётчики? Абсорбер должен быть обслужен.";
        if (MessageBox.Show(this, prompt, "Сброс", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await Run("Сброс…", token =>
        {
            var result = session.Reset(dataDirectory, token);
            ShowReadings(result.Counters);
            Ui(() =>
            {
                notice.Text = session.IsSimulation ? "Тестовый сброс завершён." : "Сброс проверен. Перезапустите принтер и обновите данные.";
                MessageBox.Show(this, notice.Text, "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        });
    }

    private void DisplaySession() => Ui(() =>
    {
        deviceName.Text = (session!.IsSimulation ? "Симулятор · " : "") + session.Profile.Title;
        serialLabel.Text = "S/N: " + (string.IsNullOrWhiteSpace(session.Serial) ? "—" : session.Serial);
        notice.Text = session.IsSimulation ? "Тестовые данные." : "";
    });

    private void Disconnect()
    {
        var previous = session;
        session = null;
        previous?.Dispose();
        Ui(() =>
        {
            ClearCards();
            deviceName.Text = "Принтер не подключён";
            serialLabel.Text = notice.Text = "";
            status.Text = "Выберите принтер.";
        });
    }

    private void ClearCards()
    {
        foreach (Control card in cards.Controls.Cast<Control>().ToArray()) card.Dispose();
        cards.Controls.Clear();
    }

    private void ShowReadings(CounterReading[] readings) => Ui(() =>
    {
        cards.SuspendLayout();
        ClearCards();
        foreach (var reading in readings)
        {
            cards.Controls.Add(new CounterCard(reading, Math.Max(600, cards.ClientSize.Width - 28)));
            WriteLog($"Счётчик {reading.Number}: {reading.Value}/{reading.Maximum}; " +
                string.Join(", ", reading.Raw.Select(v => $"0x{v.Key:X}=0x{v.Value:X2}")));
        }
        cards.ResumeLayout();
        status.Text = session?.IsSimulation == true ? "Симулятор готов." : "Данные прочитаны.";
    });

    private async Task Run(string text, Action<CancellationToken> action)
    {
        if (busy) return;
        busy = true;
        operation = new CancellationTokenSource();
        SetButtons();
        progress.Visible = true;
        status.Text = text;
        try { await Task.Run(() => action(operation.Token)); }
        catch (Exception ex)
        {
            WriteLog(ex.ToString());
            if (session != null && ex is not PrintQueueCheckException) await Task.Run(Disconnect);
            status.Text = ex is OperationCanceledException ? "Отменено." : "Ошибка. Подробности — в журнале.";
            notice.Text = "";
            if (ex is not OperationCanceledException)
                MessageBox.Show(this, ex.Message.Split('\n')[0], "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            operation.Dispose();
            operation = null;
            busy = false;
            progress.Visible = false;
            SetButtons();
        }
    }

    private void SetButtons()
    {
        scan.Enabled = connect.Enabled = simulation.Enabled = devices.Enabled = !busy;
        read.Enabled = disconnect.Enabled = !busy && session != null;
        reset.Enabled = !busy && session?.Profile.CanReset == true;
        cancel.Enabled = busy;
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) Invoke(action); else action();
    }

    private void WriteLog(string text) => Ui(() =>
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {text}";
        sessionLog.Add(line);
        log.AppendText(line + Environment.NewLine);
    });
}
