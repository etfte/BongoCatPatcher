using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace BongoCat;

public class ModMenu : MonoBehaviour
{
    private static bool _initialized;

    // Mod options
    public static bool UnlockAllSkins = true;
    public static bool KeepEquipped = true;
    public static bool UnlockPawPass = true;
    public static bool InfiniteCoins = true;
    public static bool UnlockPromoItems = true;
    public static bool CustomSounds = false;
    public static int TargetFps = 0;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        LoadSettings();
        ApplyFpsLimit();

        var go = new GameObject("[BongoCatModManager]");
        DontDestroyOnLoad(go);
        go.AddComponent<ModMenu>();
    }

    private void Awake()
    {
        LoadSettings();
        ApplyFpsLimit();
    }

    public static void InjectIntoMenuTabs(MenuTabs menuTabs)
    {
        if (menuTabs == null) return;
        menuTabs.StartCoroutine(InjectDelayed(menuTabs));
    }

    private static IEnumerator InjectDelayed(MenuTabs menuTabs)
    {
        yield return null; // Wait 1 frame so Tabs finishes Awake

        try
        {
            Tabs? tabsComp = menuTabs.GetComponent<Tabs>();
            if (tabsComp == null) yield break;

            FieldInfo? tabsField = typeof(Tabs).GetField("tabs", BindingFlags.NonPublic | BindingFlags.Instance);
            if (tabsField == null) yield break;

            Array? oldTabs = tabsField.GetValue(tabsComp) as Array;
            if (oldTabs == null || oldTabs.Length == 0) yield break;

            // Check if already injected
            Type tabType = typeof(Tabs).GetNestedType("Tab", BindingFlags.NonPublic)!;
            FieldInfo btnField = tabType.GetField("button")!;
            FieldInfo tabContentField = tabType.GetField("tab")!;
            FieldInfo canvasGroupField = tabType.GetField("canvasGroup")!;

            for (int i = 0; i < oldTabs.Length; i++)
            {
                object? t = oldTabs.GetValue(i);
                if (t != null)
                {
                    Button? b = btnField.GetValue(t) as Button;
                    if (b != null && b.gameObject.name == "ModMenuTabButton")
                    {
                        yield break; // Already injected
                    }
                }
            }

            // Reference settings tab (index 4 or second to last)
            int templateIdx = Mathf.Clamp(4, 0, oldTabs.Length - 1);
            object settingsTab = oldTabs.GetValue(templateIdx)!;
            Button settingsButton = (Button)btnField.GetValue(settingsTab);
            GameObject settingsPanel = (GameObject)tabContentField.GetValue(settingsTab);

            // 1. Clone Tab Button in Menu Tab Bar
            GameObject newBtnGo = GameObject.Instantiate(settingsButton.gameObject, settingsButton.transform.parent);
            newBtnGo.name = "ModMenuTabButton";
            Button newButton = newBtnGo.GetComponent<Button>();
            newButton.onClick = new Button.ButtonClickedEvent();

            // Style the tab button with distinct pastel purple accent
            Image[] btnImages = newBtnGo.GetComponentsInChildren<Image>(true);
            foreach (var img in btnImages)
            {
                img.color = new Color(0.72f, 0.45f, 1f, 1f);
            }

            // Disable or update tooltip
            MonoBehaviour[] hoverBehaviors = newBtnGo.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var mb in hoverBehaviors)
            {
                if (mb.GetType().Name.Contains("Tooltip"))
                {
                    mb.enabled = false;
                }
            }

            // 2. Clone Tab Content Panel
            GameObject newPanelGo = GameObject.Instantiate(settingsPanel, settingsPanel.transform.parent);
            newPanelGo.name = "ModMenuTabPanel";
            CanvasGroup newCanvasGroup = newPanelGo.GetComponent<CanvasGroup>();

            // Setup content inside new panel
            SetupPanelContent(newPanelGo);

            // 3. Create new Tab entry
            object newTab = Activator.CreateInstance(tabType)!;
            btnField.SetValue(newTab, newButton);
            tabContentField.SetValue(newTab, newPanelGo);
            canvasGroupField.SetValue(newTab, newCanvasGroup);

            // 4. Extend tabs array
            int newTabIndex = oldTabs.Length;
            Array newTabsArray = Array.CreateInstance(tabType, newTabIndex + 1);
            Array.Copy(oldTabs, newTabsArray, newTabIndex);
            newTabsArray.SetValue(newTab, newTabIndex);
            tabsField.SetValue(tabsComp, newTabsArray);

            // 5. Connect click event to Tabs.OpenTab
            newButton.onClick.AddListener(() =>
            {
                tabsComp.OpenTab(newTabIndex);
            });

            // Initially deactivate
            newPanelGo.SetActive(false);
            newButton.interactable = true;
        }
        catch (Exception ex)
        {
            global::UnityEngine.Debug.LogError("[BongoCatMod] Error injecting mod tab: " + ex);
        }
    }

    private static void SetupPanelContent(GameObject panel)
    {
        try
        {
            ScrollRect scrollRect = panel.GetComponentInChildren<ScrollRect>(true);
            Transform content = scrollRect != null ? scrollRect.content : panel.transform;

            // Find an existing PlayerPrefsToggle to use as row template
            PlayerPrefsToggle[] existingToggles = content.GetComponentsInChildren<PlayerPrefsToggle>(true);
            GameObject? rowTemplate = existingToggles.Length > 0 ? existingToggles[0].gameObject : null;

            if (rowTemplate != null)
            {
                // Deactivate original children
                for (int i = 0; i < content.childCount; i++)
                {
                    content.GetChild(i).gameObject.SetActive(false);
                }

                // Create custom mod toggles
                CreateToggleRow(rowTemplate, content, "Разблокировать все скины и шапки", "BMOD_SKINS", UnlockAllSkins, v => UnlockAllSkins = v);
                CreateToggleRow(rowTemplate, content, "Сохранять экипировку при перезапуске", "BMOD_PERSIST", KeepEquipped, v => KeepEquipped = v);
                CreateToggleRow(rowTemplate, content, "Разблокировать боевой пропуск (PawPass)", "BMOD_PAWPASS", UnlockPawPass, v => UnlockPawPass = v);
                CreateToggleRow(rowTemplate, content, "999 999 монет в копилке", "BMOD_COINS", InfiniteCoins, v => InfiniteCoins = v);
                CreateToggleRow(rowTemplate, content, "Разблокировать скрытые промо-предметы", "BMOD_PROMO", UnlockPromoItems, v => UnlockPromoItems = v);
                CreateToggleRow(rowTemplate, content, "Кастомные звуки нажатий (Mods/Sounds)", "BMOD_SOUNDS", CustomSounds, v => CustomSounds = v);

                // Create folder buttons
                Button? templateBtn = content.GetComponentsInChildren<Button>(true).Length > 0
                    ? content.GetComponentsInChildren<Button>(true)[0] : null;

                if (templateBtn != null)
                {
                    CreateActionButton(templateBtn.gameObject, content, "🎨 Открыть папку скинов (Mods/Skins)", () =>
                    {
                        OpenFolder(Path.Combine(Application.dataPath, "..", "Mods", "Skins"));
                    });

                    CreateActionButton(templateBtn.gameObject, content, "🎵 Открыть папку звуков (Mods/Sounds)", () =>
                    {
                        OpenFolder(Path.Combine(Application.dataPath, "..", "Mods", "Sounds"));
                    });
                }
            }
        }
        catch (Exception ex)
        {
            global::UnityEngine.Debug.LogError("[BongoCatMod] Error setting up panel content: " + ex);
        }
    }

    private static void CreateToggleRow(GameObject template, Transform parent, string label, string prefKey, bool initialVal, Action<bool> onChanged)
    {
        GameObject row = GameObject.Instantiate(template, parent);
        row.name = "ModToggle_" + prefKey;
        row.SetActive(true);

        // Remove localization component to prevent text overwrite
        LocalizeStringEvent[] localizers = row.GetComponentsInChildren<LocalizeStringEvent>(true);
        foreach (var loc in localizers)
        {
            loc.enabled = false;
        }

        // Set row text
        TMP_Text[] tmps = row.GetComponentsInChildren<TMP_Text>(true);
        foreach (var tmp in tmps)
        {
            tmp.text = label;
        }

        Text[] texts = row.GetComponentsInChildren<Text>(true);
        foreach (var txt in texts)
        {
            txt.text = label;
        }

        // Configure PlayerPrefsToggle
        PlayerPrefsToggle toggle = row.GetComponent<PlayerPrefsToggle>() ?? row.GetComponentInChildren<PlayerPrefsToggle>();
        if (toggle != null)
        {
            FieldInfo? keyField = typeof(PlayerPrefsToggle).GetField("_playerPrefsKey", BindingFlags.NonPublic | BindingFlags.Instance);
            if (keyField != null)
            {
                keyField.SetValue(toggle, prefKey);
            }

            toggle.OnToggleUpdated = new UnityEvent<bool>();
            toggle.OnToggleUpdated.AddListener(val =>
            {
                PlayerPrefs.SetInt(prefKey, val ? 1 : 0);
                PlayerPrefs.Save();
                onChanged(val);
            });

            toggle.Value = PlayerPrefs.GetInt(prefKey, initialVal ? 1 : 0) == 1;
        }
    }

    private static void CreateActionButton(GameObject template, Transform parent, string label, Action onClick)
    {
        GameObject btnGo = GameObject.Instantiate(template, parent);
        btnGo.name = "ModButton_" + label.GetHashCode();
        btnGo.SetActive(true);

        LocalizeStringEvent[] localizers = btnGo.GetComponentsInChildren<LocalizeStringEvent>(true);
        foreach (var loc in localizers)
        {
            loc.enabled = false;
        }

        TMP_Text[] tmps = btnGo.GetComponentsInChildren<TMP_Text>(true);
        foreach (var tmp in tmps)
        {
            tmp.text = label;
        }

        Text[] texts = btnGo.GetComponentsInChildren<Text>(true);
        foreach (var txt in texts)
        {
            txt.text = label;
        }

        Button btn = btnGo.GetComponent<Button>() ?? btnGo.GetComponentInChildren<Button>();
        if (btn != null)
        {
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(new UnityAction(onClick));
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
