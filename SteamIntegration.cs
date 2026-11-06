using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BongoCatPatcher;

public static class SteamIntegration
{
    public const string BongoCatAppId = "3419430";

    public static string GetRecommendedLaunchOption(string patcherExePath)
    {
        return $"\"{patcherExePath}\" --steam-launch %command%";
    }

    public static List<string> FindLocalConfigFiles()
    {
        var results = new List<string>();

        // 1. From Registry SteamPath
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steamPath)
            {
                steamPath = steamPath.Replace('/', '\\');
                string userdataDir = Path.Combine(steamPath, "userdata");
                if (Directory.Exists(userdataDir))
                {
                    foreach (var userDir in Directory.GetDirectories(userdataDir))
                    {
                        string cfg = Path.Combine(userDir, "config", "localconfig.vdf");
                        if (File.Exists(cfg))
                        {
                            results.Add(cfg);
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Standard path fallback
        string defaultUserdata = @"C:\Program Files (x86)\Steam\userdata";
        if (Directory.Exists(defaultUserdata))
        {
            foreach (var userDir in Directory.GetDirectories(defaultUserdata))
            {
                string cfg = Path.Combine(userDir, "config", "localconfig.vdf");
                if (File.Exists(cfg) && !results.Contains(cfg, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(cfg);
                }
            }
        }

        return results;
    }

    public static bool IsSteamHookInstalled(string? patcherExePath = null)
    {
        var configs = FindLocalConfigFiles();
        foreach (var cfg in configs)
        {
            try
            {
                string text = File.ReadAllText(cfg);
                if (text.Contains(BongoCatAppId) && text.Contains("--steam-launch"))
                {
                    if (string.IsNullOrEmpty(patcherExePath) || text.Contains(Path.GetFileName(patcherExePath)))
                        return true;
                }
            }
            catch { }
        }
        return false;
    }

    public static bool InstallSteamLaunchHook(string patcherExePath, Action<string>? log = null)
    {
        var configs = FindLocalConfigFiles();
        if (configs.Count == 0)
        {
            log?.Invoke("[-] Файлы конфигурации Steam (localconfig.vdf) не найдены!");
            return false;
        }

        string launchOptionValue = GetRecommendedLaunchOption(patcherExePath);
        bool anySuccess = false;

        foreach (var cfg in configs)
        {
            try
            {
                string text = File.ReadAllText(cfg);
                string backup = cfg + ".bak";
                if (!File.Exists(backup))
                {
                    File.Copy(cfg, backup, overwrite: false);
                }

                string escapedVal = launchOptionValue.Replace("\\", "\\\\").Replace("\"", "\\\"");

                // Check if 3419430 is already in apps section
                string pattern = $@"""{BongoCatAppId}""\s*\{{[^}}]*""LaunchOptions""\s*""([^""]*)""";
                if (Regex.IsMatch(text, pattern))
                {
                    text = Regex.Replace(text, pattern, $"\"{BongoCatAppId}\"\n\t\t\t{{\n\t\t\t\t\"LaunchOptions\"\t\t\"{escapedVal}\"");
                }
                else if (text.Contains($"\"{BongoCatAppId}\""))
                {
                    // Add LaunchOptions under existing app entry
                    string appBlockPattern = $@"""{BongoCatAppId}""\s*\{{";
                    text = Regex.Replace(text, appBlockPattern, $"\"{BongoCatAppId}\"\n\t\t\t{{\n\t\t\t\t\"LaunchOptions\"\t\t\"{escapedVal}\"");
                }
                else
                {
                    // Insert into "apps" block
                    int appsIdx = text.IndexOf("\"apps\"");
                    if (appsIdx >= 0)
                    {
                        int openBrace = text.IndexOf('{', appsIdx);
                        if (openBrace >= 0)
                        {
                            string entry = $"\n\t\t\t\"{BongoCatAppId}\"\n\t\t\t{{\n\t\t\t\t\"LaunchOptions\"\t\t\"{escapedVal}\"\n\t\t\t}}";
                            text = text.Insert(openBrace + 1, entry);
                        }
                    }
                }

                File.WriteAllText(cfg, text);
                log?.Invoke($"[+] Параметр запуска Steam успешно установлен в: {cfg}");
                anySuccess = true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[-] Ошибка при настройке {cfg}: {ex.Message}");
            }
        }

        if (anySuccess)
        {
            log?.Invoke("[V] Steam Hook активен: при запуске Bongo Cat из Steam игра будет автоматически проверяться и обновляться!");
        }

        return anySuccess;
    }

    public static bool RemoveSteamLaunchHook(Action<string>? log = null)
    {
        var configs = FindLocalConfigFiles();
        bool anySuccess = false;

        foreach (var cfg in configs)
        {
            try
            {
                string text = File.ReadAllText(cfg);
                string pattern = $@"\t*""LaunchOptions""\s*""[^""]*--steam-launch[^""]*""\r?\n?";
                if (Regex.IsMatch(text, pattern))
                {
                    text = Regex.Replace(text, pattern, "");
                    File.WriteAllText(cfg, text);
                    log?.Invoke($"[+] Параметр авто-запуска удалён из: {cfg}");
                    anySuccess = true;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[-] Ошибка при очистке {cfg}: {ex.Message}");
            }
        }

        return anySuccess;
    }
}
