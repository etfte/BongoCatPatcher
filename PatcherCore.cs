using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BongoCatPatcher;

public static class PatcherCore
{
    public static string? ResolveAssemblyPath(string? customPath = null, Action<string>? log = null)
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

        log?.Invoke("[*] Поиск Bongo Cat через реестр Steam и библиотеки...");

        var candidates = GetSteamLibraryFolders();
        foreach (var library in candidates)
        {
            string checkPath = Path.Combine(library, "steamapps", "common", "BongoCat", "BongoCat_Data", "Managed", "Assembly-CSharp.dll");
            if (File.Exists(checkPath))
            {
                log?.Invoke($"[+] Игра найдена в библиотеке Steam: {library}");
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
            if (File.Exists(path))
            {
                log?.Invoke($"[+] Игра найдена по стандартному пути: {path}");
                return path;
            }
        }

        return null;
    }

    public static List<string> GetSteamLibraryFolders()
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

    public static bool CheckIfPatched(string assemblyPath)
    {
        try
        {
            string managedDir = Path.GetDirectoryName(assemblyPath)!;
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

    public static bool Restore(string assemblyPath, Action<string>? log = null)
    {
        string managedDir = Path.GetDirectoryName(assemblyPath)!;
        string backupPath = Path.Combine(managedDir, "Assembly-CSharp.dll.bak");
        string cleanBackupPath = Path.Combine(managedDir, "Assembly-CSharp.dll.clean.bak");

        string? sourceBackup = File.Exists(backupPath) ? backupPath : (File.Exists(cleanBackupPath) ? cleanBackupPath : null);

        if (sourceBackup == null)
        {
            log?.Invoke("[-] Файл резервной копии не найден! (Assembly-CSharp.dll.bak отсутствует)");
            return false;
        }

        try
        {
            File.Copy(sourceBackup, assemblyPath, overwrite: true);
            log?.Invoke($"[+] Сборка успешно восстановлена из бэкапа: {sourceBackup}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[-] Ошибка при восстановлении: {ex.Message}");
            return false;
        }
    }

    public static bool ApplyPatch(string assemblyPath, Action<string>? log = null)
    {
        string managedDir = Path.GetDirectoryName(assemblyPath)!;
        string backupPath = Path.Combine(managedDir, "Assembly-CSharp.dll.bak");
        string cleanBackupPath = Path.Combine(managedDir, "Assembly-CSharp.dll.clean.bak");

        // Create backup if missing
        if (!File.Exists(backupPath))
        {
            try
            {
                File.Copy(assemblyPath, backupPath, overwrite: false);
                log?.Invoke($"[+] Создана резервная копия: {backupPath}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[-] Предупреждение при создании бэкапа: {ex.Message}");
            }
        }

        if (!File.Exists(cleanBackupPath))
        {
            try
            {
                File.Copy(assemblyPath, cleanBackupPath, overwrite: false);
            }
            catch { }
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadWrite = true
        });

        var module = assembly.MainModule;

        // 1. SteamItemRuntime: IsOwned -> true, IsHiddenInInventory -> false
        var steamItemRuntime = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.SteamItemRuntime");
        if (steamItemRuntime != null)
        {
            var isOwned = steamItemRuntime.Methods.FirstOrDefault(m => m.Name == "get_IsOwned");
            if (isOwned != null)
            {
                PatchReturnBool(isOwned, true);
                log?.Invoke("  [+] SteamItemRuntime.IsOwned -> true (все предметы открыты)");
            }

            var isHidden = steamItemRuntime.Methods.FirstOrDefault(m => m.Name == "get_IsHiddenInInventory");
            if (isHidden != null)
            {
                PatchReturnBool(isHidden, false);
                log?.Invoke("  [+] SteamItemRuntime.IsHiddenInInventory -> false (ничего не скрыто)");
            }
        }

        // 2. ItemCollection: IsRevealed -> true
        var itemCollection = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.ItemCollection");
        if (itemCollection != null)
        {
            var isRevealed = itemCollection.Methods.FirstOrDefault(m => m.Name == "IsRevealed");
            if (isRevealed != null)
            {
                PatchReturnBool(isRevealed, true);
                log?.Invoke("  [+] ItemCollection.IsRevealed -> true (событийные скины видимы)");
            }
        }

        // 3. CatCosmetics: EquipItem flag = false, Validate -> validated = true
        var catCosmetics = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.CatCosmetics");
        if (catCosmetics != null)
        {
            var equipItem = catCosmetics.Methods.FirstOrDefault(m => m.Name == "EquipItem");
            if (equipItem != null)
            {
                PatchEquipItem(equipItem);
                log?.Invoke("  [+] CatCosmetics.EquipItem -> сохранение в постоянные скины (EQUIPPED_ITEMS_3)");
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
                    log?.Invoke("  [+] CatCosmetics.Validate -> защита экипировки от сброса при перезапуске");
                }
            }
        }

        // 4. EmoteDonut: Validate -> return;
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
                log?.Invoke("  [+] EmoteDonut.Validate -> защита от сброса колеса эмоций");
            }
        }

        // 5. PawPass: HasFreePass -> true, HasPremiumPass -> true
        var pawPassEntity = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.PawPass.PawPassEntity");
        if (pawPassEntity != null)
        {
            var freePass = pawPassEntity.Methods.FirstOrDefault(m => m.Name == "get_HasFreePass");
            if (freePass != null)
            {
                PatchReturnBool(freePass, true);
                log?.Invoke("  [+] PawPassEntity.HasFreePass -> true (бесплатный пропуск лапок)");
            }

            var premPass = pawPassEntity.Methods.FirstOrDefault(m => m.Name == "get_HasPremiumPass");
            if (premPass != null)
            {
                PatchReturnBool(premPass, true);
                log?.Invoke("  [+] PawPassEntity.HasPremiumPass -> true (премиальный пропуск лапок)");
            }
        }

        // 6. CoinStash: Refresh -> 999 999 coins
        var coinStash = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.CoinStash");
        if (coinStash != null)
        {
            var refresh = coinStash.Methods.FirstOrDefault(m => m.Name == "Refresh");
            if (refresh != null)
            {
                PatchCoinStash(refresh);
                log?.Invoke("  [+] CoinStash.Refresh -> 999 999 монет в копилке");
            }
        }

        try
        {
            assembly.Write();
            log?.Invoke("[V] Сборка Assembly-CSharp.dll успешно сохранена!");

            // Prepare Mods/Skins directory
            EnsureModsDirectory(assemblyPath, log);

            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[-] Ошибка сохранения сборки: {ex.Message}");
            return false;
        }
    }

    private static void PatchReturnBool(MethodDefinition method, bool returnValue)
    {
        method.Body.Instructions.Clear();
        method.Body.Variables.Clear();
        method.Body.ExceptionHandlers.Clear();

        var il = method.Body.GetILProcessor();
        il.Emit(returnValue ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private static void PatchEquipItem(MethodDefinition equipItem)
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

    private static void PatchCoinStash(MethodDefinition refresh)
    {
        var stloc0 = refresh.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Stloc_0);
        if (stloc0 != null)
        {
            var il = refresh.Body.GetILProcessor();
            var curr = refresh.Body.Instructions.First();
            while (curr != stloc0)
            {
                var next = curr.Next;
                il.Remove(curr);
                curr = next!;
            }
            il.InsertBefore(stloc0, il.Create(OpCodes.Ldc_I4, 999999));
        }
    }

    public static string EnsureModsDirectory(string assemblyPath, Action<string>? log = null)
    {
        try
        {
            string gameRoot = Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(assemblyPath)!)!.FullName)!.FullName;
            string modsDir = Path.Combine(gameRoot, "Mods", "Skins");
            if (!Directory.Exists(modsDir))
            {
                Directory.CreateDirectory(modsDir);
                log?.Invoke($"[+] Создана папка для кастомных скинов: {modsDir}");

                string readmePath = Path.Combine(modsDir, "README.txt");
                if (!File.Exists(readmePath))
                {
                    File.WriteAllText(readmePath,
                        "Bongo Cat Custom Skins Folder\n" +
                        "=============================\n" +
                        "Поместите сюда PNG-изображения (например, CatDefault.png, CowboyHat.png, BongosDefault.png)\n" +
                        "для автоматической подмены спрайтов в игре.\n");
                }
            }
            return modsDir;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[-] Не удалось инициализировать папку Mods: {ex.Message}");
            return string.Empty;
        }
    }

    public static void StartWatcher(string assemblyPath, Action<string> log, CancellationToken cancellationToken)
    {
        string managedDir = Path.GetDirectoryName(assemblyPath)!;
        string targetFile = Path.GetFileName(assemblyPath);

        log($"[i] Steam Update Guardian запущен. Отслеживание изменений в: {managedDir}");

        using var watcher = new FileSystemWatcher(managedDir, targetFile);
        watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;

        DateTime lastPatchTime = DateTime.MinValue;

        FileSystemEventHandler onChange = (sender, e) =>
        {
            if (DateTime.Now - lastPatchTime < TimeSpan.FromSeconds(3)) return;

            log($"[!] Замечено изменение файла {targetFile} (возможно обновление игры в Steam)!");
            Thread.Sleep(1500); // Даем Steam закончить запись

            if (!CheckIfPatched(assemblyPath))
            {
                log("[*] Сборка вернулась к оригинальной. Автоматическое применение патча...");
                if (ApplyPatch(assemblyPath, log))
                {
                    lastPatchTime = DateTime.Now;
                    log("[V] Авто-патч успешно завершён!");
                }
            }
            else
            {
                log("[i] Сборка уже пропатчена, повторное применение не требуется.");
            }
        };

        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.EnableRaisingEvents = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            Thread.Sleep(500);
        }

        log("[i] Steam Update Guardian остановлен.");
    }
}
