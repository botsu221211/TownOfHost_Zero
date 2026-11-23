using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY;

/// <summary>
/// ファントムが元の役職で、ほかの人の画面ではクルーに見せる役職(タイガー・カタリストなど)を、ホストがやる時のアビリティボタン。
/// ホストの画面ではこれらの役職はクルーの役職になっているので、バニラのファントムのボタンが動かない。
/// そこでボタンを押した時に透明化のチェック(各役職の OnCheckVanish)を直接呼び、クールダウンもここで数えて表示する。
/// </summary>
[HarmonyPatch]
public static class HostPhantomAbility
{
    private static float timer;
    private static float maxTimer = 10f;
    private static bool wasInTask;
    private static bool spriteSet;

    /// <summary>ホストが、ホスト用のファントムボタンを使う役職で生きているか</summary>
    public static bool IsActive
    {
        get
        {
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsInGame) return false;
            var pc = PlayerControl.LocalPlayer;
            if (pc == null || !pc.IsAlive()) return false;
            var info = pc.GetCustomRole().GetRoleInfo();
            return info != null && info.IsDesyncImpostor && info.BaseRoleType.Invoke() == RoleTypes.Phantom;
        }
    }

    private static bool InTask => GameStates.IsInTask && MeetingHud.Instance == null && ExileController.Instance == null;

    /// <summary>クールダウンを最初から数え直す(ホストの役職の設定の秒数)</summary>
    public static void ResetCooldown()
    {
        var cooldown = 10f;
        try { cooldown = GameOptionsManager.Instance.CurrentGameOptions.GetFloat(FloatOptionNames.PhantomCooldown); }
        catch { }
        maxTimer = Mathf.Max(cooldown, 0.01f);
        timer = maxTimer;
    }

    [HarmonyPatch(typeof(AbilityButton), nameof(AbilityButton.DoClick)), HarmonyPrefix]
    public static bool DoClickPrefix()
    {
        if (!IsActive) return true;
        var pc = PlayerControl.LocalPlayer;
        if (!InTask || timer > 0f || !pc.CanMove) return false;
        // PlayerControlCheckVanishPatch が役職の処理をして、クールダウンもリセットする
        pc.CheckVanish();
        return false;
    }

    [HarmonyPatch(typeof(AbilityButton), "Update"), HarmonyPostfix]
    public static void UpdatePostfix(AbilityButton __instance)
    {
        if (!IsActive)
        {
            wasInTask = false;
            if (!GameStates.IsInGame) spriteSet = false;
            return;
        }
        if (!spriteSet)
        {
            spriteSet = true;
            var phantom = RoleManager.Instance?.GetRole(RoleTypes.Phantom);
            if (phantom != null && phantom.Ability != null) __instance.SetFromSettings(phantom.Ability);
        }

        var inTask = InTask;
        if (inTask && !wasInTask) ResetCooldown(); // タスクターンが始まるたびに最初から
        wasInTask = inTask;
        if (inTask && timer > 0f && !PlayerControl.LocalPlayer.inVent) timer -= Time.deltaTime;
        __instance.SetCoolDown(Mathf.Max(timer, 0f), maxTimer);
    }
}
