using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHostY;

/// <summary>
/// TOH_Zero のホーム画面(タイトル画面)
/// 本体のボタン(プレイ・持ち物・ショップ・ニュース・アカウント・設定・クレジット・終了)は
/// そのまま使い、見た目と位置だけを変える。位置は 16:9 (横10.67×縦6) を基準にした座標で指定する。
/// </summary>
[HarmonyPatch(typeof(MainMenuManager))]
public static class ZeroHomeUI
{
    // ==== レイアウト調整用 (16:9 基準の座標。画面中央が 0,0 / 上下は ±3) ====
    private const float RefHalfWidth = 5.333f;
    private const float ButtonPpu = 200f;
    private static readonly Vector3 LogoPos = new(-0.2f, 1.75f, 0f);
    private const float LogoWidth = 4.7f;
    private const string YoutubeUrl = "https://youtube.com/channel/UCwpCNW40HFXXfliw_rFZ9SQ";
    private const string GitHubUrl = "https://github.com/botsu221211/TownOfHost_Zero";

    private enum Kind { Big, Left, Right, Small, Mini, Friend }
    private static readonly Dictionary<Kind, Vector2> Sizes = new()
    {
        [Kind.Big] = new(3.04f, 0.76f),
        [Kind.Left] = new(2.48f, 0.51f),
        [Kind.Right] = new(2.13f, 0.44f),
        [Kind.Small] = new(1.33f, 0.29f),
        [Kind.Mini] = new(1.23f, 0.29f),
        [Kind.Friend] = new(2.03f, 0.59f),
    };

    private static GameObject homeRoot;
    private static GameObject backButton;
    private static readonly List<SpriteRenderer> panelRenderers = new();

    [HarmonyPatch(nameof(MainMenuManager.Start)), HarmonyPostfix, HarmonyPriority(Priority.Last)]
    public static void StartPostfix(MainMenuManager __instance)
    {
        try
        {
            Build(__instance);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "ZeroHomeUI");
        }
    }

    private static void Build(MainMenuManager menu)
    {
        panelRenderers.Clear();
        var mainUI = menu.transform.Find("MainUI");
        var scaler = mainUI?.Find("AspectScaler");
        if (scaler == null) return;

        // 全体の縮尺(16:9 より横が狭い画面では全体を縮める)
        var cam = Camera.main;
        float halfWidth = cam != null ? cam.orthographicSize * cam.aspect : RefHalfWidth;
        float k = Mathf.Min(1f, halfWidth / RefHalfWidth);

        homeRoot = new GameObject("ZeroHome");
        homeRoot.layer = menu.gameObject.layer;
        homeRoot.transform.SetParent(mainUI, false);
        homeRoot.transform.localPosition = new Vector3(0f, 0f, -4f);
        homeRoot.transform.localScale = new Vector3(k, k, 1f);

        /******** 背景 ********/
        HideRenderer(mainUI.Find("Tint"));
        HideRenderer(scaler.Find("BackgroundTexture"));
        var bg = NewSprite("ZeroHomeBackground", mainUI, "HomeBackground.jpg", 180f, new Vector3(0f, 0f, 20f));
        bg.sortingOrder = -100; // 背景は一番奥
        // 画面いっぱいに広げる(はみ出す分は切れる)
        float bgScale = Mathf.Max(1f, halfWidth / RefHalfWidth);
        bg.transform.localScale = new Vector3(bgScale, bgScale, 1f);

        /******** 本体の飾りを隠す ********/
        var leftPanel = scaler.Find("LeftPanel");
        HideRenderer(leftPanel);
        HideRenderer(leftPanel?.Find("Sizer/LOGO-AU"));
        HideRenderer(leftPanel?.Find("Main Buttons/Divider"));
        HideRenderer(leftPanel?.Find("Main Buttons/BottomButtonBounds"));
        // 右の窓枠はサブメニュー(プレイ・アカウント等)を開いた時だけ出す
        var rightPanel = scaler.Find("RightPanel");
        foreach (var name in new[] { "", "WindowShine", "MaskedBlackScreen" })
        {
            var t = name == "" ? rightPanel : rightPanel?.Find(name);
            var sr = t?.GetComponent<SpriteRenderer>();
            if (sr != null) panelRenderers.Add(sr);
        }

        // これまでのロゴ(右の窓の中)と下のボタンは使わない
        if (CredentialsPatch.TohLogo != null)
        {
            CredentialsPatch.TohLogo.enabled = false;
            foreach (var n in new[] { "DiscordButton", "WikiwikiButton", "GitHubButton" })
                CredentialsPatch.TohLogo.transform.Find(n)?.gameObject.SetActive(false);
        }

        /******** ロゴ ********/
        var logo = NewSprite("ZeroHomeLogo", homeRoot.transform, "TownOfHostZero-Logo.png", 970f / LogoWidth, LogoPos);

        /******** 本体のボタン ********/
        var buttons = leftPanel?.Find("Main Buttons");
        // 追加ボタンの複製元(ニュースボタンを動かす前に複製しておく)
        PassiveButton template = null;
        var newsButton = buttons?.Find("NewsButton")?.GetComponent<PassiveButton>();
        if (newsButton != null)
        {
            template = Object.Instantiate(newsButton, menu.transform);
            template.name = "ZeroHome_Template";
            template.gameObject.SetActive(false);
        }
        Restyle(buttons?.Find("PlayButton"), Kind.Big, "Play", new(0.11f, -0.10f));
        Restyle(buttons?.Find("Inventory Button"), Kind.Big, "Inventory", new(0.11f, -1.19f));
        Restyle(buttons?.Find("ShopButton"), Kind.Big, "Shop", new(0.11f, -2.24f));
        Restyle(buttons?.Find("NewsButton"), Kind.Right, "News", new(3.25f, -0.87f));
        Restyle(buttons?.Find("AcountButton"), Kind.Right, "Account", new(3.25f, -1.46f));
        Restyle(buttons?.Find("SettingsButton"), Kind.Right, "Settings", new(3.25f, -2.05f));
        Restyle(buttons?.Find("BottomButtonBounds/CreditsButton"), Kind.Mini, "Credits", new(2.67f, -2.57f));
        Restyle(buttons?.Find("BottomButtonBounds/ExitGameButton"), Kind.Mini, "Quit", new(4.16f, -2.57f));

        /******** 追加のボタン ********/
        if (template != null)
        {
            AddButton(template, Kind.Left, "Stats", new(-3.08f, -0.98f), OpenStats);
            AddButton(template, Kind.Small, "Youtube", new(-3.33f, -1.62f), () => Application.OpenURL(YoutubeUrl));
            AddButton(template, Kind.Small, "Discord", new(-3.33f, -2.12f), () => Application.OpenURL(Main.DiscordInviteUrl));
            AddButton(template, Kind.Small, "GitHub", new(-3.33f, -2.62f), () => Application.OpenURL(GitHubUrl));

            // サブメニューから戻るボタン(サブメニューを開いている時だけ出す)
            var back = AddButton(template, Kind.Mini, "Back", Vector2.zero, () => menu.ResetScreen(), parent: mainUI);
            back.transform.localPosition = new Vector3(-halfWidth + 0.9f, 2.0f, -50f);
            backButton = back.gameObject;
        }

        // 文字(バージョン表記・ランダムな一言)は後から作られるので少し待って配置
        _ = new LateTask(() => PlaceTexts(halfWidth), 0.3f, "ZeroHomeTexts");

        SetHomeMode(true);
    }

    private static void PlaceTexts(float halfWidth)
    {
        try
        {
            var credentials = GameObject.Find("TOHCredentialsText")?.GetComponent<TextMeshPro>();
            if (credentials != null)
            {
                credentials.alignment = TextAlignmentOptions.Center;
                credentials.transform.position = new Vector3(0.1f, -2.85f, -5f);
            }
            var special = GameObject.Find("SpecialEventText")?.transform;
            if (special != null && homeRoot != null)
            {
                special.SetParent(homeRoot.transform, true);
                special.localPosition = new Vector3(-3.1f, 1.35f, -1f);
                special.localScale = Vector3.one * 0.8f;
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "ZeroHomeUI.PlaceTexts");
        }
    }

    /******** サブメニューの開け閉め ********/
    [HarmonyPatch(nameof(MainMenuManager.OpenGameModeMenu))]
    [HarmonyPatch(nameof(MainMenuManager.OpenOnlineMenu))]
    [HarmonyPatch(nameof(MainMenuManager.OpenAccountMenu))]
    [HarmonyPatch(nameof(MainMenuManager.OpenCredits))]
    [HarmonyPostfix]
    public static void OpenSubMenuPostfix() => SetHomeMode(false);

    [HarmonyPatch(nameof(MainMenuManager.ResetScreen)), HarmonyPostfix]
    public static void ResetScreenPostfix() => SetHomeMode(true);

    private static bool inSubMenu;
    private static bool popupOpen;
    private static int checkFrame;

    [HarmonyPatch(nameof(MainMenuManager.LateUpdate)), HarmonyPostfix]
    public static void LateUpdatePostfix(MainMenuManager __instance)
    {
        if (homeRoot == null || ++checkFrame % 6 != 0) return;
        bool open = false;
        try
        {
            open = (__instance.announcementPopUp != null && __instance.announcementPopUp.gameObject.activeInHierarchy)
                || IsOpen(Object.FindObjectOfType<OptionsMenuBehaviour>())
                || Object.FindObjectOfType<StatsPopup>() != null;
        }
        catch { }
        if (open == popupOpen) return;
        popupOpen = open;
        homeRoot.SetActive(!inSubMenu && !popupOpen);
    }
    private static bool IsOpen(OptionsMenuBehaviour options) => options != null && options.gameObject.activeInHierarchy && options.IsOpen;

    private static void SetHomeMode(bool home)
    {
        inSubMenu = !home;
        if (homeRoot != null) homeRoot.SetActive(home && !popupOpen);
        if (backButton != null) backButton.SetActive(!home);
        foreach (var sr in panelRenderers)
            if (sr != null) sr.enabled = !home;
    }

    /******** ボタンの見た目 ********/
    private static void Restyle(Transform button, Kind kind, string id, Vector2 pos)
    {
        var passive = button?.GetComponent<PassiveButton>();
        if (passive == null) return;
        button.SetParent(homeRoot.transform, false);
        Style(passive, kind, id, pos);
    }

    private static PassiveButton AddButton(PassiveButton template, Kind kind, string id, Vector2 pos, Action onClick, Transform parent = null)
    {
        var button = Object.Instantiate(template, parent ?? homeRoot.transform);
        button.name = $"ZeroHome_{id}";
        button.gameObject.SetActive(true);
        button.OnClick = new();
        if (onClick != null) button.OnClick.AddListener(onClick);
        Style(button, kind, id, pos);
        return button;
    }

    /// <summary>id: 画像名(Home_{id}.png / Home_{id}_Hover.png)。文字は画像に入っている</summary>
    private static void Style(PassiveButton button, Kind kind, string id, Vector2 pos)
    {
        var t = button.transform;
        t.localScale = Vector3.one;
        t.localPosition = new Vector3(pos.x, pos.y, -1f);

        // 位置を勝手に動かす部品と、本体の絵(枠・アイコン・光沢・バッジ)・文字を外す
        foreach (var aspect in button.GetComponentsInChildren<AspectPosition>(true)) Object.Destroy(aspect);
        foreach (var sr in button.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = false;
        foreach (var tmp in button.GetComponentsInChildren<TextMeshPro>(true)) tmp.gameObject.SetActive(false);

        var normal = NewSprite("ZeroFrame", t, $"Home_{id}.png", ButtonPpu, new Vector3(0f, 0f, 0.5f));
        var hover = NewSprite("ZeroFrameHover", t, $"Home_{id}_Hover.png", ButtonPpu, new Vector3(0f, 0f, 0.4f));
        hover.gameObject.SetActive(false);
        normal.sortingOrder = hover.sortingOrder = 1;

        var collider = button.GetComponent<BoxCollider2D>();
        if (collider != null)
        {
            collider.size = Sizes[kind];
            collider.offset = Vector2.zero;
        }

        // マウスを乗せた時は水色の枠と白い文字の画像に切り替え
        button.OnMouseOver = new();
        button.OnMouseOut = new();
        button.OnMouseOver.AddListener((Action)(() =>
        {
            if (hover != null) hover.gameObject.SetActive(true);
            if (normal != null) normal.gameObject.SetActive(false);
        }));
        button.OnMouseOut.AddListener((Action)(() =>
        {
            if (hover != null) hover.gameObject.SetActive(false);
            if (normal != null) normal.gameObject.SetActive(true);
        }));
    }

    /******** 部品 ********/
    private static SpriteRenderer NewSprite(string name, Transform parent, string file, float ppu, Vector3 localPos)
    {
        var obj = new GameObject(name);
        obj.layer = parent.gameObject.layer;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = ZeroSettingUI.LoadSprite(file, ppu);
        return sr;
    }
    private static void HideRenderer(Transform t)
    {
        var sr = t?.GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;
    }

    private static void OpenFriends()
    {
        try
        {
            DestroyableSingleton<FriendsListManager>.Instance?.OpenUi();
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "ZeroHomeUI.OpenFriends");
        }
    }
    private static void OpenStats()
    {
        try
        {
            var popups = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<StatsPopup>());
            var popup = popups.Length > 0 ? popups[0].TryCast<StatsPopup>() : null;
            if (popup != null) popup.gameObject.SetActive(true);
            else Logger.Info("StatsPopup が見つかりません", "ZeroHomeUI");
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "ZeroHomeUI.OpenStats");
        }
    }
}
