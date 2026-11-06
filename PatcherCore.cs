using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public static string? ResolveGameExePath(string? customPath = null, Action<string>? log = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            string clean = customPath.Trim('\"', '\'');
            if (File.Exists(clean) && Path.GetFileName(clean).Equals("BongoCat.exe", StringComparison.OrdinalIgnoreCase))
                return clean;
            if (File.Exists(clean) && Path.GetFileName(clean).Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
            {
                string exeCandidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(clean)!, "..", "..", "BongoCat.exe"));
                if (File.Exists(exeCandidate)) return exeCandidate;
            }
            if (Directory.Exists(clean))
            {
                string inRoot = Path.Combine(clean, "BongoCat.exe");
                if (File.Exists(inRoot)) return inRoot;
                string inSub = Path.Combine(clean, "BongoCat", "BongoCat.exe");
                if (File.Exists(inSub)) return inSub;
            }
        }

        var candidates = GetSteamLibraryFolders();
        foreach (var library in candidates)
        {
            string checkPath = Path.Combine(library, "steamapps", "common", "BongoCat", "BongoCat.exe");
            if (File.Exists(checkPath))
            {
                return checkPath;
            }
        }

        string[] defaultPaths =
        {
            @"D:\SteamLibrary\steamapps\common\BongoCat\BongoCat.exe",
            @"C:\Program Files (x86)\Steam\steamapps\common\BongoCat\BongoCat.exe",
            @"C:\SteamLibrary\steamapps\common\BongoCat\BongoCat.exe",
            @"E:\SteamLibrary\steamapps\common\BongoCat\BongoCat.exe"
        };

        foreach (var path in defaultPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

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

        // 6. CoinStash: Refresh -> read from PlayerPrefs("LOCAL_BONGO_COINS", 999999)
        var coinStash = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.CoinStash");
        if (coinStash != null)
        {
            var refresh = coinStash.Methods.FirstOrDefault(m => m.Name == "Refresh");
            if (refresh != null)
            {
                PatchCoinStash(refresh, module, resolver);
                log?.Invoke("  [+] CoinStash.Refresh -> динамический кошелёк (PlayerPrefs: LOCAL_BONGO_COINS)");
            }
        }

        // 7. SteamExchange: CanExchange -> true when coins > 0, Exchange -> local fulfillment & coin deduction
        var steamExchange = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.SteamJsonParser.SteamExchange");
        if (steamExchange != null)
        {
            var canExchange = steamExchange.Methods.FirstOrDefault(m => m.Name == "CanExchange");
            if (canExchange != null)
            {
                PatchCanExchange(canExchange, module, resolver);
                log?.Invoke("  [+] SteamExchange.CanExchange -> активная покупка за монеты в магазине");
            }

            var exchange = steamExchange.Methods.FirstOrDefault(m => m.Name == "Exchange");
            if (exchange != null)
            {
                PatchSteamExchange(steamExchange, module, resolver);
                log?.Invoke("  [+] SteamExchange.Exchange -> реальное списание монет, анимация и выдача наград");
            }
        }

        // 8. ChestExchanger: OpenChest -> bypass Steam token wait, grant reward & trigger animations
        var chestExchanger = module.Types.FirstOrDefault(t => t.FullName == "Steam.ChestExchanger");
        if (chestExchanger != null)
        {
            PatchChestExchanger(chestExchanger, module);
            log?.Invoke("  [+] ChestExchanger.OpenChest -> мгновенное открытие сундуков без ожидания токенов");
        }

        // 9. Pets: CanSpendPets -> true (сундуки всегда доступны для открытия)
        var pets = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.Pets");
        if (pets != null)
        {
            PatchPets(pets);
            log?.Invoke("  [+] Pets.CanSpendPets -> доступность открытия сундуков за клики");
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

    private static void PatchCoinStash(MethodDefinition refresh, ModuleDefinition module, DefaultAssemblyResolver resolver)
    {
        var stloc0 = refresh.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Stloc_0);
        if (stloc0 != null)
        {
            var coreMod = resolver.Resolve(new AssemblyNameReference("UnityEngine.CoreModule", new Version(0, 0, 0, 0)));
            var ppType = coreMod.MainModule.Types.First(t => t.FullName == "UnityEngine.PlayerPrefs");
            var getIntMethod = module.ImportReference(ppType.Methods.First(m => m.Name == "GetInt" && m.Parameters.Count == 2));

            var il = refresh.Body.GetILProcessor();
            var curr = refresh.Body.Instructions.First();
            while (curr != stloc0)
            {
                var next = curr.Next;
                il.Remove(curr);
                curr = next!;
            }

            il.InsertBefore(stloc0, il.Create(OpCodes.Ldstr, "LOCAL_BONGO_COINS"));
            il.InsertBefore(stloc0, il.Create(OpCodes.Ldc_I4, 999999));
            il.InsertBefore(stloc0, il.Create(OpCodes.Call, getIntMethod));
        }
    }

    private static void PatchCanExchange(MethodDefinition canExchange, ModuleDefinition module, DefaultAssemblyResolver resolver)
    {
        var coreMod = resolver.Resolve(new AssemblyNameReference("UnityEngine.CoreModule", new Version(0, 0, 0, 0)));
        var ppType = coreMod.MainModule.Types.First(t => t.FullName == "UnityEngine.PlayerPrefs");
        var getIntMethod = module.ImportReference(ppType.Methods.First(m => m.Name == "GetInt" && m.Parameters.Count == 2));

        canExchange.Body.Instructions.Clear();
        canExchange.Body.Variables.Clear();
        canExchange.Body.ExceptionHandlers.Clear();

        var il = canExchange.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, "LOCAL_BONGO_COINS");
        il.Emit(OpCodes.Ldc_I4, 999999);
        il.Emit(OpCodes.Call, getIntMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Ret);
    }

    private static void PatchSteamExchange(TypeDefinition steamExchange, ModuleDefinition module, DefaultAssemblyResolver resolver)
    {
        var exchange = steamExchange.Methods.FirstOrDefault(m => m.Name == "Exchange");
        if (exchange == null) return;

        // Grab existing ValueTuple ctor and Action invoke references from the method before clearing
        var tupleCtor = exchange.Body.Instructions
            .FirstOrDefault(i => i.OpCode == OpCodes.Newobj && i.Operand?.ToString()?.Contains("ValueTuple") == true)?.Operand as MethodReference;
        var actionInvoke = exchange.Body.Instructions
            .FirstOrDefault(i => i.OpCode == OpCodes.Callvirt && i.Operand?.ToString()?.Contains("Invoke") == true)?.Operand as MethodReference;

        if (tupleCtor == null || actionInvoke == null) return;

        // Resolve external assemblies
        var heathenAsm = resolver.Resolve(new AssemblyNameReference("Heathen.Steamworks", new Version(0, 0, 0, 0)));
        var steamworksAsm = resolver.Resolve(new AssemblyNameReference("com.rlabrecque.steamworks.net", new Version(0, 0, 0, 0)));
        var coreMod = resolver.Resolve(new AssemblyNameReference("UnityEngine.CoreModule", new Version(0, 0, 0, 0)));

        var itemDetailType = heathenAsm.MainModule.Types.First(t => t.FullName == "Heathen.SteamworksIntegration.ItemDetail");
        var itemDetailRef = module.ImportReference(itemDetailType);
        var itemDetailsField = module.ImportReference(itemDetailType.Fields.First(f => f.Name == "ItemDetails"));

        var steamItemDetailsType = steamworksAsm.MainModule.Types.First(t => t.FullName == "Steamworks.SteamItemDetails_t");
        var defField = module.ImportReference(steamItemDetailsType.Fields.First(f => f.Name == "m_iDefinition"));
        var qtyField = module.ImportReference(steamItemDetailsType.Fields.First(f => f.Name == "m_unQuantity"));

        var steamItemDefType = steamworksAsm.MainModule.Types.First(t => t.FullName == "Steamworks.SteamItemDef_t");
        var intDefField = module.ImportReference(steamItemDefType.Fields.First(f => f.Name == "m_SteamItemDef"));

        var ppType = coreMod.MainModule.Types.First(t => t.FullName == "UnityEngine.PlayerPrefs");
        var getIntMethod = module.ImportReference(ppType.Methods.First(m => m.Name == "GetInt" && m.Parameters.Count == 2));
        var setIntMethod = module.ImportReference(ppType.Methods.First(m => m.Name == "SetInt" && m.Parameters.Count == 2));
        var saveMethod = module.ImportReference(ppType.Methods.First(m => m.Name == "Save" && m.Parameters.Count == 0));

        // Resolve BongoCat types
        var catInventoryType = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.CatInventory");
        var catInvInstanceField = catInventoryType?.Fields.FirstOrDefault(f => f.Name == "Instance");
        var catInvCoinStashField = catInventoryType?.Fields.FirstOrDefault(f => f.Name == "_coinStash");
        var coinStashType = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.CoinStash");
        var coinStashRefreshMethod = coinStashType?.Methods.FirstOrDefault(m => m.Name == "Refresh");

        var inputField = steamExchange.Fields.FirstOrDefault(f => f.Name == "Input");
        var outputField = steamExchange.Fields.FirstOrDefault(f => f.Name == "Output");
        var idField = steamExchange.BaseType.Resolve().Fields.FirstOrDefault(f => f.Name == "Id");

        exchange.Body.Instructions.Clear();
        exchange.Body.Variables.Clear();
        exchange.Body.ExceptionHandlers.Clear();

        // Variables:
        // 0: int curCoins
        // 1: int cost
        // 2: int targetId
        // 3: ItemDetail[]
        var v0 = new VariableDefinition(module.TypeSystem.Int32);
        var v1 = new VariableDefinition(module.TypeSystem.Int32);
        var v2 = new VariableDefinition(module.TypeSystem.Int32);
        var v3 = new VariableDefinition(new ArrayType(itemDetailRef));
        exchange.Body.Variables.Add(v0);
        exchange.Body.Variables.Add(v1);
        exchange.Body.Variables.Add(v2);
        exchange.Body.Variables.Add(v3);

        var il = exchange.Body.GetILProcessor();

        // 1. cost = 100
        il.Emit(OpCodes.Ldc_I4, 100);
        il.Emit(OpCodes.Stloc_1);

        // If this.Input != null && this.Input.Count > 0: cost = this.Input[0].Amount
        var labelAfterInput = il.Create(OpCodes.Nop);
        if (inputField != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, inputField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelAfterInput);
            if (inputField.FieldType is GenericInstanceType inputListType)
            {
                var countMethod = new MethodReference("get_Count", module.TypeSystem.Int32, inputListType) { HasThis = true };
                var getItemMethod = new MethodReference("get_Item", inputListType.GenericArguments[0], inputListType) { HasThis = true };
                getItemMethod.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Callvirt, countMethod);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ble_S, labelAfterInput);

                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Callvirt, getItemMethod);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse_S, labelAfterInput);
                var exchangeInputType = inputListType.GenericArguments[0].Resolve();
                var amountField = module.ImportReference(exchangeInputType.Fields.First(f => f.Name == "Amount"));
                il.Emit(OpCodes.Ldfld, amountField);
                il.Emit(OpCodes.Stloc_1);
            }
            else
            {
                il.Emit(OpCodes.Pop);
            }
        }
        il.Append(labelAfterInput);

        // 2. curCoins = PlayerPrefs.GetInt("LOCAL_BONGO_COINS", 999999)
        il.Emit(OpCodes.Ldstr, "LOCAL_BONGO_COINS");
        il.Emit(OpCodes.Ldc_I4, 999999);
        il.Emit(OpCodes.Call, getIntMethod);
        il.Emit(OpCodes.Stloc_0);

        // curCoins = Math.Max(0, curCoins - cost)
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_0);
        var maxMethod = module.ImportReference(typeof(Math).GetMethod("Max", new[] { typeof(int), typeof(int) })!);
        il.Emit(OpCodes.Call, maxMethod);
        il.Emit(OpCodes.Stloc_0);

        // PlayerPrefs.SetInt("LOCAL_BONGO_COINS", curCoins); PlayerPrefs.Save();
        il.Emit(OpCodes.Ldstr, "LOCAL_BONGO_COINS");
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Call, setIntMethod);
        il.Emit(OpCodes.Call, saveMethod);

        // CatInventory.Instance._coinStash.Refresh()
        var labelAfterRefresh = il.Create(OpCodes.Nop);
        if (catInvInstanceField != null && catInvCoinStashField != null && coinStashRefreshMethod != null)
        {
            il.Emit(OpCodes.Ldsfld, catInvInstanceField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelAfterRefresh);
            il.Emit(OpCodes.Ldfld, catInvCoinStashField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelAfterRefresh);
            il.Emit(OpCodes.Callvirt, coinStashRefreshMethod);
        }
        il.Append(labelAfterRefresh);

        // 3. targetId = this.Id
        if (idField != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, idField);
            il.Emit(OpCodes.Stloc_2);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc_2);
        }

        // If this.Output != null && this.Output.Count > 0: targetId = this.Output[0].Item.Id
        var labelAfterOutput = il.Create(OpCodes.Nop);
        if (outputField != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, outputField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelAfterOutput);
            if (outputField.FieldType is GenericInstanceType outputListType)
            {
                var countMethod = new MethodReference("get_Count", module.TypeSystem.Int32, outputListType) { HasThis = true };
                var getItemMethod = new MethodReference("get_Item", outputListType.GenericArguments[0], outputListType) { HasThis = true };
                getItemMethod.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Callvirt, countMethod);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ble_S, labelAfterOutput);

                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Callvirt, getItemMethod);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse_S, labelAfterOutput);
                var exchangeOutputType = outputListType.GenericArguments[0].Resolve();
                var itemField = module.ImportReference(exchangeOutputType.Fields.First(f => f.Name == "Item"));
                il.Emit(OpCodes.Ldfld, itemField);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse_S, labelAfterOutput);
                var baseIdField = module.ImportReference(itemField.FieldType.Resolve().Fields.First(f => f.Name == "Id"));
                il.Emit(OpCodes.Ldfld, baseIdField);
                il.Emit(OpCodes.Stloc_2);
            }
            else
            {
                il.Emit(OpCodes.Pop);
            }
        }
        il.Append(labelAfterOutput);

        // 4. Construct ItemDetail[1]
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, itemDetailRef);
        il.Emit(OpCodes.Stloc_3);

        // details[0].ItemDetails.m_iDefinition.m_SteamItemDef = targetId
        il.Emit(OpCodes.Ldloc_3);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelema, itemDetailRef);
        il.Emit(OpCodes.Ldflda, itemDetailsField);
        il.Emit(OpCodes.Ldflda, defField);
        il.Emit(OpCodes.Ldloc_2);
        il.Emit(OpCodes.Stfld, intDefField);

        // details[0].ItemDetails.m_unQuantity = 1
        il.Emit(OpCodes.Ldloc_3);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelema, itemDetailRef);
        il.Emit(OpCodes.Ldflda, itemDetailsField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, qtyField);

        // 5. If callback != null: callback.Invoke(new ValueTuple<bool, ItemDetail[]>(true, details))
        var labelReturn = il.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse_S, labelReturn);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc_3);
        il.Emit(OpCodes.Newobj, tupleCtor);
        il.Emit(OpCodes.Callvirt, actionInvoke);

        il.Append(labelReturn);
    }

    private static void PatchChestExchanger(TypeDefinition chestExchanger, ModuleDefinition module)
    {
        var openChest = chestExchanger.Methods.FirstOrDefault(m => m.Name == "OpenChest");
        if (openChest == null) return;

        var catInvType = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.CatInventory");
        var catInvInstance = catInvType?.Fields.FirstOrDefault(f => f.Name == "Instance");
        var catInvItems = catInvType?.Fields.FirstOrDefault(f => f.Name == "Items");
        var updateVisuals = catInvType?.Methods.FirstOrDefault(m => m.Name == "UpdateVisuals");

        var achStatsType = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.Achievements.AchievementStats");
        var incItems = achStatsType?.Methods.FirstOrDefault(m => m.Name == "IncrementItems");

        var steamItemRuntimeType = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.SteamItemRuntime");
        var getIdMethod = steamItemRuntimeType?.Methods.FirstOrDefault(m => m.Name == "get_Id");

        var actionIntType = openChest.Parameters[0].ParameterType as GenericInstanceType;
        MethodReference? actionInvoke = null;
        if (actionIntType != null)
        {
            actionInvoke = new MethodReference("Invoke", module.TypeSystem.Void, actionIntType) { HasThis = true };
            actionInvoke.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        }

        openChest.Body.Instructions.Clear();
        openChest.Body.Variables.Clear();
        openChest.Body.ExceptionHandlers.Clear();

        // Variable 0: int rewardId = 1000
        // Variable 1: int count
        var v0 = new VariableDefinition(module.TypeSystem.Int32);
        var v1 = new VariableDefinition(module.TypeSystem.Int32);
        openChest.Body.Variables.Add(v0);
        openChest.Body.Variables.Add(v1);

        var il = openChest.Body.GetILProcessor();

        il.Emit(OpCodes.Ldc_I4, 1000);
        il.Emit(OpCodes.Stloc_0);

        // If CatInventory.Instance != null && CatInventory.Instance.Items != null && CatInventory.Instance.Items.Count > 0:
        // rewardId = CatInventory.Instance.Items[Random.Range(0, count)].Id
        var labelAfterPick = il.Create(OpCodes.Nop);
        if (catInvInstance != null && catInvItems != null && getIdMethod != null)
        {
            il.Emit(OpCodes.Ldsfld, catInvInstance);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelAfterPick);
            il.Emit(OpCodes.Ldfld, catInvItems);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelAfterPick);

            if (catInvItems.FieldType is GenericInstanceType itemsListType)
            {
                var countMethod = new MethodReference("get_Count", module.TypeSystem.Int32, itemsListType) { HasThis = true };
                var getItemMethod = new MethodReference("get_Item", itemsListType.GenericArguments[0], itemsListType) { HasThis = true };
                getItemMethod.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Callvirt, countMethod);
                il.Emit(OpCodes.Stloc_1);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ble_S, labelAfterPick);

                // UnityEngine.Random.Range(0, count)
                var coreMod = module.AssemblyResolver.Resolve(new AssemblyNameReference("UnityEngine.CoreModule", new Version(0, 0, 0, 0)));
                var randomType = coreMod.MainModule.Types.FirstOrDefault(t => t.FullName == "UnityEngine.Random");
                var rangeMethod = module.ImportReference(randomType?.Methods.FirstOrDefault(m => m.Name == "Range" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.FullName == "System.Int32"));

                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc_1);
                if (rangeMethod != null)
                {
                    il.Emit(OpCodes.Call, rangeMethod);
                }
                else
                {
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ldc_I4_0);
                }

                il.Emit(OpCodes.Callvirt, getItemMethod);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse_S, labelAfterPick);
                il.Emit(OpCodes.Callvirt, getIdMethod);
                il.Emit(OpCodes.Stloc_0);
            }
            else
            {
                il.Emit(OpCodes.Pop);
            }
        }
        il.Append(labelAfterPick);

        // AchievementStats.IncrementItems()
        if (incItems != null)
        {
            il.Emit(OpCodes.Call, incItems);
        }

        // callback?.Invoke(rewardId)
        var labelAfterCallback = il.Create(OpCodes.Nop);
        if (actionInvoke != null)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelAfterCallback);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, actionInvoke);
        }
        il.Append(labelAfterCallback);

        // CatInventory.Instance.UpdateVisuals()
        var labelRet = il.Create(OpCodes.Ret);
        if (catInvInstance != null && updateVisuals != null)
        {
            il.Emit(OpCodes.Ldsfld, catInvInstance);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, labelRet);
            il.Emit(OpCodes.Callvirt, updateVisuals);
        }
        il.Append(labelRet);
    }

    private static void PatchPets(TypeDefinition pets)
    {
        var canSpend = pets.Methods.FirstOrDefault(m => m.Name == "CanSpendPets");
        if (canSpend != null)
        {
            canSpend.Body.Instructions.Clear();
            canSpend.Body.Variables.Clear();
            canSpend.Body.ExceptionHandlers.Clear();
            var il = canSpend.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
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

    public static bool LaunchGameWithAutoUpdate(string? gameExePath, string[]? extraArgs, Action<string>? log)
    {
        if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
        {
            gameExePath = ResolveGameExePath(null, log);
        }

        if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
        {
            log?.Invoke("[-] Не удалось найти исполнимый файл BongoCat.exe!");
            return false;
        }

        string assemblyPath = Path.Combine(Path.GetDirectoryName(gameExePath)!, "BongoCat_Data", "Managed", "Assembly-CSharp.dll");
        if (File.Exists(assemblyPath))
        {
            bool isPatched = CheckIfPatched(assemblyPath);
            if (!isPatched)
            {
                log?.Invoke("[!] Замечено обновление игры Steam. Выполняется автоматическая адаптация патча под новую версию...");

                // Pull latest updates from GitHub if available (2 sec timeout)
                try
                {
                    GitHubUpdater.CheckAndPullUpdatesAsync(log, 2).GetAwaiter().GetResult();
                }
                catch { }

                // Apply patch to the updated game
                if (ApplyPatch(assemblyPath, log))
                {
                    log?.Invoke("[V] Патч успешно применён к обновлённой версии игры!");
                }
                else
                {
                    log?.Invoke("[-] Предупреждение: авто-патч завершился с ошибкой, запуск оригинальной игры...");
                }
            }
            else
            {
                log?.Invoke("[V] Сборка Bongo Cat актуальна и пропатчена.");
            }
        }

        log?.Invoke($"[>] Запуск игры: {gameExePath}");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = gameExePath,
                Arguments = extraArgs != null && extraArgs.Length > 0 ? string.Join(" ", extraArgs) : "",
                WorkingDirectory = Path.GetDirectoryName(gameExePath)
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[-] Ошибка запуска игры: {ex.Message}");
            return false;
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
                log("[*] Сборка вернулась к оригинальной. Проверка обновлений и повторный патч...");
                try
                {
                    GitHubUpdater.CheckAndPullUpdatesAsync(log, 3).GetAwaiter().GetResult();
                }
                catch { }

                if (ApplyPatch(assemblyPath, log))
                {
                    lastPatchTime = DateTime.Now;
                    log("[V] Авто-патч под новую версию игры успешно завершён!");
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
