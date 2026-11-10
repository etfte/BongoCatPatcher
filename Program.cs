using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BongoCatPatcher;

class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 1. Steam Launch: Fast & Direct
        if (args.Length > 0 && args[0].Equals("--steam-launch", StringComparison.OrdinalIgnoreCase))
        {
            string? gameExe = args.Length > 1 ? args[1] : null;
            string[] extraArgs = args.Length > 2 ? args.Skip(2).ToArray() : Array.Empty<string>();

            Action<string> log = msg =>
            {
                try { Console.WriteLine(msg); } catch { }
            };

            bool launched = PatcherCore.LaunchGame(gameExe, extraArgs, log);
            return launched ? 0 : 1;
        }

        string? customPath = null;
        bool isRestore = false;
        bool isStatusCheck = false;
        bool isWatch = false;
        bool isLaunch = false;
        bool isInstallSteamHook = false;
        bool isRemoveSteamHook = false;
        bool forceCli = false;
        bool forceGui = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            if (arg is "-h" or "--help")
            {
                PrintBanner();
                PrintHelp();
                return 0;
            }
            if (arg is "-g" or "--gui") forceGui = true;
            else if (arg is "--cli") forceCli = true;
            else if (arg is "-w" or "--watch") isWatch = true;
            else if (arg is "-r" or "--restore" or "--uninstall") isRestore = true;
            else if (arg is "-s" or "--status" or "--check") isStatusCheck = true;
            else if (arg is "-l" or "--launch") isLaunch = true;
            else if (arg is "--install-steam-hook") isInstallSteamHook = true;
            else if (arg is "--remove-steam-hook") isRemoveSteamHook = true;
            else if (arg is "-p" or "--path")
            {
                if (i + 1 < args.Length)
                {
                    customPath = args[++i];
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[-] Ошибка: параметр --path требует указания пути.");
                    Console.ResetColor();
                    return 1;
                }
            }
        }

        // If no arguments or explicitly asked for GUI, launch Windows Forms UI
        if (forceGui || (args.Length == 0 && !forceCli))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PatcherForm());
            return 0;
        }

        PrintBanner();

        Action<string> logCli = msg =>
        {
            if (msg.StartsWith("[-]")) Console.ForegroundColor = ConsoleColor.Red;
            else if (msg.StartsWith("[+]") || msg.StartsWith("[V]")) Console.ForegroundColor = ConsoleColor.Green;
            else if (msg.StartsWith("[!]")) Console.ForegroundColor = ConsoleColor.Yellow;
            else if (msg.StartsWith("[*]") || msg.StartsWith("[i]")) Console.ForegroundColor = ConsoleColor.Cyan;
            else Console.ForegroundColor = ConsoleColor.Gray;

            Console.WriteLine(msg);
            Console.ResetColor();
        };

        if (isInstallSteamHook)
        {
            string currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
            return SteamIntegration.InstallSteamLaunchHook(currentExe, logCli) ? 0 : 1;
        }

        if (isRemoveSteamHook)
        {
            return SteamIntegration.RemoveSteamLaunchHook(logCli) ? 0 : 1;
        }

        if (isLaunch)
        {
            string? gameExe = PatcherCore.ResolveGameExePath(customPath, logCli);
            return PatcherCore.LaunchGame(gameExe, null, logCli) ? 0 : 1;
        }

        string? assemblyPath = PatcherCore.ResolveAssemblyPath(customPath, logCli);
        if (assemblyPath == null || !File.Exists(assemblyPath))
        {
            logCli("[-] Не удалось найти файл Assembly-CSharp.dll!");
            logCli("    Укажите путь вручную с помощью параметра: --path \"<путь к папке BongoCat>\"");
            return 1;
        }

        bool isPatched = PatcherCore.CheckIfPatched(assemblyPath);

        if (isStatusCheck)
        {
            Console.WriteLine();
            logCli($"[STATUS] Сборка: {(isPatched ? "ПРОПАТЧЕНА (все скины, экипировка и PawPass разблокированы)" : "ОРИГИНАЛЬНАЯ / ЧИСТАЯ")}");
            bool hookInstalled = SteamIntegration.IsSteamHookInstalled();
            logCli($"[STATUS] Steam Launch Hook: {(hookInstalled ? "АКТИВЕН" : "НЕ АКТИВЕН")}");
            return 0;
        }

        if (isRestore)
        {
            return PatcherCore.Restore(assemblyPath, logCli) ? 0 : 1;
        }

        if (isWatch)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            logCli("[*] Запуск в режиме защиты от обновлений Steam (нажмите Ctrl+C для выхода)...");
            PatcherCore.StartWatcher(assemblyPath, logCli, cts.Token);
            return 0;
        }

        // Default CLI action: Apply patch
        if (isPatched)
        {
            logCli("[!] Сборка уже пропатчена. Применяем актуальное обновление патчей...");
        }

        bool success = PatcherCore.ApplyPatch(assemblyPath, logCli);
        if (success)
        {
            Console.WriteLine();
            logCli("[V] Патч успешно применён! Все скины и PawPass разблокированы.");
            return 0;
        }

        return 1;
    }

    static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(@"
=====================================================
            Bongo Cat Assembly Patcher               
       Skins, Cosmetics & PawPass Unlock             
=====================================================
");
        Console.ResetColor();
    }

    static void PrintHelp()
    {
        Console.WriteLine("Использование: BongoCatPatcher [ОПЦИИ]");
        Console.WriteLine();
        Console.WriteLine("Опции:");
        Console.WriteLine("  --gui, -g                 Открыть графический интерфейс (по умолчанию при даблклике)");
        Console.WriteLine("  --cli                     Принудительный режим командной строки");
        Console.WriteLine("  -l, --launch              Запустить Bongo Cat напрямую");
        Console.WriteLine("  --steam-launch <exe>      Режим запуска для Steam Launch Options");
        Console.WriteLine("  --install-steam-hook      Привязать запуск через утилиту в Steam");
        Console.WriteLine("  --remove-steam-hook       Удалить привязку из Steam");
        Console.WriteLine("  -p, --path <путь>         Указать путь к папке игры или к Assembly-CSharp.dll");
        Console.WriteLine("  -s, --status              Проверить статус сборки и хука Steam");
        Console.WriteLine("  -r, --restore             Восстановить оригинальную сборку из резервной копии");
        Console.WriteLine("  -w, --watch               Запустить Steam Update Guardian (мониторинг)");
        Console.WriteLine("  -h, --help                Показать эту справку");
        Console.WriteLine();
        Console.WriteLine("Примеры:");
        Console.WriteLine("  BongoCatPatcher --launch              # Запустить игру");
        Console.WriteLine("  BongoCatPatcher --cli --restore       # Восстановить оригинальную сборку");
        Console.WriteLine("  BongoCatPatcher --cli --status        # Проверить статус сборки");
    }
}
