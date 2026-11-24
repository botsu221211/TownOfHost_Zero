using System;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostY;

/// <summary>
/// 「プレイ」を押した後の「ローカル」「オンライン」ボタンの絵を TOH_Zero の絵に差し替える
/// 絵は元の絵と同じ場所・同じ大きさになるように合わせる
/// </summary>
[HarmonyPatch]
public static class PlayMenuImages
{
    private const string LocalImage = "PlayLocal.png";
    private const string OnlineImage = "PlayOnline.png";
    private const string CreateGameImage = "PlayCreateGame.png";
    // ==== 見た目の調整用 ====
    /// <summary>絵の横幅(元の絵の横幅に対する割合)</summary>
    private const float ImageWidthRatio = 0.79f;
    /// <summary>ローカルとオンラインのボタンを左右にずらす量(それぞれ)</summary>
    private const float ButtonSpread = 0.18f;
    /// <summary>外枠(画像のピクセル単位)</summary>
    private const int FrameWidth = 22;
    private const int CornerRadius = 80;
    private static readonly Color32 FrameColor = new(150, 155, 162, 255);
    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> framedCache = new();
    private static bool loggedHierarchy;

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix]
    public static void StartPostfix(MainMenuManager __instance)
    {
        Apply(__instance);
        ApplyCreateGame(__instance);
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenGameModeMenu)), HarmonyPostfix]
    public static void OpenGameModeMenuPostfix(MainMenuManager __instance) => Apply(__instance);

    // オンラインを押した後の「ゲーム作成」ボタン
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenOnlineMenu)), HarmonyPostfix]
    public static void OpenOnlineMenuPostfix(MainMenuManager __instance) => ApplyCreateGame(__instance);

    private static bool loggedCreateHierarchy;
    private const string CreateLabelName = "ZeroCreateGameLabel";
    /// <summary>普段の明るさ(マウスを乗せると 1 になって光る)</summary>
    private const float NormalBrightness = 0.8f;
    private static readonly Color DisabledColor = new(0.45f, 0.45f, 0.45f, 1f);

    // 光らせる処理で使う(毎フレーム、マウスが乗っているかを自分で調べて色を決める)
    private static PassiveButton localButtonRef, onlineButtonRef, createButtonRef;
    private static SpriteRenderer localArtRef, onlineArtRef;
    private static GameObject createLabelHighlight, createLabelInactive;
    private static bool onlineEnabled = true;

    /// <summary>
    /// オンラインを押した後の「ゲーム作成」ボタンを、ローカル・オンラインと同じ見た目にする:
    /// 同じ大きさの絵(枠付き)と、その下に同じ大きさの「ゲーム作成」の札
    /// </summary>
    private static void ApplyCreateGame(MainMenuManager menu)
    {
        try
        {
            var button = menu?.createGameButton;
            if (button == null) return;
            createButtonRef = button;
            if (!loggedCreateHierarchy)
            {
                loggedCreateHierarchy = true;
                LogHierarchy(button.transform, "CreateGame");
            }
            var scaler = button.transform.Find("Scaler");
            if (scaler == null || scaler.Find(CreateLabelName) != null) return; // もう作ってある

            // お手本: ローカルのボタン(先に差し替えておく)
            Apply(menu);
            var localButton = menu.playLocalButton;
            var localArt = localButton?.transform.Find("Scaler/Background")?.GetComponent<SpriteRenderer>();
            var localLabel = localButton?.transform.Find("Scaler/Label");
            var localBar = localLabel?.Find("Inactive")?.GetComponent<SpriteRenderer>();
            if (localArt == null || !IsOurs(localArt.sprite) || localLabel == null || localBar == null)
            {
                Logger.Warn("お手本のローカルのボタンが見つからないので、ゲーム作成のボタンを変えられません", nameof(PlayMenuImages));
                return;
            }
            // ローカルの絵と札の、画面上の大きさと位置関係
            var imgWorld = Vector2.Scale(localArt.sprite.bounds.size, localArt.transform.lossyScale);
            var labelOffset = localLabel.position - localArt.transform.position;
            var barWorld = Vector2.Scale(localBar.sprite.bounds.size, localBar.transform.lossyScale);
            var barCenterOffset = labelOffset + (localBar.transform.position - localLabel.position);

            // ゲーム作成のボタンは状態ごとに別の絵を持っている
            // (マウスを乗せた時 Highlight/Background・普段 Inactive/BackgroundDesat・押した時 Clicked/Background (1))
            var states = new (string path, float brightness)[]
            {
                ("Scaler/Label/Highlight/Background", 1f),
                ("Scaler/Label/Inactive/BackgroundDesat", NormalBrightness),
                ("Scaler/Label/Clicked/Background (1)", 0.9f),
            };
            var arts = new System.Collections.Generic.List<(SpriteRenderer art, float brightness)>();
            foreach (var (path, brightness) in states)
            {
                var art = button.transform.Find(path)?.GetComponent<SpriteRenderer>();
                if (art != null) arts.Add((art, brightness));
            }
            if (arts.Count == 0)
            {
                Logger.Warn("ゲーム作成のボタンの絵が見つかりませんでした", nameof(PlayMenuImages));
                return;
            }

            // 絵と札をまとめた全体が、元のボタンの絵の真ん中に来るようにする
            var areaCenter = arts[0].art.transform.position;
            var top = imgWorld.y / 2f;
            var bottom = barCenterOffset.y - barWorld.y / 2f;
            var imgCenter = areaCenter - new Vector3(0f, (top + bottom) / 2f, 0f);

            var tex = Framed(CreateGameImage);
            if (tex == null) return;
            foreach (var (art, brightness) in arts)
            {
                var scaleX = Mathf.Max(art.transform.lossyScale.x, 0.0001f);
                var ppu = tex.width / (imgWorld.x / scaleX);
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
                sprite.name = $"ZeroPlay_{CreateGameImage}";
                sprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
                art.drawMode = SpriteDrawMode.Simple;
                art.sprite = sprite;
                // 元の絵はボタンの枠の中でだけ見える(マスク)。同じ大きさにすると枠からはみ出すので外す
                art.maskInteraction = SpriteMaskInteraction.None;
                art.color = new Color(brightness, brightness, brightness, 1f);
                art.transform.position = new Vector3(imgCenter.x, imgCenter.y, art.transform.position.z);
            }
            // 元の文字(絵の上に重なる)は隠す
            var origLabel = button.transform.Find("Scaler/Label");
            if (origLabel != null)
                foreach (var tmp in origLabel.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                    tmp.gameObject.SetActive(false);

            // ローカルの札を写して、同じ大きさ・同じ位置関係で付ける
            var label = UnityEngine.Object.Instantiate(localLabel.gameObject, scaler).transform;
            label.name = CreateLabelName;
            var parentScale = scaler.lossyScale;
            label.localScale = new Vector3(
                localLabel.lossyScale.x / Mathf.Max(parentScale.x, 0.0001f),
                localLabel.lossyScale.y / Mathf.Max(parentScale.y, 0.0001f),
                localLabel.localScale.z);
            label.position = new Vector3(imgCenter.x + labelOffset.x, imgCenter.y + labelOffset.y, arts[0].art.transform.position.z - 0.5f);
            foreach (var tmp in label.GetComponentsInChildren<TMPro.TextMeshPro>(true))
            {
                foreach (var tr in tmp.GetComponents<TextTranslatorTMP>()) UnityEngine.Object.Destroy(tr);
                tmp.text = "ゲーム作成";
            }
            createLabelHighlight = label.Find("Highlight")?.gameObject;
            createLabelInactive = label.Find("Inactive")?.gameObject;
            label.Find("Disabled")?.gameObject.SetActive(false);
            createLabelHighlight?.SetActive(false);
            createLabelInactive?.SetActive(true);
            label.gameObject.SetActive(true);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(PlayMenuImages));
        }
    }

    /// <summary>マウスがボタンに乗っているか(ボタンの当たり判定で自分で調べる)</summary>
    private static bool IsHovered(PassiveButton button)
    {
        if (button == null || !button.isActiveAndEnabled || Camera.main == null) return false;
        var point = (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
        foreach (var collider in button.GetComponents<Collider2D>())
            if (collider != null && collider.enabled && collider.OverlapPoint(point)) return true;
        return false;
    }

    /// <summary>
    /// マウスを乗せた時に光らせる(普段は少し暗く)。
    /// オンラインはバニラが乗せた時・離した時に絵を入れ替える作りで、こちらの色が上書きされるので、毎フレーム色を決め直す
    /// </summary>
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate)), HarmonyPostfix]
    public static void HoverPostfix()
    {
        try
        {
            if (localArtRef != null && localButtonRef != null && IsOurs(localArtRef.sprite))
                localArtRef.color = IsHovered(localButtonRef) ? Color.white : Normal();
            if (onlineArtRef != null && onlineButtonRef != null && IsOurs(onlineArtRef.sprite))
                onlineArtRef.color = !onlineEnabled ? DisabledColor : IsHovered(onlineButtonRef) ? Color.white : Normal();
            if (createButtonRef != null && (createLabelHighlight != null || createLabelInactive != null))
            {
                var hovered = IsHovered(createButtonRef);
                if (createLabelHighlight != null && createLabelHighlight.activeSelf != hovered) createLabelHighlight.SetActive(hovered);
                if (createLabelInactive != null && createLabelInactive.activeSelf == hovered) createLabelInactive.SetActive(!hovered);
            }
        }
        catch { }
    }
    private static Color Normal() => new(NormalBrightness, NormalBrightness, NormalBrightness, 1f);

    // オンラインはログイン前など押せない時に白黒の絵になる。差し替えた絵は灰色にして表す
    [HarmonyPatch(typeof(PlayOnlineButtonSprite), nameof(PlayOnlineButtonSprite.SetState)), HarmonyPostfix]
    public static void SetStatePostfix(PlayOnlineButtonSprite __instance, bool __0)
    {
        if (__instance.Sprite == null || !IsOurs(__instance.Sprite.sprite)) return;
        onlineEnabled = __0;
        __instance.Sprite.color = __0 ? Normal() : DisabledColor;
    }

    private static bool IsOurs(Sprite sprite) => sprite != null && sprite.name.StartsWith("ZeroPlay");

    private static void Apply(MainMenuManager menu)
    {
        try
        {
            if (menu == null) return;
            var onlineButton = menu.PlayOnlineButton;
            var localButton = menu.playLocalButton;
            if (!loggedHierarchy)
            {
                loggedHierarchy = true;
                LogHierarchy(onlineButton?.transform, "Online");
                LogHierarchy(localButton?.transform, "Local");
            }

            // オンライン: 絵は PlayOnlineButtonSprite が持っている(通常の絵と白黒の絵)
            SpriteRenderer onlineArt = null;
            if (onlineButton != null)
            {
                var onlineSprite = onlineButton.GetComponentInChildren<PlayOnlineButtonSprite>(true);
                if (onlineSprite != null && onlineSprite.Sprite != null)
                {
                    onlineArt = onlineSprite.Sprite;
                    var sprite = Replace(onlineArt, OnlineImage);
                    if (sprite != null)
                    {
                        onlineSprite.Colored = sprite;
                        onlineSprite.Greyscale = sprite;
                        onlineButtonRef = onlineButton;
                        onlineArtRef = onlineArt;
                    }
                }
            }

            // ローカル: オンラインと同じ作りなので、同じ場所にある絵を探す
            Spread(localButton, -ButtonSpread);
            Spread(onlineButton, ButtonSpread);

            if (localButton != null)
            {
                SpriteRenderer localArt = null;
                if (onlineArt != null && onlineButton != null)
                {
                    var path = RelativePath(onlineButton.transform, onlineArt.transform);
                    localArt = path == null ? null : localButton.transform.Find(path)?.GetComponent<SpriteRenderer>();
                }
                localArt ??= FindLargestArt(localButton);
                if (localArt != null && Replace(localArt, LocalImage) != null)
                {
                    localButtonRef = localButton;
                    localArtRef = localArt;
                }
                else Logger.Warn("ローカルのボタンの絵が見つかりませんでした", nameof(PlayMenuImages));
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(PlayMenuImages));
        }
    }

    /// <summary>元の絵と同じ大きさで差し替える。差し替えた絵を返す</summary>
    private static Sprite Replace(SpriteRenderer renderer, string file) => Replace(renderer, file, ImageWidthRatio);
    private static Sprite Replace(SpriteRenderer renderer, string file, float widthRatio, float heightRatio = 1f)
    {
        if (renderer == null) return null;
        if (IsOurs(renderer.sprite)) return renderer.sprite;
        var original = renderer.sprite;
        Vector2 size = renderer.drawMode == SpriteDrawMode.Simple || original == null
            ? (original != null ? (Vector2)original.bounds.size : Vector2.one)
            : renderer.size;
        if (size.x <= 0f || size.y <= 0f) return null;

        var texture = Framed(file);
        if (texture == null) return null;
        // 横幅を元の絵より少し細くして、隣のボタンの絵と重ならないようにする
        var ppu = Mathf.Max(texture.width / (size.x * widthRatio), texture.height / (size.y * heightRatio));
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), ppu);
        sprite.name = $"ZeroPlay_{file}";
        sprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;

        renderer.drawMode = SpriteDrawMode.Simple;
        renderer.sprite = sprite;
        renderer.color = Color.white;
        Logger.Info($"{file} に差し替え (元の大きさ {size.x:0.00}x{size.y:0.00})", nameof(PlayMenuImages));
        return sprite;
    }

    /// <summary>ボタンを左右にずらす(元の位置を覚えておき、何度呼んでも同じ位置にする)</summary>
    private static readonly System.Collections.Generic.Dictionary<int, float> originalX = new();
    private static void Spread(PassiveButton button, float dx)
    {
        if (button == null) return;
        var t = button.transform;
        var id = t.GetInstanceID();
        if (!originalX.TryGetValue(id, out var x)) originalX[id] = x = t.localPosition.x;
        t.localPosition = new Vector3(x + dx, t.localPosition.y, t.localPosition.z);
    }

    /// <summary>角を丸くして、グレーの外枠をつけた絵を作る</summary>
    private static Texture2D Framed(string file)
    {
        if (framedCache.TryGetValue(file, out var cached) && cached != null) return cached;
        var src = ZeroSettingUI.LoadSprite(file, 100f)?.texture;
        if (src == null) return null;
        int w = src.width, h = src.height;
        var pixels = src.GetPixels32();
        var outPixels = new Color32[pixels.Length];
        var center = new Vector2(w / 2f, h / 2f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var outer = RoundedRectDistance(p, center, new Vector2(w / 2f, h / 2f), CornerRadius);
                var inner = RoundedRectDistance(p, center, new Vector2(w / 2f - FrameWidth, h / 2f - FrameWidth), CornerRadius - FrameWidth);
                // 絵と枠の境目をなめらかにする
                var c = Color32.Lerp(pixels[y * w + x], FrameColor, Mathf.Clamp01(inner + 0.5f));
                c.a = (byte)(255 * Mathf.Clamp01(0.5f - outer));
                outPixels[y * w + x] = c;
            }
        }
        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
        tex.SetPixels32(outPixels);
        tex.Apply();
        tex.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
        framedCache[file] = tex;
        return tex;
    }
    /// <summary>角の丸い四角形の外側への距離(内側は負)</summary>
    private static float RoundedRectDistance(Vector2 p, Vector2 center, Vector2 half, float radius)
    {
        var q = new Vector2(Mathf.Abs(p.x - center.x) - (half.x - radius), Mathf.Abs(p.y - center.y) - (half.y - radius));
        var outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    private static string RelativePath(Transform root, Transform target)
    {
        var path = "";
        for (var t = target; t != null; t = t.parent)
        {
            if (t == root) return path;
            path = path == "" ? t.name : $"{t.name}/{path}";
        }
        return null;
    }

    /// <summary>ボタンの枠(通常/選択中の絵)を除いて、一番大きい絵</summary>
    private static SpriteRenderer FindLargestArt(PassiveButton button)
    {
        SpriteRenderer best = null;
        var bestArea = 0f;
        foreach (var r in button.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.sprite == null || IsFrame(button, r.transform)) continue;
            var area = r.bounds.size.x * r.bounds.size.y;
            if (area > bestArea) { bestArea = area; best = r; }
        }
        return best;
    }
    private static bool IsFrame(PassiveButton button, Transform t)
    {
        for (; t != null && t != button.transform; t = t.parent)
            if ((button.inactiveSprites != null && t == button.inactiveSprites.transform)
                || (button.activeSprites != null && t == button.activeSprites.transform)
                || (button.selectedSprites != null && t == button.selectedSprites.transform))
                return true;
        return false;
    }

    private static void LogHierarchy(Transform root, string label, string indent = "")
    {
        if (root == null) return;
        var r = root.GetComponent<SpriteRenderer>();
        var info = r != null ? $" [Sprite:{r.sprite?.name} size:{r.bounds.size.x:0.00}x{r.bounds.size.y:0.00} mask:{r.maskInteraction}]" : "";
        Logger.Info($"{label}: {indent}{root.name}{info}", nameof(PlayMenuImages));
        for (int i = 0; i < root.childCount; i++)
            LogHierarchy(root.GetChild(i), label, indent + "  ");
    }
}
