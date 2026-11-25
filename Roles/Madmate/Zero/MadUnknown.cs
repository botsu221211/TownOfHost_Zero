using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Madmate;

/// <summary>
/// マッドアンノウン (TOH_Zero)
/// ・キルボタン: 対象にインポスター陣営にしか見えない印(?)を付ける(キルはしない)
/// ・ファントムボタン: 印の付いた人の体の色をランダムに変え、名前を「？？？」にする。自分が死亡するまで続く
/// ・ファントムを使う前に会議に入ると、印はリセットされる
/// ・ベントに入ると自分にも印を付けられる
/// 陣営はインポスター(マッドメイト)、カウントはクルー、勝利条件はインポスター勝利
/// </summary>
public sealed class MadUnknown : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadUnknown),
            player => new MadUnknown(player),
            CustomRoles.MadUnknown,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadY + 1000,
            SetupOptionItem,
            "マッドアンノウン",
            isDesyncImpostor: true
        );
    public MadUnknown(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        killCooldown = OptionKillCooldown.GetFloat();
        phantomCooldown = OptionPhantomCooldown.GetFloat();
        ventCooldown = OptionVentCooldown.GetFloat();
        usesLeft = OptionPhantomUses.GetInt();

        pending = new();
        lastVentTime = float.NegativeInfinity;
        Instances.Add(this);
        CustomRoleManager.MarkOthers.Add(GetMarkOthers);
    }
    public override void OnDestroy()
    {
        Instances.Remove(this);
        if (Instances.Count == 0)
        {
            Disguised.Clear();
            DisguisedColor.Clear();
            CustomRoleManager.MarkOthers.Remove(GetMarkOthers);
        }
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionPhantomCooldown;
    private static OptionItem OptionVentCooldown;
    private static OptionItem OptionPhantomUses;
    enum OptionName
    {
        MadUnknownPhantomCooldown,
        MadUnknownPhantomUses,
    }
    private static float killCooldown;
    private static float phantomCooldown;
    private static float ventCooldown;

    private static readonly List<MadUnknown> Instances = new();
    /// <summary>姿を変えた人 → 変えたマッドアンノウンの PlayerId</summary>
    private static readonly Dictionary<byte, byte> Disguised = new();
    /// <summary>姿を変えた人 → 変えた後の色(コミュサボの解除で元に戻った時にかけ直す)</summary>
    private static readonly Dictionary<byte, byte> DisguisedColor = new();
    public static bool TryGetDisguiseColor(byte playerId, out byte color) => DisguisedColor.TryGetValue(playerId, out color);

    /// <summary>まだファントムを使っていない(会議でリセットされる)印</summary>
    private HashSet<byte> pending;
    private int usesLeft;
    private float lastVentTime;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionPhantomCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.MadUnknownPhantomCooldown, new(0f, 180f, 1f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVentCooldown = FloatOptionItem.Create(RoleInfo, 12, GeneralOption.VentCooldown, new(0f, 30f, 1f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionPhantomUses = IntegerOptionItem.Create(RoleInfo, 13, OptionName.MadUnknownPhantomUses, new(0, 10, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
    }

    /// <summary>名前を「？？？」で表示するか(GetRealName から呼ぶ)</summary>
    public static bool IsDisguised(byte playerId) => Disguised.ContainsKey(playerId);

    /******** キルボタン: 印を付ける ********/
    public float CalculateKillCooldown() => killCooldown;
    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => true;

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        // キルはしない
        info.DoKill = false;
        var target = info.AttemptTarget;
        if (target == null || !Player.IsAlive()) return;
        if (!IsDisguised(target.PlayerId) && pending.Add(target.PlayerId))
            Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: 印を付けた", nameof(MadUnknown));
        Player.SetKillCooldown();
        Utils.NotifyRoles();
    }

    /******** ファントムボタン: 印の付いた人の姿を変える ********/
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = phantomCooldown;
        AURoleOptions.PhantomDuration = 1f;
    }
    public override bool CanUseAbilityButton() => Player.IsAlive() && usesLeft > 0;

    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        // 透明にはならず、ファントムのクールだけ進める。キルクールはそのまま
        killCooldown = -1f;
        canResetAbilityCooldown = true;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || usesLeft <= 0) return false;
        if (pending.Count == 0)
        {
            Logger.Info($"{Player.GetNameWithRole()}: 印の付いた人がいないので何もしない", nameof(MadUnknown));
            return false;
        }

        foreach (var id in pending)
        {
            var target = Utils.GetPlayerById(id);
            if (target == null || !target.IsAlive()) continue;
            Disguise(target);
        }
        pending.Clear();
        usesLeft--;
        Logger.Info($"{Player.GetNameWithRole()}: 姿を変えた (残り {usesLeft} 回)", nameof(MadUnknown));
        Utils.NotifyRoles();
        return false;
    }

    private void Disguise(PlayerControl target)
    {
        if (Disguised.ContainsKey(target.PlayerId)) return;
        Disguised[target.PlayerId] = Player.PlayerId;
        // バニラでは同じ色の人は 2 人いないので、ほかの人が使っていない色から選ぶ
        int count = Palette.PlayerColors.Length;
        byte current = (byte)target.Data.DefaultOutfit.ColorId;
        var used = Main.AllPlayerControls
            .Where(pc => pc != null && pc.Data != null)
            .Select(pc => pc.Data.DefaultOutfit.ColorId)
            .ToHashSet();
        var free = Enumerable.Range(0, count).Where(c => c != current && !used.Contains(c)).ToList();
        byte color = free.Count > 0
            ? (byte)free[IRandom.Instance.Next(free.Count)]
            : (byte)((current + 1 + IRandom.Instance.Next(count - 1)) % count);
        DisguisedColor[target.PlayerId] = color;
        SendDisguiseColor(target, color);
    }

    /// <summary>
    /// 変えた色を、本人以外の画面にだけ送る(ホストの画面は手元で変える)。
    /// 客の本人の画面で自分の色を変えると、その人が会議の画面に移れなくなるので、本人の画面では元の色のままにする
    /// (名前は「？？？」になるので、姿を変えられたことは本人にも分かる)
    /// </summary>
    /// <param name="selfColor">本人の画面に送る色(コミュサボの解除で本人の画面も灰色から戻す時)。null なら本人には送らない</param>
    public static void SendDisguiseColor(PlayerControl target, int color, int? selfColor = null)
    {
        if (target == null || target.Data == null) return;
        target.SetColor((byte)color);
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc == null || pc.AmOwner) continue;
            if (pc.Data == null || pc.Data.Disconnected) continue;
            int sendColor;
            if (pc.PlayerId == target.PlayerId)
            {
                if (selfColor is not { } own) continue;
                sendColor = own;
            }
            else sendColor = color;
            var clientId = pc.GetClientId();
            if (clientId == -1) continue;
            var writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.SetColor, SendOption.Reliable, clientId);
            writer.Write(target.Data.NetId);
            writer.Write((byte)sendColor);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
    /// <summary>元の色に戻す(全員に送る。本人の画面はもともと元の色なので変わらない)</summary>
    private static void SetColor(PlayerControl target, byte color)
    {
        target.SetColor(color);
        var sender = CustomRpcSender.Create(name: $"MadUnknown.SetColor({target.Data.PlayerName})");
        sender.AutoStartRpc(target.NetId, (byte)RpcCalls.SetColor)
            .Write(target.Data.NetId)
            .Write(color)
            .EndRpc();
        sender.SendMessage();
    }

    /// <summary>自分が死んだら、自分が姿を変えた人を元に戻す</summary>
    private void RestoreAll()
    {
        foreach (var id in Disguised.Where(kv => kv.Value == Player.PlayerId).Select(kv => kv.Key).ToArray())
        {
            Disguised.Remove(id);
            DisguisedColor.Remove(id);
            var target = Utils.GetPlayerById(id);
            if (target == null || !Camouflage.PlayerSkins.TryGetValue(id, out var outfit)) continue;
            SetColor(target, (byte)outfit.ColorId);
        }
        Utils.NotifyRoles();
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || Player.IsAlive()) return;
        if (Disguised.ContainsValue(Player.PlayerId)) RestoreAll();
    }

    /******** ベント: 自分にも印を付ける ********/
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (Time.time - lastVentTime < ventCooldown)
        {
            Logger.Info($"{Player.GetNameWithRole()}: ベントクール中なので入れない", nameof(MadUnknown));
            return false;
        }
        lastVentTime = Time.time;
        if (Player.IsAlive() && !IsDisguised(Player.PlayerId) && pending.Add(Player.PlayerId))
        {
            Logger.Info($"{Player.GetNameWithRole()}: ベントで自分に印を付けた", nameof(MadUnknown));
            Utils.NotifyRoles();
        }
        return true;
    }

    /******** 会議: 使っていない印はリセット ********/
    public override void OnStartMeeting() => pending.Clear();

    /******** 印の表示(インポスター陣営と自分だけ) ********/
    private const string MarkText = "<color=#b05cff>?</color>";
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        return seer == Player && pending.Contains(seen.PlayerId) ? MarkText : "";
    }
    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer == null || !seer.Is(CustomRoleTypes.Impostor)) return "";
        return Instances.Any(x => x.pending.Contains(seen.PlayerId)) ? MarkText : "";
    }
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(usesLeft > 0 ? Palette.ImpostorRed : Color.gray, $"({usesLeft})");
}
