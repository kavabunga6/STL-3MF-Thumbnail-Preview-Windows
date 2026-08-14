using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Explorer3DPreview.Setup;

internal static class Program
{
    internal static readonly string[] PayloadFiles =
    {
        "Explorer3DPreview.comhost.dll", "Explorer3DPreview.deps.json",
        "Explorer3DPreview.dll", "Explorer3DPreview.runtimeconfig.json",
        "Explorer3DPreview.ico", "install.ps1", "uninstall.ps1"
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--verify-package", StringComparer.OrdinalIgnoreCase)) return VerifyPayload();
        if (args.Length == 2 && args[0].Equals("--extract-payload", StringComparison.OrdinalIgnoreCase))
        {
            ExtractPayload(args[1]);
            return 0;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0].Equals("--screenshot", StringComparison.OrdinalIgnoreCase))
        {
            using var form = new InstallerForm();
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            form.Hide();
            return 0;
        }
        Application.Run(new InstallerForm());
        return 0;
    }

    internal static void ExtractPayload(string directory)
    {
        Directory.CreateDirectory(directory);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var file in PayloadFiles)
        {
            using var source = assembly.GetManifestResourceStream($"Payload.{file}")
                ?? throw new InvalidDataException($"В установщике отсутствует файл {file}.");
            using var destination = File.Create(Path.Combine(directory, file));
            source.CopyTo(destination);
        }
    }

    private static int VerifyPayload()
    {
        var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        return PayloadFiles.All(file => names.Contains($"Payload.{file}", StringComparer.Ordinal)) ? 0 : 2;
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TreeView _formats;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button _installButton;
    private readonly Button _closeButton;
    private bool _updatingChecks;

    public InstallerForm()
    {
        Text = "Установка STL & 3MF Thumbnail Preview";
        ClientSize = new Size(720, 610);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 9f);

        var title = new Label
        {
            Text = "STL & 3MF Thumbnail Preview", Location = new Point(32, 24), Size = new Size(650, 38),
            Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 40, 56),
            UseMnemonic = false
        };
        var description = new Label
        {
            Text = "Выберите форматы, для которых Проводник будет показывать миниатюры в списке файлов.",
            Location = new Point(35, 70), Size = new Size(650, 25), Font = new Font("Segoe UI", 10.5f),
            ForeColor = Color.FromArgb(75, 88, 102)
        };
        _formats = new TreeView
        {
            Location = new Point(35, 105), Size = new Size(650, 345), CheckBoxes = true,
            BorderStyle = BorderStyle.FixedSingle, HideSelection = false, ShowLines = false,
            BackColor = Color.White
        };
        PopulateFormats();
        _formats.AfterCheck += FormatsAfterCheck;

        var selectAll = CreateSecondaryButton("Выбрать все", new Point(35, 458), new Size(110, 32));
        var recommended = CreateSecondaryButton("Рекомендуемые", new Point(153, 458), new Size(125, 32));
        var clear = CreateSecondaryButton("Снять все", new Point(286, 458), new Size(100, 32));
        selectAll.Click += (_, _) => SetAll(true);
        recommended.Click += (_, _) => SetRecommended();
        clear.Click += (_, _) => SetAll(false);

        var note = new Label
        {
            Text = "Открытые сетки рендерятся локально. Штатные обработчики SOLIDWORKS/eDrawings сохраняются; " +
                   "для остальных CAD используются только уже установленные лёгкие обработчики. SOLIDWORKS не запускается.",
            Location = new Point(35, 495), Size = new Size(650, 38), ForeColor = Color.FromArgb(95, 108, 122)
        };
        _status = new Label
        {
            Text = "Готово к установке.", Dock = DockStyle.Fill, Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
            ForeColor = Color.FromArgb(55, 72, 88)
        };
        _progress = new ProgressBar
        {
            Dock = DockStyle.Top, Height = 6, Margin = new Padding(0, 16, 18, 0),
            Style = ProgressBarStyle.Marquee, Visible = false
        };
        _installButton = new Button
        {
            Text = "Установить", Dock = DockStyle.Fill, Margin = new Padding(0, 1, 6, 1),
            BackColor = Color.FromArgb(35, 126, 206), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false
        };
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.Click += InstallButtonClick;
        _closeButton = new Button
        {
            Text = "Отмена", Dock = DockStyle.Fill, Margin = new Padding(6, 1, 0, 1),
            BackColor = Color.White, ForeColor = Color.FromArgb(42, 55, 68),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false
        };
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(163, 174, 184);
        _closeButton.FlatAppearance.BorderSize = 1;
        _closeButton.Click += (_, _) => Close();

        var actionButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty,
            ColumnCount = 2, RowCount = 1
        };
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        actionButtons.Controls.Add(_installButton, 0, 0);
        actionButtons.Controls.Add(_closeButton, 1, 0);

        var footer = new TableLayoutPanel
        {
            Location = new Point(35, 535), Size = new Size(650, 58),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Margin = Padding.Empty, Padding = Padding.Empty, ColumnCount = 2, RowCount = 2
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        footer.Controls.Add(_status, 0, 0);
        footer.SetColumnSpan(_status, 2);
        footer.Controls.Add(_progress, 0, 1);
        footer.Controls.Add(actionButtons, 1, 1);

        Controls.AddRange(new Control[] { title, description, _formats, selectAll, recommended, clear,
            note, footer });
        AcceptButton = _installButton;
        CancelButton = _closeButton;
    }

    private static Button CreateSecondaryButton(string text, Point location, Size size)
    {
        var button = new Button
        {
            Text = text, Location = location, Size = size, BackColor = Color.White,
            ForeColor = Color.FromArgb(42, 55, 68), FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(163, 174, 184);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private void PopulateFormats()
    {
        foreach (var group in FormatCatalog.All.GroupBy(option => option.Group))
        {
            var parent = new TreeNode(group.Key) { Checked = group.All(option => option.Recommended) };
            foreach (var option in group)
                parent.Nodes.Add(new TreeNode($"{option.Extension} — {option.Description}") { Tag = option, Checked = option.Recommended });
            _formats.Nodes.Add(parent);
            parent.Expand();
        }
    }

    private void FormatsAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_updatingChecks) return;
        var node = e.Node;
        if (node is null) return;
        _updatingChecks = true;
        try
        {
            if (node.Tag is null)
                foreach (TreeNode child in node.Nodes) child.Checked = node.Checked;
            else if (node.Parent is { } parent)
                parent.Checked = parent.Nodes.Cast<TreeNode>().All(node => node.Checked);
        }
        finally { _updatingChecks = false; }
    }

    private void SetAll(bool value)
    {
        _updatingChecks = true;
        foreach (TreeNode parent in _formats.Nodes)
        {
            parent.Checked = value;
            foreach (TreeNode child in parent.Nodes) child.Checked = value;
        }
        _updatingChecks = false;
    }

    private void SetRecommended()
    {
        _updatingChecks = true;
        foreach (TreeNode parent in _formats.Nodes)
        {
            foreach (TreeNode child in parent.Nodes)
                child.Checked = child.Tag is FormatOption option && option.Recommended;
            parent.Checked = parent.Nodes.Cast<TreeNode>().All(node => node.Checked);
        }
        _updatingChecks = false;
    }

    private string[] SelectedExtensions() => _formats.Nodes.Cast<TreeNode>()
        .SelectMany(parent => parent.Nodes.Cast<TreeNode>())
        .Where(node => node.Checked && node.Tag is FormatOption)
        .Select(node => ((FormatOption)node.Tag!).Extension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private async void InstallButtonClick(object? sender, EventArgs e)
    {
        var selected = SelectedExtensions();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "Выберите хотя бы одно расширение.", "Нет выбранных форматов",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _installButton.Enabled = false;
        _closeButton.Enabled = false;
        _formats.Enabled = false;
        _progress.Visible = true;
        _status.Text = $"Установка обработчика для {selected.Length} расширений…";
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"Explorer3DPreview.Setup.{Guid.NewGuid():N}");
        try
        {
            await Task.Run(() => Program.ExtractPayload(temporaryDirectory));
            var selectionFile = Path.Combine(temporaryDirectory, "extensions.txt");
            await File.WriteAllLinesAsync(selectionFile, selected);
            var script = Path.Combine(temporaryDirectory, "install.ps1");
            var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = powerShell,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -ExtensionsFile \"{selectionFile}\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить установку.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);

            _progress.Visible = false;
            _status.Text = $"Готово. Выбрано расширений: {selected.Length}. В Проводнике включите крупные значки.";
            _status.ForeColor = Color.FromArgb(30, 125, 74);
            _closeButton.Text = "Закрыть";
            _closeButton.Enabled = true;
            _installButton.Visible = false;
            AcceptButton = _closeButton;
        }
        catch (Exception exception)
        {
            _progress.Visible = false;
            _status.Text = "Установка не выполнена.";
            _status.ForeColor = Color.FromArgb(180, 48, 48);
            _formats.Enabled = true;
            _installButton.Enabled = true;
            _closeButton.Enabled = true;
            MessageBox.Show(this, exception.GetBaseException().Message, "Ошибка установки",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
        }
    }
}
