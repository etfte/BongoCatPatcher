using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BongoCatPatcher;

class Program
{
    static int Main(string[] args)
    {
        string managedDir = @"D:\SteamLibrary\steamapps\common\BongoCat\BongoCat_Data\Managed";
        string targetDll = Path.Combine(managedDir, "Assembly-CSharp.dll");
        string backupDll = Path.Combine(managedDir, "Assembly-CSharp.dll.bak");

        Console.WriteLine($"[+] Target assembly: {targetDll}");

        if (!File.Exists(targetDll))
        {
            Console.Error.WriteLine($"[-] Target file not found: {targetDll}");
            return 1;
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);

        var readerParams = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadWrite = true
        };

        using var assembly = AssemblyDefinition.ReadAssembly(targetDll, readerParams);
        var module = assembly.MainModule;

        // 1. Patch SteamItemRuntime.get_IsOwned -> true
        var steamItemRuntime = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.SteamItemRuntime");
        if (steamItemRuntime == null)
        {
            Console.Error.WriteLine("[-] Could not find type BongoCat.SteamItemRuntime");
            return 1;
        }

        var isOwnedGetter = steamItemRuntime.Methods.FirstOrDefault(m => m.Name == "get_IsOwned");
        if (isOwnedGetter == null)
        {
            Console.Error.WriteLine("[-] Could not find method get_IsOwned");
            return 1;
        }

        PatchReturnBool(isOwnedGetter, true);
        Console.WriteLine("[+] Patched BongoCat.SteamItemRuntime.get_IsOwned -> true");

        // 2. Patch SteamItemRuntime.get_IsHiddenInInventory -> false
        var isHiddenGetter = steamItemRuntime.Methods.FirstOrDefault(m => m.Name == "get_IsHiddenInInventory");
        if (isHiddenGetter == null)
        {
            Console.Error.WriteLine("[-] Could not find method get_IsHiddenInInventory");
            return 1;
        }

        PatchReturnBool(isHiddenGetter, false);
        Console.WriteLine("[+] Patched BongoCat.SteamItemRuntime.get_IsHiddenInInventory -> false");

        // 3. Patch ItemCollection.IsRevealed -> true
        var itemCollection = module.Types.FirstOrDefault(t => t.FullName == "BongoCat.ItemCollection");
        if (itemCollection == null)
        {
            Console.Error.WriteLine("[-] Could not find type BongoCat.ItemCollection");
            return 1;
        }

        var isRevealed = itemCollection.Methods.FirstOrDefault(m => m.Name == "IsRevealed");
        if (isRevealed == null)
        {
            Console.Error.WriteLine("[-] Could not find method IsRevealed in ItemCollection");
            return 1;
        }

        PatchReturnBool(isRevealed, true);
        Console.WriteLine("[+] Patched BongoCat.ItemCollection.IsRevealed -> true");

        // Write modified assembly
        assembly.Write();
        Console.WriteLine("[+] Successfully saved patched assembly!");

        return 0;
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
}
