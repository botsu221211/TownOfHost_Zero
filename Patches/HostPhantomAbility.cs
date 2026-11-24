using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY;

/// <summary>
/// ファントムが元の役職で、ほかの人の画面ではクルーに見せる役職を、ホストがやる時のアビリティボタン。
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

/// <summary>
/// シェイプシフターが元の役職で、ほかの人の画面ではクルーに見せる役職(白狐など)を、ホストがやる時のアビリティボタン。
/// ホストの画面ではクルーなのでバニラの変身ボタンが動かない。ボタンを押したら変身先を選ぶ画面を自分で開き、
/// 選んだら変身のチェック(各役職の OnCheckShapeshift)を直接呼ぶ。クールダウンもここで数えて表示する
/// </summary>
[HarmonyPatch]
public static class HostShapeshiftAbility
{
    private static float timer;
    private static float maxTimer = 10f;
    private static bool wasInTask;
    private static bool spriteSet;
    private static bool menuOpenedByUs;

    public static bool IsActive
    {
        get
        {
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsInGame) return false;
            var pc = PlayerControl.LocalPlayer;
            if (pc == null || !pc.IsAlive()) return false;
            var info = pc.GetCustomRole().GetRoleInfo();
            return info != null && info.IsDesyncImpostor && info.BaseRoleType.Invoke() == RoleTypes.Shapeshifter;
        }
    }

    private static bool InTask => GameStates.IsInTask && MeetingHud.Instance == null && ExileController.Instance == null;

    public static void ResetCooldown()
    {
        var cooldown = 10f;
        try { cooldown = GameOptionsManager.Instance.CurrentGameOptions.GetFloat(FloatOptionNames.ShapeshifterCooldown); }
        catch { }
        maxTimer = Mathf.Max(cooldown, 0.01f);
        timer = maxTimer;
    }

    [HarmonyPatch(typeof(AbilityButton), nameof(AbilityButton.DoClick)), HarmonyPrefix]
    public static bool DoClickPrefix()
    {
        if (!IsActive) return true;
        var pc = PlayerControl.LocalPlayer;
        if (!InTask || timer > 0f || !pc.CanMove || Minigame.Instance != null) return false;
        var role = RoleManager.Instance?.GetRole(RoleTypes.Shapeshifter)?.TryCast<ShapeshifterRole>();
        if (role == null || role.ShapeshifterMenu == null) return false;
        var menu = Object.Instantiate(role.ShapeshifterMenu);
        menu.transform.SetParent(Camera.main.transform, false);
        menu.transform.localPosition = new Vector3(0f, 0f, -50f);
        menuOpenedByUs = true;
        menu.Begin(null);
        return false;
    }

    // 変身先を選んだ: バニラの処理(ホストの画面ではシェイプシフターではないので弾かれる)の代わりに、変身のチェックを直接呼ぶ
    [HarmonyPatch(typeof(ShapeshifterMinigame), "Shapeshift"), HarmonyPrefix]
    public static bool ShapeshiftPrefix(ShapeshifterMinigame __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!menuOpenedByUs || !IsActive) return true;
        menuOpenedByUs = false;
        try { __instance.Close(); } catch { }
        if (target == null) return false;
        PlayerControl.LocalPlayer.CheckShapeshift(target, false);
        ResetCooldown();
        return false;
    }

    [HarmonyPatch(typeof(AbilityButton), "Update"), HarmonyPostfix]
    public static void UpdatePostfix(AbilityButton __instance)
    {
        if (!IsActive)
        {
            wasInTask = false;
            menuOpenedByUs = false;
            if (!GameStates.IsInGame) spriteSet = false;
            return;
        }
        if (!spriteSet)
        {
            spriteSet = true;
            var ss = RoleManager.Instance?.GetRole(RoleTypes.Shapeshifter);
            if (ss != null && ss.Ability != null) __instance.SetFromSettings(ss.Ability);
        }
        var inTask = InTask;
        if (inTask && !wasInTask) ResetCooldown();
        wasInTask = inTask;
        if (inTask && timer > 0f && !PlayerControl.LocalPlayer.inVent) timer -= Time.deltaTime;
        __instance.SetCoolDown(Mathf.Max(timer, 0f), maxTimer);
    }
}
