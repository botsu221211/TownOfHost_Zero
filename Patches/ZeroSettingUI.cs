using System;
using System.Collections.Generic;
using TMPro;
using TownOfHostY.Modules;
using TownOfHostY.Templates;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHostY;

/// <summary>
/// TOH_Zero 用のルーム設定画面の見た目(左メニュー・ヘッダー・プリセット画面)
/// 位置や大きさは下の「レイアウト調整用」の定数でまとめて調整できる
/// </summary>
public static class ZeroSettingUI
{
    // ==== レイアウト調整用 ====
    // 左メニューのボタン (GameSettingMenu 内の座標)
    public const float TabX = -3.94f;
    public const float TabTopY = 1.58f;
    public const float TabStepY = 0.44f;
    private static readonly Vector2 TabColliderSize = new(2.24f, 0.4f);
    private const float TabPpu = 250f; // 560x100px → 2.24x0.4
    // 左上のロゴ
    private static readonly Vector3 HeaderLogoPos = new(-3.94f, 2.12f, -3f);
    private const float HeaderLogoPpu = 270f;
    // パネルの色
    private static readonly Color PanelColor = new(0.07f, 0.1f, 0.2f, 1f);
    private static readonly Color LeftSideColor = new(0.03f, 0.05f, 0.12f, 1f);
    // プリセット画面 (GameSettingMenu 内の座標)
    private static readonly Vector3 PresetPanelPos = new(0.9f, 0f, -5f);
    private static readonly Color32 ButtonColor = new(26, 44, 82, 255);
    private static readonly Color32 ButtonHoverColor = new(52, 96, 160, 255);
    private static readonly Color32 DangerColor = new(92, 30, 44, 255);
    private static readonly Color32 DangerHoverColor = new(150, 48, 70, 255);

    // 左メニューの並び順
    public const int PresetTabIndex = 0;
    public const int GameSettingsTabIndex = 1;
    public static int GetTabDisplayIndex(TabGroup tab) => tab switch
    {
        TabGroup.ModMainSettings => 2,
        TabGroup.ImpostorRoles => 3,
        TabGroup.MadmateRoles => 4,
        TabGroup.CrewmateRoles => 5,
        TabGroup.NeutralRoles => 6,
        TabGroup.Addons => 7,
        TabGroup.UnitRoles => 8,
        _ => 9,
    };
    public static string GetTabName(TabGroup tab) => tab switch
    {
        TabGroup.ModMainSettings => "MODの設定",
        TabGroup.ImpostorRoles => "インポスター",
        TabGroup.MadmateRoles => "マッドメイト",
        TabGroup.CrewmateRoles => "クルーメイト",
        TabGroup.NeutralRoles => "ニュートラル",
        TabGroup.Addons => "属性",
        TabGroup.UnitRoles => "コンビネーション",
        _ => tab.ToString(),
    };

    private static readonly Dictionary<string, Sprite> spriteCache = new();
    public static Sprite LoadSprite(string file, float ppu)
    {
        var key = $"{file}@{ppu}";
        if (spriteCache.TryGetValue(key, out var cached) && cached != null) return cached;
        var sprite = Utils.LoadSprite($"TownOfHost_Zero.Resources.{file}", ppu);
        if (sprite != null) sprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
        spriteCache[key] = sprite;
        return sprite;
    }

    /******** 左メニュー ********/
    public static void StyleTabButton(PassiveButton button, string key, int displayIndex)
    {
        if (button == null) return;
        var label = button.GetComponentInChildren<TextMeshPro>();
        if (label != null)
        {
            label.DestroyTranslator();
            label.text = "";
        }

        var normal = LoadSprite($"Tab_Small_{key}.png", TabPpu);
        var active = LoadSprite($"Tab_Active_{key}.png", TabPpu);
        SetButtonSprite(button.inactiveSprites, normal);
        SetButtonSprite(button.activeSprites, active);
        SetButtonSprite(button.selectedSprites, active);

        button.transform.localPosition = new Vector3(TabX, TabTopY - TabStepY * displayIndex, 0f);
        button.transform.localScale = Vector3.one;

        var collider = button.GetComponent<BoxCollider2D>();
        if (collider != null)
        {
            collider.size = TabColliderSize;
            collider.offset = Vector2.zero;
        }
    }
    private static void SetButtonSprite(GameObject obj, Sprite sprite)
    {
        if (obj == null) return;
        var renderer = obj.GetComponent<SpriteRenderer>();
        if (renderer == null) return;
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Simple;
        renderer.color = Color.white;
        obj.transform.localScale = Vector3.one;
        obj.transform.localPosition = new Vector3(0f, 0f, obj.transform.localPosition.z);
    }

    /******** パネル・ヘッダー ********/
    public static void ApplyPanelStyle(GameSettingMenu menu)
    {
        var panel = menu.transform.Find("PanelSprite")?.GetComponent<SpriteRenderer>();
        if (panel != null) panel.color = PanelColor;
        var leftSide = menu.transform.Find("PanelSprite/LeftSideTint")?.GetComponent<SpriteRenderer>();
        if (leftSide != null) leftSide.color = LeftSideColor;

        // 元の「ゲーム設定」の見出しは隠してロゴに置き換える
        menu.transform.Find("GameSettingsLabel")?.gameObject.SetActive(false);

        var logo = new GameObject("ZeroHeaderLogo");
        logo.layer = menu.gameObject.layer;
        logo.transform.SetParent(menu.transform, false);
        logo.transform.localPosition = HeaderLogoPos;
        var renderer = logo.AddComponent<SpriteRenderer>();
        renderer.sprite = LoadSprite("SettingHeaderLogo.png", HeaderLogoPpu);
    }

    /******** プリセット画面 ********/
    private static GameObject presetPanel;
    private static TextMeshPro presetNameText;
    private static readonly List<SimpleButton> actionButtons = new();
    private static readonly string[] actionLabels = { "闇鍋", "属性入り闇鍋", "役職をリセット" };
    private static int pendingAction = -1;
    private static float pendingTime;
    private const float ConfirmSeconds = 4f;

    public static void CreatePresetPanel(GameSettingMenu menu)
    {
        actionButtons.Clear();
        pendingAction = -1;

        presetPanel = new GameObject("ZeroPresetPanel");
        presetPanel.layer = menu.gameObject.layer;
        presetPanel.transform.SetParent(menu.transform, false);
        presetPanel.transform.localPosition = PresetPanelPos;
        var parent = presetPanel.transform;

        var title = TMPTemplate.Create("PresetTitle", "プリセット", Color.white, 3.2f, TextAlignmentOptions.Center, true, parent);
        title.transform.localPosition = new Vector3(0f, 1.9f, 0f);
        title.fontStyle = FontStyles.Bold;

        var caption = TMPTemplate.Create("PresetCaption", "現在のプリセット", new Color(0.7f, 0.85f, 1f), 2.2f, TextAlignmentOptions.Center, true, parent);
        caption.transform.localPosition = new Vector3(0f, 1.2f, 0f);

        presetNameText = TMPTemplate.Create("PresetName", "", new Color(1f, 0.9f, 0.4f), 3f, TextAlignmentOptions.Center, true, parent);
        presetNameText.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        presetNameText.fontStyle = FontStyles.Bold;

        var prev = new SimpleButton(parent, "PresetPrev", new Vector3(-2f, 0.65f, 0f), ButtonColor, ButtonHoverColor, (Action)(() => ChangePreset(-1)), "◀");
        prev.Scale = new Vector2(0.6f, 0.5f);
        prev.FontSize = 2.5f;
        var next = new SimpleButton(parent, "PresetNext", new Vector3(2f, 0.65f, 0f), ButtonColor, ButtonHoverColor, (Action)(() => ChangePreset(1)), "▶");
        next.Scale = new Vector2(0.6f, 0.5f);
        next.FontSize = 2.5f;

        for (int i = 0; i < actionLabels.Length; i++)
        {
            int actionIndex = i;
            bool danger = i == 2;
            var button = new SimpleButton(
                parent, $"PresetAction{i}", new Vector3(0f, -0.35f - 0.7f * i, 0f),
                danger ? DangerColor : ButtonColor, danger ? DangerHoverColor : ButtonHoverColor,
                (Action)(() => OnActionClicked(actionIndex)), actionLabels[i]);
            button.Scale = new Vector2(3.6f, 0.55f);
            button.FontSize = 2.4f;
            actionButtons.Add(button);
        }

        var note = TMPTemplate.Create("PresetNote",
            "闇鍋：属性以外の全役職を 100% / 最大1 にする\n属性入り闇鍋：属性も含めて 100% / 最大1 にする\n役職をリセット：全役職・属性を 0% にする\n<color=#9fb8e0>※今選んでいるプリセットだけが変わります。押すと確認が出ます</color>",
            Color.white, 1.4f, TextAlignmentOptions.Center, true, parent);
        note.transform.localPosition = new Vector3(0f, -2.55f, 0f);

        UpdatePresetPanel();
        presetPanel.SetActive(false);
    }

    public static void SetPresetPanelActive(bool active)
    {
        if (presetPanel == null) return;
        presetPanel.SetActive(active);
        if (active) UpdatePresetPanel();
    }

    private static void UpdatePresetPanel()
    {
        if (presetNameText != null && OptionItem.FastOptions.TryGetValue(OptionItem.PresetId, out var preset))
            presetNameText.text = preset.GetString();
        pendingAction = -1;
        for (int i = 0; i < actionButtons.Count; i++)
            if (!SimpleButton.IsNullOrDestroyed(actionButtons[i])) actionButtons[i].Label.text = actionLabels[i];
    }

    private static void ChangePreset(int delta)
    {
        if (!OptionItem.FastOptions.TryGetValue(OptionItem.PresetId, out var preset)) return;
        preset.SetValue(preset.CurrentValue + delta);
        GameSettingMenuPatch.NotifyPresetChanged();
        GameOptionsMenuPatch.RefreshChildSettings();
        UpdatePresetPanel();
    }

    private static void OnActionClicked(int index)
    {
        // 誤操作防止: 4秒以内にもう一度押したら実行
        if (pendingAction == index && Time.time - pendingTime <= ConfirmSeconds)
        {
            switch (index)
            {
                case 0: SetAllRoles(includeRoles: true, includeAddons: false, on: true); break;
                case 1: SetAllRoles(includeRoles: true, includeAddons: true, on: true); break;
                case 2: SetAllRoles(includeRoles: true, includeAddons: true, on: false); break;
            }
            UpdatePresetPanel();
            actionButtons[index].Label.text = $"{actionLabels[index]}　<color=#7dffa8>完了！</color>";
            return;
        }

        UpdatePresetPanel();
        pendingAction = index;
        pendingTime = Time.time;
        actionButtons[index].Label.text = "<color=#ffd84d>もう一度押すと実行</color>";
    }

    /// <summary>役職(と属性)の出現率・最大数をまとめて設定する</summary>
    private static void SetAllRoles(bool includeRoles, bool includeAddons, bool on)
    {
        foreach (var (role, spawn) in Options.CustomRoleSpawnChances)
        {
            if (spawn == null || spawn.Tab == TabGroup.ModMainSettings) continue;
            bool isAddon = spawn.Tab == TabGroup.Addons;
            if (isAddon ? !includeAddons : !includeRoles) continue;
            // オンにする時は、今のゲームモードで表示されていない役職は触らない
            if (on && spawn.IsHiddenOn(Options.CurrentGameMode)) continue;

            SetRawValue(spawn, spawn.Rule.GetNearestIndex(on ? 100 : 0));
            if (Options.CustomRoleCounts.TryGetValue(role, out var count) && count is IntegerOptionItem countItem && !count.IsFixValue)
                SetRawValue(count, countItem.Rule.GetNearestIndex(on ? 1 : 0));
        }
        OptionSaver.Save();
        OptionItem.SyncAllOptions();
        GameOptionsMenuPatch.RefreshChildSettings();
    }
    // 1件ずつ保存・画面更新すると重いので、値だけ書き換えて最後にまとめて保存する
    private static void SetRawValue(OptionItem option, int index)
    {
        if (option.IsSingleValue)
        {
            option.SetValue(index, false, false);
            return;
        }
        option.AllValues[OptionItem.CurrentPreset] = index;
        option.Refresh();
    }

    public static void Clear()
    {
        presetPanel = null;
        presetNameText = null;
        actionButtons.Clear();
        pendingAction = -1;
    }
}
