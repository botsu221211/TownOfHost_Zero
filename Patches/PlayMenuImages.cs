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
    public static void StartPostfix(MainMenuManager __instance) => Apply(__instance);

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenGameModeMenu)), HarmonyPostfix]
    public static void OpenGameModeMenuPostfix(MainMenuManager __instance) => Apply(__instance);

    // オンラインはログイン前など押せない時に白黒の絵になる。差し替えた絵は灰色にして表す
    [HarmonyPatch(typeof(PlayOnlineButtonSprite), nameof(PlayOnlineButtonSprite.SetState)), HarmonyPostfix]
    public static void SetStatePostfix(PlayOnlineButtonSprite __instance, bool __0)
    {
        if (__instance.Sprite == null || !IsOurs(__instance.Sprite.sprite)) return;
        __instance.Sprite.color = __0 ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
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
                if (localArt != null) Replace(localArt, LocalImage);
                else Logger.Warn("ローカルのボタンの絵が見つかりませんでした", nameof(PlayMenuImages));
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(PlayMenuImages));
        }
    }

    /// <summary>元の絵と同じ大きさで差し替える。差し替えた絵を返す</summary>
    private static Sprite Replace(SpriteRenderer renderer, string file)
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
        var ppu = Mathf.Max(texture.width / (size.x * ImageWidthRatio), texture.height / size.y);
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
