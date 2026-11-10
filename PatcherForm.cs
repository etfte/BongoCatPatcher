using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BongoCatPatcher;

public class PatcherForm : Form
{
    private TextBox txtPath = null!;
    private TextBox txtLog = null!;
    private Label lblStatus = null!;
    private Button btnLaunch = null!;
    private Button btnPatch = null!;
    private Button btnRestore = null!;
    private Button btnSteamHook = null!;
    private Button btnMods = null!;
    private Button btnGameDir = null!;
    private Button btnWatch = null!;

    private string? currentAssemblyPath;
    private CancellationTokenSource? watcherCts;

    public PatcherForm()
    {
        InitializeComponents();
        LoadDetectedPath();
    }

    private void InitializeComponents()
    {
        Text = "Bongo Cat Assembly Patcher";
        Size = new Size(820, 620);
        MinimumSize = new Size(740, 540);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 24, 27);
        ForeColor = Color.FromArgb(244, 244, 245);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.FromArgb(39, 39, 42),
            Padding = new Padding(16, 12, 16, 12)
        };

        var lblTitle = new Label
        {
            Text = "🐱 Bongo Cat Assembly Patcher",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Color.FromArgb(167, 139, 250),
            AutoSize = true,
            Location = new Point(16, 12)
        };

        var lblSubtitle = new Label
        {
            Text = "Чистая разблокировка всех скинов, постоянная экипировка и PawPass (без накрутки монет)",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(161, 161, 170),
            AutoSize = true,
            Location = new Point(18, 48)
        };

        lblStatus = new Label
        {
            Text = "СТАТУС: ПОИСК...",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(63, 63, 70),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(220, 34),
            Location = new Point(570, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubtitle);
        pnlHeader.Controls.Add(lblStatus);

        // Path section
        var pnlPath = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65,
            Padding = new Padding(16, 10, 16, 10)
        };

        var lblPath = new Label
        {
            Text = "Путь к игре (Assembly-CSharp.dll):",
            Location = new Point(16, 8),
            AutoSize = true,
            ForeColor = Color.FromArgb(212, 212, 216)
        };

        txtPath = new TextBox
        {
            Location = new Point(18, 30),
            Width = 640,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(39, 39, 42),
            ForeColor = Color.FromArgb(228, 228, 231),
            BorderStyle = BorderStyle.FixedSingle
        };
        txtPath.TextChanged += (s, e) => { currentAssemblyPath = txtPath.Text; UpdateStatus(); };

        var btnBrowse = new Button
        {
            Text = "Обзор...",
            Location = new Point(670, 28),
            Size = new Size(110, 27),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(63, 63, 70),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnBrowse.FlatAppearance.BorderSize = 0;
        btnBrowse.Click += OnBrowseClicked;

        pnlPath.Controls.Add(lblPath);
        pnlPath.Controls.Add(txtPath);
        pnlPath.Controls.Add(btnBrowse);

        // Buttons section
        var pnlButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 90,
            Padding = new Padding(16, 4, 16, 4),
            AutoScroll = false
        };

        btnLaunch = CreateButton("🎮 Запустить Bongo Cat", Color.FromArgb(16, 185, 129), 200, 36);
        btnLaunch.Click += OnLaunchClicked;

        btnPatch = CreateButton("🚀 Применить патч", Color.FromArgb(34, 197, 94), 160, 36);
        btnPatch.Click += OnPatchClicked;

        btnRestore = CreateButton("🔄 Восстановить", Color.FromArgb(239, 68, 68), 140, 36);
        btnRestore.Click += OnRestoreClicked;

        btnSteamHook = CreateButton("⚡ Интеграция в Steam", Color.FromArgb(139, 92, 246), 180, 36);
        btnSteamHook.Click += OnToggleSteamHookClicked;

        btnWatch = CreateButton("🛡️ Защита Steam", Color.FromArgb(107, 114, 128), 150, 36);
        btnWatch.Click += OnToggleWatcherClicked;

        btnMods = CreateButton("🎨 Папка скинов", Color.FromArgb(75, 85, 99), 140, 36);
        btnMods.Click += OnOpenModsClicked;

        btnGameDir = CreateButton("📂 Папка игры", Color.FromArgb(75, 85, 99), 130, 36);
        btnGameDir.Click += OnOpenGameDirClicked;

        pnlButtons.Controls.AddRange(new Control[]
        {
            btnLaunch, btnPatch, btnRestore, btnSteamHook,
            btnWatch, btnMods, btnGameDir
        });

        // Log section
        var pnlLog = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 16)
        };

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(52, 211, 153),
            Font = new Font("Consolas", 9.5f),
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle
        };

        pnlLog.Controls.Add(txtLog);

        Controls.Add(pnlLog);
        Controls.Add(pnlButtons);
        Controls.Add(pnlPath);
        Controls.Add(pnlHeader);
    }

    private Button CreateButton(string text, Color backColor, int width, int height)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(width, height),
            BackColor = backColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 8, 8),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(Log), message);
            return;
        }

        string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        txtLog.AppendText(entry + Environment.NewLine);
    }

    private void LoadDetectedPath()
    {
        Log("Поиск установки Bongo Cat...");
        currentAssemblyPath = PatcherCore.ResolveAssemblyPath(null, Log);

        if (!string.IsNullOrEmpty(currentAssemblyPath))
        {
            txtPath.Text = currentAssemblyPath;
            UpdateStatus();
        }
        else
        {
            Log("[-] Игра не найдена автоматически. Укажите путь вручную через 'Обзор...'.");
            lblStatus.Text = "ИГРА НЕ НАЙДЕНА";
            lblStatus.BackColor = Color.FromArgb(239, 68, 68);
        }

        UpdateSteamHookButton();
    }

    private void UpdateStatus(bool announce = false)
    {
        if (string.IsNullOrEmpty(currentAssemblyPath) || !File.Exists(currentAssemblyPath))
        {
            lblStatus.Text = "ФАЙЛ НЕ НАЙДЕН";
            lblStatus.BackColor = Color.FromArgb(239, 68, 68);
            return;
        }

        bool isPatched = PatcherCore.CheckIfPatched(currentAssemblyPath);
        if (isPatched)
        {
            lblStatus.Text = "✔ ПРОПАТЧЕНА";
            lblStatus.BackColor = Color.FromArgb(16, 185, 129);
            if (announce) Log("[i] Статус: Сборка пропатчена (все скины, постоянная экипировка и PawPass активны).");
        }
        else
        {
            lblStatus.Text = "ORIGINAL / ЧИСТАЯ";
            lblStatus.BackColor = Color.FromArgb(245, 158, 11);
            if (announce) Log("[i] Статус: Сборка оригинальная / чистая.");
        }

        UpdateSteamHookButton();
    }

    private void UpdateSteamHookButton()
    {
        bool hooked = SteamIntegration.IsSteamHookInstalled();
        if (hooked)
        {
            btnSteamHook.BackColor = Color.FromArgb(16, 185, 129);
            btnSteamHook.Text = "⚡ Steam Hook: ВКЛ";
        }
        else
        {
            btnSteamHook.BackColor = Color.FromArgb(139, 92, 246);
            btnSteamHook.Text = "⚡ Интеграция в Steam";
        }
    }

    private void OnLaunchClicked(object? sender, EventArgs e)
    {
        Log("Запуск Bongo Cat...");
        string? exePath = PatcherCore.ResolveGameExePath(currentAssemblyPath, Log);
        Task.Run(() =>
        {
            bool ok = PatcherCore.LaunchGame(exePath, null, Log);
            if (ok)
            {
                Invoke(() => UpdateStatus());
            }
        });
    }

    private void OnToggleSteamHookClicked(object? sender, EventArgs e)
    {
        string currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
        bool hooked = SteamIntegration.IsSteamHookInstalled(currentExe);

        if (hooked)
        {
            var res = MessageBox.Show(
                "Параметр запуска в Steam уже установлен.\n\nЖелаете отключить интеграцию в Steam?",
                "Steam Launch Hook",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                SteamIntegration.RemoveSteamLaunchHook(Log);
                UpdateSteamHookButton();
            }
        }
        else
        {
            bool ok = SteamIntegration.InstallSteamLaunchHook(currentExe, Log);
            UpdateSteamHookButton();
            if (ok)
            {
                string cmd = SteamIntegration.GetRecommendedLaunchOption(currentExe);
                try { Clipboard.SetText(cmd); } catch { }
                MessageBox.Show(
                    "Параметр запуска успешно привязан к Bongo Cat в Steam!\n\n" +
                    $"Параметр запуска: {cmd}\n(также скопирован в буфер обмена)",
                    "Успех",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Выберите Assembly-CSharp.dll или BongoCat.exe",
            Filter = "Исполняемые файлы и библиотеки (*.dll;*.exe)|*.dll;*.exe|Все файлы (*.*)|*.*"
        };

        if (ofd.ShowDialog() == DialogResult.OK)
        {
            string resolved = PatcherCore.ResolveAssemblyPath(ofd.FileName, Log) ?? ofd.FileName;
            txtPath.Text = resolved;
        }
    }

    private void OnPatchClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentAssemblyPath) || !File.Exists(currentAssemblyPath))
        {
            MessageBox.Show("Укажите корректный путь к Assembly-CSharp.dll перед применением патча.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Log("Запуск процесса патчинга сборки...");
        bool ok = PatcherCore.ApplyPatch(currentAssemblyPath, Log);
        if (ok)
        {
            Log("");
            Log("==================================================");
            Log("🎉 Доступные горячие клавиши в игре:");
            Log("  [F7]  - Заспавнить кота-клона (синхронные лапки + скин)");
            Log("  [F5]  - Удалить последнего кота-клона");
            Log("  [F9]  - Случайно переодеть всех клонов");
            Log("  [F10] - Собрать все награды PawPass в 1 клик");
            Log("==================================================");
        }
        UpdateStatus();

        if (ok)
        {
            Log("[V] Патч успешно завершён!");
            MessageBox.Show("Патч успешно применён!\n\nВсе скины, аксессуары и PawPass разблокированы (баланс монет не изменён).", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnRestoreClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentAssemblyPath) || !File.Exists(currentAssemblyPath))
        {
            MessageBox.Show("Укажите корректный путь к Assembly-CSharp.dll.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Log("Восстановление оригинальной сборки...");
        bool ok = PatcherCore.Restore(currentAssemblyPath, Log);
        UpdateStatus();

        if (ok)
        {
            Log("[V] Сборка возвращена в исходное состояние.");
            MessageBox.Show("Оригинальная сборка успешно восстановлена!", "Восстановление", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnOpenModsClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentAssemblyPath)) return;
        string modsDir = PatcherCore.EnsureModsDirectory(currentAssemblyPath, Log);
        if (Directory.Exists(modsDir))
        {
            Process.Start("explorer.exe", modsDir);
        }
    }

    private void OnOpenGameDirClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentAssemblyPath)) return;
        string? gameDir = Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(currentAssemblyPath)!)!.FullName)?.FullName;
        if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
        {
            Process.Start("explorer.exe", gameDir);
        }
    }

    private void OnToggleWatcherClicked(object? sender, EventArgs e)
    {
        if (watcherCts != null)
        {
            watcherCts.Cancel();
            watcherCts = null;
            btnWatch.BackColor = Color.FromArgb(107, 114, 128);
            btnWatch.Text = "🛡️ Защита Steam";
            Log("[i] Защита Steam Update Guardian отключена.");
            return;
        }

        if (string.IsNullOrEmpty(currentAssemblyPath) || !File.Exists(currentAssemblyPath))
        {
            MessageBox.Show("Сначала укажите путь к игре.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        watcherCts = new CancellationTokenSource();
        btnWatch.BackColor = Color.FromArgb(16, 185, 129);
        btnWatch.Text = "🛡️ Защита активна";

        Task.Run(() => PatcherCore.StartWatcher(currentAssemblyPath, Log, watcherCts.Token));
    }
}
