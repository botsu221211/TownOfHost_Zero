//参考：TOH-K
using HarmonyLib;

namespace TownOfHostY;



[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.Serialize))]
class GameDataSerializePatch
{

    public static int SerializeMessageCount;

    public static bool DontTouch;

    public static bool Prefix(NetworkedPlayerInfo __instance, ref bool __result, out string __state)
    {
        __state = null;
        if (AmongUsClient.Instance == null || !GameStates.IsInGame)
        {
            __result = true;
            return true;
        }
        // ダミー(CustomNetObject)の見た目を送る間は、ホストの名前を絵に書き換えたまま送る必要があるので触らない
        // マッドアンノウンが1人あてに送るデータも、書き換えたまま送る
        if (CustomNetObject.SerializingSprite || Roles.Madmate.MadUnknown.SerializingDisguise)
        {
            __result = true;
            return true;
        }
        if (DontTouch)
        {
            __instance.ClearDirtyBits();
            __result = false;
            return false;
        }
        if (SerializeMessageCount > 0)
        {
            __result = true;
            MaskName(__instance, out __state);
            restoreColor = Roles.Madmate.MadUnknown.SwapToOriginalColor(__instance);
            return true;
        }

        if (Options.CurrentGameMode != CustomGameMode.Standard
            || !GameStates.IsMeeting
            || ExileController.Instance != null
            || AntiBlackout.IsCached)
        {
            MaskName(__instance, out __state);
            restoreColor = Roles.Madmate.MadUnknown.SwapToOriginalColor(__instance);
            return true;
        }
        __instance.ClearDirtyBits();
        __result = false;
        return false;
    }

    /// <summary>マッドアンノウンで変えた色を、送った後にホストのデータへ戻すための値</summary>
    private static int? restoreColor;

    public static void Postfix(NetworkedPlayerInfo __instance, string __state, bool __result)
    {
        if (__state != null) __instance.DefaultOutfit.PlayerName = __state;
        if (restoreColor is { } color && __instance?.DefaultOutfit != null) __instance.DefaultOutfit.ColorId = color;
        restoreColor = null;
        if (!__result || __instance == null || CustomNetObject.SerializingSprite || Roles.Madmate.MadUnknown.SerializingDisguise) return;
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !GameStates.IsInGame) return;
        // マッドアンノウンに姿を変えられた人のデータが送られたら、ほかの人の画面の色・名前をかけ直す
        Roles.Madmate.MadUnknown.OnDataSerialized(__instance.PlayerId);
        // データが届くと、その人の名前が全員の画面でマーク(印)の無い名前に戻るので、名前を送り直す
        NameResync.Request(__instance.PlayerId);
    }

    private static void MaskName(NetworkedPlayerInfo info, out string original)
    {
        original = null;
        if (!AmongUsClient.Instance.AmHost) return;
        // ホストの画面の名前(ホストから見えている役職など)が入ったまま全員に送られると、
        // 役職が全員に透けてしまう。ホストが生きていても死んでいても、送る時は本当の名前に戻す
        if (PlayerControl.LocalPlayer == null) return;
        if (Camouflage.IsCamouflage || MushroomMixupUpdateSystemPatch.InSabotage) return;
        if (Main.AllPlayerNames == null || !Main.AllPlayerNames.TryGetValue(info.PlayerId, out var realName)) return;
        // マッドアンノウンに姿を変えられた人は、本当の名前ではなく「？？？」で送る(送り直しで名前が戻らないように)
        if (Roles.Madmate.MadUnknown.IsDisguised(info.PlayerId)) realName = "？？？";
        var outfit = info.DefaultOutfit;
        if (outfit == null || outfit.PlayerName == realName) return;
        original = outfit.PlayerName;
        outfit.PlayerName = realName;
    }
}

[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.MarkDirty))]
class NetworkedPlayerInfoMarkDirtyPatch
{
    public static bool Prefix() => GameDataSerializePatch.DontTouch is false;
}
/// <summary>
/// ある人のデータが送られると、全員の画面でその人の名前がデータの名前(マークなどの無い名前)に戻る。
/// ホストは「前と同じ名前なら送らない」ので、その人の名前を送り直す(マークが消えたままにならないように)
/// </summary>
public static class NameResync
{
    private static readonly System.Collections.Generic.HashSet<byte> pending = new();
    private static bool scheduled;

    public static void Request(byte playerId)
    {
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !GameStates.IsInGame) return;
        pending.Add(playerId);
        if (scheduled) return;
        scheduled = true;
        _ = new LateTask(() =>
        {
            scheduled = false;
            if (!GameStates.IsInGame || Main.LastNotifyNames == null) { pending.Clear(); return; }
            foreach (var id in pending)
            {
                foreach (var seer in Main.AllPlayerControls)
                {
                    // ホスト自身の画面は送り直さない(ホストの名前を変えるとデータがまた送られて繰り返すため)
                    if (seer == null || seer.AmOwner || seer.PlayerId == id) continue;
                    Main.LastNotifyNames[(id, seer.PlayerId)] = null;
                }
            }
            pending.Clear();
            Utils.NotifyRoles(isForMeeting: GameStates.IsMeeting);
        }, 0.3f, "NameResync");
    }
}
