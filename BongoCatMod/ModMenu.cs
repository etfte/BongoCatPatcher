using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace BongoCat;

public class ModMenu : MonoBehaviour
{
    private static ModMenu? _instance;
    private static bool _initialized;

    // Mod options
    public static bool UnlockAllSkins = true;
    public static bool KeepEquipped = true;
    public static bool UnlockPawPass = true;
    public static bool InfiniteCoins = true;
    public static bool UnlockPromoItems = true;
    public static bool CustomSounds = false;
    public static int TargetFps = 0; // 0 = Default, -1 = Unlimited

    // UI state
    private bool _isOpen = false;
    private Rect _windowRect = new Rect(30, 30, 490, 520);
    private int _tab = 0; // 0 = Skins, 1 = Sound & FPS, 2 = About

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        LoadSettings();

        var go = new GameObject("[BongoCatModMenu]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ModMenu>();
    }

    private void Awake()
    {
        LoadSettings();
        ApplyFpsLimit();
    }

    private void OnGUI()
    {
        // Hotkeys check in OnGUI
        if (Event.current.type == EventType.KeyDown)
        {
            if (Event.current.keyCode == KeyCode.F1 || Event.current.keyCode == KeyCode.Insert || Event.current.keyCode == KeyCode.F8)
            {
                _isOpen = !_isOpen;
                Event.current.Use();
            }
        }

        // Top-Right Quick Toggle Button matching Bongo Cat's pastel theme
        float btnW = 160f;
        float btnH = 30f;
        var pillRect = new Rect(Screen.width - btnW - 15f, 15f, btnW, btnH);

        Color pillColor = _isOpen ? new Color(0.85f, 0.35f, 0.45f) : new Color(0.62f, 0.45f, 0.95f);
        if (DrawButton(pillRect, _isOpen ? "✕ Закрыть меню" : "🐱 MOD MENU (F1)", pillColor))
        {
            _isOpen = !_isOpen;
        }

        if (_isOpen)
        {
            DrawWindow();
        }
    }

    private void DrawWindow()
    {
        var prevBg = GUI.backgroundColor;
        var prevColor = GUI.color;

        // Window background
        GUI.backgroundColor = new Color(0.12f, 0.11f, 0.18f, 0.97f);
        GUI.Box(_windowRect, "");

        // Header banner
        var headerRect = new Rect(_windowRect.x, _windowRect.y, _windowRect.width, 40f);
        GUI.backgroundColor = new Color(0.22f, 0.19f, 0.32f);
        GUI.Box(headerRect, "🐱 Bongo Cat Mod Manager v4.0");

        // Close button in header
        var closeHeaderRect = new Rect(_windowRect.x + _windowRect.width - 34f, _windowRect.y + 7f, 26f, 26f);
        if (DrawButton(closeHeaderRect, "✕", new Color(0.75f, 0.25f, 0.35f)))
        {
            _isOpen = false;
        }

        // Tabs
        float tabY = _windowRect.y + 48f;
        float tabW = (_windowRect.width - 32f) / 3f;

        if (DrawTab(new Rect(_windowRect.x + 16f, tabY, tabW, 30f), "Скины & Награды", _tab == 0)) _tab = 0;
        if (DrawTab(new Rect(_windowRect.x + 16f + tabW, tabY, tabW, 30f), "Звуки & FPS", _tab == 1)) _tab = 1;
        if (DrawTab(new Rect(_windowRect.x + 16f + tabW * 2f, tabY, tabW, 30f), "О моде", _tab == 2)) _tab = 2;

        float contentY = tabY + 40f;

        switch (_tab)
        {
            case 0:
                DrawCustomizationTab(contentY);
                break;
            case 1:
                DrawSoundAndFpsTab(contentY);
                break;
            case 2:
                DrawAboutTab(contentY);
                break;
        }

        // Footer buttons
        float footY = _windowRect.y + _windowRect.height - 45f;
        var modsBtnRect = new Rect(_windowRect.x + 16f, footY, 180f, 32f);
        if (DrawButton(modsBtnRect, "📁 Папка модов (Mods)", new Color(0.35f, 0.3f, 0.48f)))
        {
            OpenFolder(Path.Combine(Application.dataPath, "..", "Mods"));
        }

        var closeBtnRect = new Rect(_windowRect.x + _windowRect.width - 120f, footY, 104f, 32f);
        if (DrawButton(closeBtnRect, "✕ Закрыть", new Color(0.85f, 0.35f, 0.45f)))
        {
            _isOpen = false;
        }

        GUI.backgroundColor = prevBg;
        GUI.color = prevColor;
    }

    private void DrawCustomizationTab(float startY)
    {
        float x = _windowRect.x + 16f;
        float w = _windowRect.width - 32f;
        float y = startY;

        GUI.Box(new Rect(x, y, w, 24f), "УПРАВЛЕНИЕ РАЗБЛОКИРОВКАМИ");
        y += 30f;

        DrawToggle(new Rect(x, y, w, 30f), "Разблокировать все скины и шапки", ref UnlockAllSkins, "BMOD_SKINS");
        y += 34f;
        DrawToggle(new Rect(x, y, w, 30f), "Сохранять экипировку при перезапуске", ref KeepEquipped, "BMOD_PERSIST");
        y += 34f;
        DrawToggle(new Rect(x, y, w, 30f), "Разблокировать боевой пропуск (PawPass)", ref UnlockPawPass, "BMOD_PAWPASS");
        y += 34f;
        DrawToggle(new Rect(x, y, w, 30f), "999 999 монет в копилке (Coin Stash)", ref InfiniteCoins, "BMOD_COINS");
        y += 34f;
        DrawToggle(new Rect(x, y, w, 30f), "Разблокировать скрытые промо-предметы", ref UnlockPromoItems, "BMOD_PROMO");
        y += 42f;

        GUI.Box(new Rect(x, y, w, 24f), "КАСТОМНЫЕ СКИНЫ");
        y += 30f;

        if (DrawButton(new Rect(x, y, w, 32f), "🎨 Открыть папку скинов (Mods/Skins)", new Color(0.55f, 0.38f, 0.88f)))
        {
            OpenFolder(Path.Combine(Application.dataPath, "..", "Mods", "Skins"));
        }
    }

    private void DrawSoundAndFpsTab(float startY)
    {
        float x = _windowRect.x + 16f;
        float w = _windowRect.width - 32f;
        float y = startY;

        GUI.Box(new Rect(x, y, w, 24f), "КАСТОМНЫЕ ЗВУКИ ТАПОВ (SOUNDPACKS)");
        y += 30f;

        DrawToggle(new Rect(x, y, w, 30f), "Включить кастомные звуки из Mods/Sounds", ref CustomSounds, "BMOD_SOUNDS");
        y += 34f;

        if (DrawButton(new Rect(x, y, w, 32f), "🎵 Открыть папку звуков (Mods/Sounds)", new Color(0.45f, 0.35f, 0.75f)))
        {
            OpenFolder(Path.Combine(Application.dataPath, "..", "Mods", "Sounds"));
        }
        y += 44f;

        GUI.Box(new Rect(x, y, w, 24f), "ЛИМИТ ЧАСТОТЫ КАДРОВ (FPS)");
        y += 30f;

        float fpsBtnW = (w - 20f) / 6f;
        if (DrawFpsButton(new Rect(x, y, fpsBtnW, 30f), "Авто", 0)) SetFps(0);
        if (DrawFpsButton(new Rect(x + (fpsBtnW + 4f), y, fpsBtnW, 30f), "60", 60)) SetFps(60);
        if (DrawFpsButton(new Rect(x + (fpsBtnW + 4f) * 2f, y, fpsBtnW, 30f), "120", 120)) SetFps(120);
        if (DrawFpsButton(new Rect(x + (fpsBtnW + 4f) * 3f, y, fpsBtnW, 30f), "144", 144)) SetFps(144);
        if (DrawFpsButton(new Rect(x + (fpsBtnW + 4f) * 4f, y, fpsBtnW, 30f), "240", 240)) SetFps(240);
        if (DrawFpsButton(new Rect(x + (fpsBtnW + 4f) * 5f, y, fpsBtnW, 30f), "Макс", -1)) SetFps(-1);

        y += 38f;
        string currentFpsLabel = TargetFps switch
        {
            0 => "По умолчанию (вертикальная синхронизация)",
            -1 => "Без ограничений (Максимум)",
            _ => $"{TargetFps} FPS"
        };
        GUI.Label(new Rect(x, y, w, 24f), $"Текущий лимит: {currentFpsLabel}");
    }

    private void DrawAboutTab(float startY)
    {
        float x = _windowRect.x + 16f;
        float w = _windowRect.width - 32f;
        float y = startY;

        GUI.Box(new Rect(x, y, w, 24f), "ИНФОРМАЦИЯ О МОДЕ");
        y += 32f;

        GUI.Label(new Rect(x, y, w, 24f), "🐱 Bongo Cat In-Game Mod Manager v4.0");
        y += 24f;
        GUI.Label(new Rect(x, y, w, 24f), "• Меню вызывается клавишами: F1, Insert или F8.");
        y += 24f;
        GUI.Label(new Rect(x, y, w, 24f), "• Все параметры сохраняются автоматически в реестр.");
        y += 24f;
        GUI.Label(new Rect(x, y, w, 24f), "• Скины и шапки помещайте в папку: Mods/Skins/");
        y += 24f;
        GUI.Label(new Rect(x, y, w, 24f), "• Звуки кликов помещайте в папку: Mods/Sounds/");
        y += 32f;
        GUI.Label(new Rect(x, y, w, 24f), "Разработчик: etfte");
        y += 24f;
        GUI.Label(new Rect(x, y, w, 24f), "GitHub: https://github.com/etfte/BongoCatPatcher");
    }

    private bool DrawButton(Rect rect, string text, Color bg)
    {
        bool isHover = rect.Contains(Event.current.mousePosition);
        var old = GUI.backgroundColor;
        GUI.backgroundColor = isHover ? Color.Lerp(bg, Color.white, 0.25f) : bg;

        GUI.Box(rect, text);
        GUI.backgroundColor = old;

        if (Event.current.type == EventType.MouseDown && isHover && Event.current.button == 0)
        {
            Event.current.Use();
            return true;
        }
        return false;
    }

    private bool DrawTab(Rect rect, string title, bool isActive)
    {
        Color c = isActive ? new Color(0.66f, 0.44f, 0.98f) : new Color(0.24f, 0.21f, 0.34f);
        return DrawButton(rect, title, c);
    }

    private void DrawToggle(Rect rect, string title, ref bool value, string prefKey)
    {
        string label = (value ? "[✔] " : "[   ] ") + title;
        Color c = value ? new Color(0.18f, 0.75f, 0.45f) : new Color(0.38f, 0.36f, 0.48f);

        if (DrawButton(rect, label, c))
        {
            value = !value;
            PlayerPrefs.SetInt(prefKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private bool DrawFpsButton(Rect rect, string label, int fps)
    {
        bool isCurrent = TargetFps == fps;
        Color c = isCurrent ? new Color(0.18f, 0.75f, 0.45f) : new Color(0.25f, 0.22f, 0.35f);
        return DrawButton(rect, label, c);
    }

    private void SetFps(int fps)
    {
        TargetFps = fps;
        PlayerPrefs.SetInt("BMOD_FPS", TargetFps);
        PlayerPrefs.Save();
        ApplyFpsLimit();
    }

    private static void ApplyFpsLimit()
    {
        if (TargetFps > 0)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFps;
        }
        else if (TargetFps == -1)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
        else
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }
    }

    private static void LoadSettings()
    {
        UnlockAllSkins = PlayerPrefs.GetInt("BMOD_SKINS", 1) == 1;
        KeepEquipped = PlayerPrefs.GetInt("BMOD_PERSIST", 1) == 1;
        UnlockPawPass = PlayerPrefs.GetInt("BMOD_PAWPASS", 1) == 1;
        InfiniteCoins = PlayerPrefs.GetInt("BMOD_COINS", 1) == 1;
        UnlockPromoItems = PlayerPrefs.GetInt("BMOD_PROMO", 1) == 1;
        CustomSounds = PlayerPrefs.GetInt("BMOD_SOUNDS", 0) == 1;
        TargetFps = PlayerPrefs.GetInt("BMOD_FPS", 0);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }
        catch { }
    }
}
