using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY;

/// <summary>
/// 役職専用のボタンの絵。自分の役職に合わせて、キル・能力・ペットのボタンの絵を差し替える。
/// MOD を入れている人(ホストを含む)の画面だけで変わる。役職が合わなくなったら元の絵に戻す
/// </summary>
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class RoleButtonImages
{
    private enum Slot { Kill, Ability, Pet }

    /// <summary>役職ごとの (ボタン, 画像ファイル)</summary>
    private static readonly Dictionary<CustomRoles, (Slot slot, string file)[]> Images = new()
    {
        [CustomRoles.Sheriff] = new[] { (Slot.Kill, "ButtonSheriffKill.png") },
        [CustomRoles.Sniper] = new[] { (Slot.Ability, "ButtonSniperShot.png") },
        [CustomRoles.SerialKiller] = new[] { (Slot.Kill, "ButtonSerialKillerKill.png") },
        [CustomRoles.Charger] = new[] { (Slot.Ability, "ButtonChargerAbility.png") },
        [CustomRoles.BountyHunter] = new[] { (Slot.Kill, "ButtonBountyHunterKill.png") },
        [CustomRoles.Arsonist] = new[] { (Slot.Kill, "ButtonArsonistDouse.png"), (Slot.Pet, "ButtonArsonistIgnite.png") },
    };

    // ボタンごとの元の絵(差し替える前の絵)
    private static readonly Dictionary<Slot, Sprite> originals = new();
    // 差し替えた絵(画像ファイルとボタンの大きさごと)
    private static readonly Dictionary<string, Sprite> cache = new();

    public static void Postfix(HudManager __instance)
    {
        try
        {
            var pc = PlayerControl.LocalPlayer;
            (Slot slot, string file)[] wanted = null;
            if (pc != null && AmongUsClient.Instance.IsGameStarted && !GameStates.IsLobby)
                Images.TryGetValue(pc.GetCustomRole(), out wanted);

            Apply(__instance.KillButton, Slot.Kill, wanted);
            Apply(__instance.AbilityButton, Slot.Ability, wanted);
            Apply(__instance.PetButton, Slot.Pet, wanted);
        }
        catch (System.Exception ex)
        {
            Logger.Exception(ex, nameof(RoleButtonImages));
        }
    }

    private static void Apply(ActionButton button, Slot slot, (Slot slot, string file)[] wanted)
    {
        if (button == null || button.graphic == null) return;
        string file = null;
        if (wanted != null)
            foreach (var w in wanted)
                if (w.slot == slot) file = w.file;

        var current = button.graphic.sprite;
        var isOurs = current != null && current.name.StartsWith("ZeroButton_");
        if (file == null)
        {
            // 役職が合わなくなったら元の絵に戻す
            if (isOurs && originals.TryGetValue(slot, out var original) && original != null)
                button.graphic.sprite = original;
            return;
        }

        if (!isOurs) originals[slot] = current; // バニラが絵を入れ直したら、それを元の絵として覚え直す
        var baseSprite = originals.TryGetValue(slot, out var o) ? o : null;
        var sprite = Load(file, baseSprite);
        if (sprite != null && current != sprite) button.graphic.sprite = sprite;
    }

    // 元の絵と同じ大きさになるように読み込む
    private static Sprite Load(string file, Sprite baseSprite)
    {
        var width = baseSprite != null ? baseSprite.bounds.size.x : 1.15f;
        var key = $"{file}@{width:0.000}";
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var tex = ZeroSettingUI.LoadSprite(file, 100f)?.texture;
        if (tex == null) return null;
        var ppu = tex.width / Mathf.Max(width, 0.1f);
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
        sprite.name = $"ZeroButton_{file}";
        sprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
        cache[key] = sprite;
        return sprite;
    }
}
