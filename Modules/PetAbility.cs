using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hazel;
using UnityEngine;

using TownOfHostY.Attributes;
using TownOfHostY.Roles.Core;

namespace TownOfHostY;

/// <summary>
/// ペットを撫でて能力を使う仕組み (TownOfHost-Pko の Petactionpatch を参考)。
/// ・撫でた合図(RpcCalls.Pet)をホストが受け取り、役職の <see cref="RoleBase.OnPet"/> を呼ぶ
/// ・クールダウンはホストで数え、本人の名前の下に表示する
/// ・設定でペットを付けていない人に紫のペット(pet_test)を自動で付ける
/// </summary>
public static class PetAbility
{
    /// <summary>クールダウンがこれ以上なら「今は使えない」</summary>
    public const float Unusable = 255f;
    private const float MinInterval = 0.5f; // 連打対策
    private const string DefaultPetId = "pet_test"; // 紫のペット

    public static OptionItem AutoGrantPet;

    private static readonly Dictionary<byte, float> readyAt = new();
    private static readonly Dictionary<byte, float> lastPet = new();
    private static readonly Dictionary<byte, int> lastShownSecond = new();
    private static readonly HashSet<byte> grantedPet = new();
    private static bool wasInTask;

    private static float Now => Time.realtimeSinceStartup;

    public static void SetupCustomOption(int id)
    {
        AutoGrantPet = BooleanOptionItem.Create(id, "AutoGrantPet", true, TabGroup.ModMainSettings, false)
            .SetColor(new Color32(160, 90, 255, 255))
            .SetGameMode(CustomGameMode.All);
    }

    [GameModuleInitializer]
    public static void Init()
    {
        readyAt.Clear();
        lastPet.Clear();
        lastShownSecond.Clear();
        grantedPet.Clear();
        wasInTask = false;
    }

    private static RoleBase PetRole(PlayerControl pc)
    {
        var role = pc?.GetRoleClass();
        return role != null && role.UsePetAbility ? role : null;
    }

    /// <summary>クールダウンを最初から数え直す(役職の今のクールダウン秒数で)</summary>
    public static void ResetCooldown(PlayerControl pc)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        var role = PetRole(pc);
        if (role == null) return;
        readyAt[pc.PlayerId] = Now + Mathf.Max(0f, role.PetCooldown);
        lastShownSecond.Remove(pc.PlayerId);
    }

    private static float CooldownLeft(PlayerControl pc)
        => readyAt.TryGetValue(pc.PlayerId, out var t) ? Mathf.Max(0f, t - Now) : 0f;

    /// <summary>本人の名前の下に出すペット能力の状態</summary>
    public static string GetLowerText(PlayerControl seer, bool isForMeeting)
    {
        if (isForMeeting || !AmongUsClient.Instance.AmHost || !GameStates.IsInTask) return "";
        var role = PetRole(seer);
        if (role == null || !seer.IsAlive() || role.PetCooldown >= Unusable) return "";
        var left = CooldownLeft(seer);
        return left > 0f
            ? Utils.ColorString(new Color32(190, 150, 255, 255), $"ペット: あと{Mathf.CeilToInt(left)}秒")
            : Utils.ColorString(new Color32(190, 150, 255, 255), "ペット: 使える");
    }

    /// <summary>撫でた時の処理(ホスト)</summary>
    public static void OnPet(PlayerControl pc)
    {
        if (!AmongUsClient.Instance.AmHost || pc == null || !pc.IsAlive()) return;
        if (!GameStates.IsInTask || GameStates.IsMeeting || ExileController.Instance != null) return;
        if (pc.inVent || pc.inMovingPlat || pc.onLadder || pc.walkingToVent) return;
        var role = PetRole(pc);
        if (role == null) return;

        if (lastPet.TryGetValue(pc.PlayerId, out var last) && Now - last < MinInterval) return;
        lastPet[pc.PlayerId] = Now;
        if (role.PetCooldown >= Unusable || CooldownLeft(pc) > 0f) return;

        Logger.Info($"{pc.GetNameWithRole()} がペットを撫でて能力を使った", nameof(PetAbility));
        role.OnPet();
        ResetCooldown(pc); // ベントから出た時と同じく、使った後は数え直す
        Utils.NotifyRoles(SpecifySeer: pc);
    }

    private static void CancelPet(PlayerPhysics physics)
    {
        try
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(physics.NetId, (byte)RpcCalls.CancelPet, SendOption.None);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        catch { }
    }

    // ほかの人が撫でた
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
    private static class ReceivePetPatch
    {
        public static void Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] byte callId)
        {
            if ((RpcCalls)callId != RpcCalls.Pet || !AmongUsClient.Instance.AmHost || GameStates.IsLobby) return;
            var pc = __instance.myPlayer;
            if (pc == null || pc.AmOwner) return;
            if (PetRole(pc) != null && GameStates.IsInTask)
            {
                // 撫でたまま動けなくならないように、撫でるのをすぐやめさせる
                CancelPet(__instance);
                _ = new LateTask(() => CancelPet(__instance), 0.4f, "PetAbility.CancelPet");
            }
            OnPet(pc);
        }
    }

    // ホストが撫でた
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.TryPet))]
    private static class LocalPetPatch
    {
        public static void Prefix(PlayerControl __instance)
        {
            if (!AmongUsClient.Instance.AmHost || GameStates.IsLobby || !__instance.AmOwner) return;
            OnPet(__instance);
        }
    }

    // タスクターンの始まりにクールダウンを数え直し、表示の秒数が変わったら名前を更新する
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    private static class UpdatePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if (__instance != PlayerControl.LocalPlayer || !AmongUsClient.Instance.AmHost || !GameStates.IsInGame) return;
            var inTask = GameStates.IsInTask && Main.introDestroyed && MeetingHud.Instance == null && ExileController.Instance == null;
            if (inTask && !wasInTask)
            {
                foreach (var pc in Main.AllAlivePlayerControls) ResetCooldown(pc);
            }
            wasInTask = inTask;
            if (!inTask) return;

            foreach (var pc in Main.AllAlivePlayerControls)
            {
                var role = PetRole(pc);
                if (role == null) continue;
                var shown = role.PetCooldown >= Unusable ? -2 : Mathf.CeilToInt(CooldownLeft(pc));
                if (lastShownSecond.TryGetValue(pc.PlayerId, out var prev) && prev == shown) continue;
                lastShownSecond[pc.PlayerId] = shown;
                Utils.NotifyRoles(SpecifySeer: pc);
            }
        }
    }

    // ===== ペットの自動付与 =====
    private static bool HasPet(PlayerControl pc)
    {
        var petId = pc.Data?.DefaultOutfit?.PetId ?? "";
        if (string.IsNullOrEmpty(petId)) return false;
        petId = petId.ToLowerInvariant();
        return petId is not ("none" or "pet_none" or "pet_emptypet" or "pet_enmptypet");
    }

    private static void SetPet(PlayerControl pc, string petId)
    {
        pc.RpcSetPet(petId);
        if (Camouflage.PlayerSkins.TryGetValue(pc.PlayerId, out var outfit))
        {
            outfit.PetId = petId;
            Camouflage.PlayerSkins[pc.PlayerId] = outfit;
        }
    }

    private static void GrantPets()
    {
        if (!AmongUsClient.Instance.AmHost || AutoGrantPet?.GetBool() != true || !GameStates.IsInGame) return;
        foreach (var pc in Main.AllAlivePlayerControls.ToArray())
        {
            if (pc.Is(CustomRoles.GM) || HasPet(pc)) continue;
            try
            {
                SetPet(pc, DefaultPetId);
                grantedPet.Add(pc.PlayerId);
                Logger.Info($"{pc.GetNameWithRole()} にペットを自動で付けた", nameof(PetAbility));
            }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(PetAbility)); }
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    private static class GrantAfterIntroPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            _ = new LateTask(GrantPets, 0.6f, "PetAbility.Grant");
            _ = new LateTask(GrantPets, 2f, "PetAbility.GrantRetry");
        }
    }

    [HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
    private static class GrantAfterMeetingPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            _ = new LateTask(GrantPets, 1.5f, "PetAbility.GrantAfterMeeting");
        }
    }

    // 試合が終わったら、自動で付けたペットを外す(ロビーに付けたまま戻らないように)
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class RemoveOnGameEndPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            foreach (var id in grantedPet.ToArray())
            {
                var pc = Utils.GetPlayerById(id);
                if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                try { pc.RpcSetPet(""); } catch { }
            }
            grantedPet.Clear();
        }
    }
}
