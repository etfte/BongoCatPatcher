using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BongoCatPatcher;

public static class GitHubUpdater
{
    public const string CurrentVersion = "v3.1";
    public const string GitHubRepo = "etfte/BongoCatPatcher";
    private static readonly string ReleasesApiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

    /// <summary>
    /// Checks GitHub for newer patcher release and downloads updates.
    /// Runs with a short timeout to never block game launch if offline.
    /// </summary>
    public static async Task<bool> CheckAndPullUpdatesAsync(Action<string>? log = null, int timeoutSeconds = 3)
    {
        try
        {
            log?.Invoke($"[*] Проверка обновлений патча на GitHub ({GitHubRepo})...");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BongoCatPatcher-Updater", "3.1"));

            var response = await client.GetAsync(ReleasesApiUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                log?.Invoke($"[i] Проверка обновлений завершена (GitHub ответил: {response.StatusCode}).");
                return false;
            }

            string json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp))
            {
                return false;
            }

            string latestTag = tagProp.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(latestTag)) return false;

            if (IsNewerVersion(latestTag, CurrentVersion))
            {
                log?.Invoke($"[!] Найдена новая версия патчера: {latestTag} (текущая: {CurrentVersion})");

                // Check for release asset
                if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        string downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";

                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            log?.Invoke($"[+] Загрузка обновления: {name}...");
                            string tempFile = Path.Combine(Path.GetTempPath(), name);
                            var bytes = await client.GetByteArrayAsync(downloadUrl, cts.Token);
                            await File.WriteAllBytesAsync(tempFile, bytes, cts.Token);
                            log?.Invoke($"[V] Обновление {name} успешно скачано!");
                            return true;
                        }
                    }
                }

                log?.Invoke($"[i] Доступно обновление {latestTag}. Посетите: https://github.com/{GitHubRepo}/releases");
                return true;
            }
            else
            {
                log?.Invoke($"[V] Патчер актуален ({CurrentVersion}).");
                return false;
            }
        }
        catch (TaskCanceledException)
        {
            log?.Invoke("[i] Сервер GitHub не ответил вовремя, используется локальный алгоритм авто-патча.");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[i] Пропуск проверки онлайн-обновлений: {ex.Message}");
            return false;
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        try
        {
            var v1 = ParseVersion(latest);
            var v2 = ParseVersion(current);
            return v1 > v2;
        }
        catch
        {
            return false;
        }
    }

    private static Version ParseVersion(string tag)
    {
        string digits = Regex.Replace(tag, @"[^\d.]", "");
        if (digits.Split('.').Length == 1) digits += ".0";
        return Version.Parse(digits);
    }
}
