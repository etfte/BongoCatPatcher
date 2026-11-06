using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BongoCatPatcher;

class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string? customPath = null;
        bool isRestore = false;
        bool isStatusCheck = false;
        bool isWatch = false;
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

        Action<string> log = msg =>
        {
            if (msg.StartsWith("[-]")) Console.ForegroundColor = ConsoleColor.Red;
            else if (msg.StartsWith("[+]") || msg.StartsWith("[V]")) Console.ForegroundColor = ConsoleColor.Green;
            else if (msg.StartsWith("[!]")) Console.ForegroundColor = ConsoleColor.Yellow;
            else if (msg.StartsWith("[*]") || msg.StartsWith("[i]")) Console.ForegroundColor = ConsoleColor.Cyan;
            else Console.ForegroundColor = ConsoleColor.Gray;

            Console.WriteLine(msg);
            Console.ResetColor();
        };

        string? assemblyPath = PatcherCore.ResolveAssemblyPath(customPath, log);
        if (assemblyPath == null || !File.Exists(assemblyPath))
        {
            log("[-] Не удалось найти файл Assembly-CSharp.dll!");
            log("    Укажите путь вручную с помощью параметра: --path \"<путь к папке BongoCat>\"");
            return 1;
        }

        bool isPatched = PatcherCore.CheckIfPatched(assemblyPath);

        if (isStatusCheck)
        {
            Console.WriteLine();
            log($"[STATUS] Сборка: {(isPatched ? "ПРОПАТЧЕНА (Вкладка мода в меню игры, скины, персистентность, PawPass и 999 999 монет активны)" : "ОРИГИНАЛЬНАЯ / ЧИСТАЯ")}");
            return 0;
        }

        if (isRestore)
        {
            return PatcherCore.Restore(assemblyPath, log) ? 0 : 1;
        }

        if (isWatch)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            log("[*] Запуск в режиме защиты от обновлений Steam (нажмите Ctrl+C для выхода)...");
            PatcherCore.StartWatcher(assemblyPath, log, cts.Token);
            return 0;
        }

        // Default CLI action: Apply patch
        if (isPatched)
        {
            log("[!] Сборка уже пропатчена. Применяем актуальное обновление патчей v4.0...");
        }

        bool success = PatcherCore.ApplyPatch(assemblyPath, log);
        if (success)
        {
            Console.WriteLine();
            log("[V] Полный патч v4.0 успешно применён!");
            log("[i] В игре доступна отдельная вкладка с настройками мода прямо в меню Bongo Cat (кнопка во вкладках меню, без биндов)!");
            return 0;
        }

        return 1;
    }

    static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(@"
=====================================================
          Bongo Cat Assembly Patcher (v4.0)          
   In-Game Mod Menu, Persistence, PawPass, Soundpacks
=====================================================
");
        Console.ResetColor();
    }

    static void PrintHelp()
    {
        Console.WriteLine("Использование: BongoCatPatcher [ОПЦИИ]");
        Console.WriteLine();
        Console.WriteLine("Опции:");
        Console.WriteLine("  --gui, -g            Открыть графический интерфейс (по умолчанию при двойном клике)");
        Console.WriteLine("  --cli                Принудительный запуск в режиме командной строки");
        Console.WriteLine("  -p, --path <путь>    Указать путь к папке игры или к Assembly-CSharp.dll");
        Console.WriteLine("  -s, --status         Проверить статус сборки");
        Console.WriteLine("  -r, --restore        Восстановить оригинальную сборку из резервной копии");
        Console.WriteLine("  -w, --watch          Запустить Steam Update Guardian (авто-патч при обновлениях)");
        Console.WriteLine("  -h, --help           Показать эту справку");
        Console.WriteLine();
        Console.WriteLine("Примеры:");
        Console.WriteLine("  BongoCatPatcher                     # Открыть GUI (или запустить с --cli для консоли)");
        Console.WriteLine("  BongoCatPatcher --cli               # Применить патч в консольном режиме");
        Console.WriteLine("  BongoCatPatcher --cli --status      # Проверить статус сборки");
        Console.WriteLine("  BongoCatPatcher --cli --watch       # Фоновый мониторинг обновлений Steam");
        Console.WriteLine("  BongoCatPatcher --cli --restore     # Откатить изменения");
    }
}
