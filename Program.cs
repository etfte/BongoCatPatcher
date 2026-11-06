using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BongoCatPatcher;

class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintBanner();

        string? customPath = null;
        bool isRestore = false;
        bool isStatusCheck = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            if (arg is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }
            if (arg is "-r" or "--restore" or "--uninstall")
            {
                isRestore = true;
            }
            else if (arg is "-s" or "--status" or "--check")
            {
                isStatusCheck = true;
            }
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

        string? assemblyPath = ResolveAssemblyPath(customPath);
        if (assemblyPath == null || !File.Exists(assemblyPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] Не удалось найти файл Assembly-CSharp.dll!");
            Console.WriteLine("    Укажите путь вручную с помощью параметра: --path \"<путь к папке BongoCat>\"");
            Console.ResetColor();
            return 1;
        }

        string managedDir = Path.GetDirectoryName(assemblyPath)!;
        string backupPath = Path.Combine(managedDir, "Assembly-CSharp.dll.bak");
        string cleanBackupPath = Path.Combine(managedDir, "Assembly-CSharp.dll.clean.bak");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[i] Целевая сборка: {assemblyPath}");
        Console.ResetColor();

        bool isPatched = CheckIfPatched(assemblyPath, managedDir);

        if (isStatusCheck)
        {
            Console.WriteLine();
            Console.ForegroundColor = isPatched ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"[STATUS] Сборка: {(isPatched ? "ПРОПАТЧЕНА (все скины и персистентность разблокированы)" : "ОРИГИНАЛЬНАЯ / ЧИСТАЯ")}");
            Console.ResetColor();
            return 0;
        }

        if (isRestore)
        {
            return HandleRestore(assemblyPath, backupPath, cleanBackupPath);
        }

        return HandlePatch(assemblyPath, managedDir, backupPath, isPatched);
    }

    static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(@"
=====================================================
          Bongo Cat Assembly Patcher (v2.0)          
      Static IL Patcher with Persistence & Auto-Steam 
=====================================================
");
        Console.ResetColor();
    }

    static void PrintHelp()
    {
        Console.WriteLine("Использование: BongoCatPatcher [ОПЦИИ]");
        Console.WriteLine();
        Console.WriteLine("Опции:");
        Console.WriteLine("  -p, --path <путь>    Указать путь к папке игры или к Assembly-CSharp.dll");
        Console.WriteLine("  -s, --status         Проверить статус сборки (пропатчена или оригинальная)");
        Console.WriteLine("  -r, --restore        Восстановить оригинальную сборку из резервной копии");
        Console.WriteLine("  -h, --help           Показать эту справку");
        Console.WriteLine();
        Console.WriteLine("Примеры:");
        Console.WriteLine("  BongoCatPatcher                     # Автопоиск и применение полного патча");
        Console.WriteLine("  BongoCatPatcher --status            # Проверить статус");
        Console.WriteLine("  BongoCatPatcher --restore           # Откатить изменения");
        Console.WriteLine("  BongoCatPatcher -p \"D:\\BongoCat\"   # Указать пользовательский путь");
    }

    static string? ResolveAssemblyPath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            string clean = customPath.Trim('\"', '\'');
            if (File.Exists(clean) && Path.GetFileName(clean).Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
                return clean;
            if (Directory.Exists(clean))
            {
                string inRoot = Path.Combine(clean, "Assembly-CSharp.dll");
                if (File.Exists(inRoot)) return inRoot;
                string inManaged = Path.Combine(clean, "BongoCat_Data", "Managed", "Assembly-CSharp.dll");
                if (File.Exists(inManaged)) return inManaged;
                string inDirectManaged = Path.Combine(clean, "Managed", "Assembly-CSharp.dll");
                if (File.Exists(inDirectManaged)) return inDirectManaged;
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("[*] Поиск установки Bongo Cat через Steam Registry и библиотеки...");
        Console.ResetColor();

        var candidates = GetSteamLibraryFolders();
        foreach (var library in candidates)
        {
            string checkPath = Path.Combine(library, "steamapps", "common", "BongoCat", "BongoCat_Data", "Managed", "Assembly-CSharp.dll");
            if (File.Exists(checkPath))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] Игра автоматически обнаружена в: {library}");
                Console.ResetColor();
                return checkPath;
            }
        }

        string[] defaultPaths =
        {
            @"D:\SteamLibrary\steamapps\common\BongoCat\BongoCat_Data\Managed\Assembly-CSharp.dll",
            @"C:\Program Files (x86)\Steam\steamapps\common\BongoCat\BongoCat_Data\Managed\Assembly-CSharp.dll",
            @"C:\SteamLibrary\steamapps\common\BongoCat\BongoCat_Data\Managed\Assembly-CSharp.dll",
            @"E:\SteamLibrary\steamapps\common\BongoCat\BongoCat_Data\Managed\Assembly-CSharp.dll"
        };

        foreach (var path in defaultPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    static List<string> GetSteamLibraryFolders()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steamPath)
            {
                steamPath = steamPath.Replace('/', '\\');
                folders.Add(steamPath);

                string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    string content = File.ReadAllText(vdf);
                    var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match m in matches)
                    {
                        if (m.Groups.Count > 1)
                        {
                            string p = m.Groups[1].Value.Replace(@"\\", @"\");
                            if (Directory.Exists(p))
                            {
                                folders.Add(p);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return folders.ToList();
    }

    static bool CheckIfPatched(string assemblyPath, string managedDir)
    {
        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedDir);

            using var asm = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { AssemblyResolver = resolver });
            var type = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "BongoCat.SteamItemRuntime");
            var isOwned = type?.Methods.FirstOrDefault(m => m.Name == "get_IsOwned");
            if (isOwned == null || isOwned.Body.Instructions.Count < 2) return false;

            return isOwned.Body.Instructions[0].OpCode == OpCodes.Ldc_I4_1 &&
                   isOwned.Body.Instructions[1].OpCode == OpCodes.Ret;
        }
        catch
        {
            return false;
        }
    }

    static int HandleRestore(string assemblyPath, string backupPath, string cleanBackupPath)
    {
        string? sourceBackup = File.Exists(backupPath) ? backupPath : (File.Exists(cleanBackupPath) ? cleanBackupPath : null);

        if (sourceBackup == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[-] Файл резервной копии не найден! (Assembly-CSharp.dll.bak отсутствует)");
            Console.ResetColor();
            return 1;
        }

        try
        {
            File.Copy(sourceBackup, assemblyPath, overwrite: true);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] Сборка успешно восстановлена из бэкапа: {sourceBackup}");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[-] Ошибка при восстановлении: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    static int HandlePatch(string assemblyPath, string managedDir, string backupPath, bool alreadyPatched)
    {
        if (alreadyPatched)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[!] Сборка уже пропатчена. Применяем повторное обновление компонентов...");
            Console.ResetColor();
        }

        if (!File.Exists(backupPath))
        {
            try
            {
                File.Copy(assemblyPath, backupPath, overwrite: false);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[+] Создана резервная копия: {backupPath}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] Не удалось создать бэкап: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadWrite = true
        });

        var module = assembly.MainModule;

        // 1. SteamItemRuntime.get_IsOwned -> true
        var steamItemRuntime = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.SteamItemRuntime");
        if (steamItemRuntime != null)
        {
            var isOwned = steamItemRuntime.Methods.FirstOrDefault(m => m.Name == "get_IsOwned");
            if (isOwned != null)
            {
                PatchReturnBool(isOwned, true);
                Console.WriteLine("  [+] BongoCat.SteamItemRuntime.IsOwned -> true (все предметы открыты)");
            }

            var isHidden = steamItemRuntime.Methods.FirstOrDefault(m => m.Name == "get_IsHiddenInInventory");
            if (isHidden != null)
            {
                PatchReturnBool(isHidden, false);
                Console.WriteLine("  [+] BongoCat.SteamItemRuntime.IsHiddenInInventory -> false (ничего не скрыто)");
            }
        }

        // 2. ItemCollection.IsRevealed -> true
        var itemCollection = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.ItemCollection");
        if (itemCollection != null)
        {
            var isRevealed = itemCollection.Methods.FirstOrDefault(m => m.Name == "IsRevealed");
            if (isRevealed != null)
            {
                PatchReturnBool(isRevealed, true);
                Console.WriteLine("  [+] BongoCat.ItemCollection.IsRevealed -> true (событийные скины видимы)");
            }
        }

        // 3. CatCosmetics: EquipItem flag = false (экипировка сохраняется как постоянная)
        var catCosmetics = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.CatCosmetics");
        if (catCosmetics != null)
        {
            var equipItem = catCosmetics.Methods.FirstOrDefault(m => m.Name == "EquipItem");
            if (equipItem != null)
            {
                PatchEquipItem(equipItem);
                Console.WriteLine("  [+] BongoCat.CatCosmetics.EquipItem -> сохранение в постоянные скины");
            }

            var validate = catCosmetics.Methods.FirstOrDefault(m => m.Name == "Validate");
            if (validate != null)
            {
                var validatedField = catCosmetics.Fields.FirstOrDefault(f => f.Name == "validated");
                if (validatedField != null)
                {
                    validate.Body.Instructions.Clear();
                    validate.Body.Variables.Clear();
                    validate.Body.ExceptionHandlers.Clear();
                    var il = validate.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Stfld, validatedField);
                    il.Emit(OpCodes.Ret);
                    Console.WriteLine("  [+] BongoCat.CatCosmetics.Validate -> защита от сброса при перезапуске");
                }
            }
        }

        // 4. EmoteDonut: Validate -> return; (защита от сброса колеса эмоций)
        var emoteDonut = module.Types.FirstOrDefault(t => t.FullName == "EmoteDonut");
        if (emoteDonut != null)
        {
            var emoteValidate = emoteDonut.Methods.FirstOrDefault(m => m.Name == "Validate");
            if (emoteValidate != null)
            {
                emoteValidate.Body.Instructions.Clear();
                emoteValidate.Body.Variables.Clear();
                emoteValidate.Body.ExceptionHandlers.Clear();
                var il = emoteValidate.Body.GetILProcessor();
                il.Emit(OpCodes.Ret);
                Console.WriteLine("  [+] EmoteDonut.Validate -> защита от сброса колеса эмоций");
            }
        }

        try
        {
            assembly.Write();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[V] Патч успешно применён! Все скины разблокированы и персистентны.");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[-] Ошибка записи сборки: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    static void PatchReturnBool(MethodDefinition method, bool returnValue)
    {
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();

        var il = method.Body.GetILProcessor();
        il.Emit(returnValue ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    static void PatchEquipItem(MethodDefinition equipItem)
    {
        var stloc1 = equipItem.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Stloc_1);
        var stfldSteamItem = equipItem.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Stfld && i.Operand?.ToString()?.Contains("steamItem") == true);

        if (stloc1 != null && stfldSteamItem != null)
        {
            var il = equipItem.Body.GetILProcessor();
            var curr = stfldSteamItem.Next;
            while (curr != null && curr != stloc1)
            {
                var next = curr.Next;
                il.Remove(curr);
                curr = next;
            }
            il.InsertBefore(stloc1, il.Create(OpCodes.Ldc_I4_0));
        }
    }
}
