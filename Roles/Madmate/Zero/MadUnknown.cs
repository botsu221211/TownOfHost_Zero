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
            RestoreHostDataColors();
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
    /// <summary>姿を変えた人 → 変えた後の色(ホストのデータの色は変えない。ほかの人の画面にだけ見せる)</summary>
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
        RefreshNames();
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
            .Concat(DisguisedColor.Values.Select(c => (int)c))
            .ToHashSet();
        var free = Enumerable.Range(0, count).Where(c => c != current && !used.Contains(c)).ToList();
        byte color = free.Count > 0
            ? (byte)free[IRandom.Instance.Next(free.Count)]
            : (byte)((current + 1 + IRandom.Instance.Next(count - 1)) % count);
        DisguisedColor[target.PlayerId] = color;
        SendDisguiseColor(target, color);
    }

    /// <summary>
    /// 色(と名前)を、本人以外の画面にだけ変える。
    /// 客の本人の画面で自分の色が変わると、その人が会議の画面に移れなくなる(色を変えなければ起きないことを確認済み)。
    /// ・ホストの画面: ホストが持つデータの色を直接変える(チャットや会議もこの色になる)。
    ///   ただし全員に送る時は元の色に戻して送る(GameDataSerializePatch → SwapToOriginalColor)ので、本人には届かない
    /// ・ほかの人の画面: その人あてにだけ、変えた色と「？？？」の名前を入れたデータを送る(チャットの色と名前も変わる)。
    ///   見た目もすぐ変わるよう、色を変える知らせも送る
    /// データが全員に送り直されるとほかの人の画面が元に戻るので、その後に送り直す(OnDataSerialized)
    /// </summary>
    public static void SendDisguiseColor(PlayerControl target, int color)
    {
        if (target == null || target.Data == null) return;
        var data = target.Data;
        var outfit = data.DefaultOutfit;
        // ホストの画面: データの色(送る時は元に戻す)と見た目
        outfit.ColorId = color;
        try { target.RawSetColor(color); } catch (Exception ex) { Logger.Warn($"手元の色: {ex.Message}", nameof(MadUnknown)); }

        var client = AmongUsClient.Instance;
        var name = IsDisguised(target.PlayerId) ? "？？？"
            : (Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(target.PlayerId, out var real) ? real : outfit.PlayerName);
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc == null || pc.AmOwner || pc.PlayerId == target.PlayerId) continue;
            if (pc.Data == null || pc.Data.Disconnected) continue;
            var clientId = pc.GetClientId();
            if (clientId == -1) continue;

            // その人あてにだけデータを送る(GameDataTo)
            var originalName = outfit.PlayerName;
            // データには役職も入っている。番犬(噛み殺しモード)やジャッカルなどあてには、インポスター側の役職をクルーとして送る
            var maskRole = Roles.Crewmate.GuardDog.SeesImpostorsAsCrew(pc) && Roles.Crewmate.GuardDog.IsRealImpostorSide(target) && data.Role != null;
            var originalRole = data.Role != null ? data.Role.Role : RoleTypes.Crewmate;
            var w = MessageWriter.Get(SendOption.Reliable);
            try
            {
                w.StartMessage(6);
                w.Write(client.GameId);
                w.WritePacked(clientId);
                w.StartMessage(1);
                w.WritePacked(data.NetId);
                outfit.PlayerName = name;
                if (maskRole) data.Role.Role = RoleTypes.Crewmate;
                SerializingDisguise = true;
                try { data.Serialize(w, false); }
                finally
                {
                    SerializingDisguise = false;
                    outfit.PlayerName = originalName;
                    if (maskRole) data.Role.Role = originalRole;
                }
                w.EndMessage();
                w.EndMessage();
                client.SendOrDisconnect(w);
            }
            catch (Exception ex) { Logger.Warn($"データ送信: {ex.Message}", nameof(MadUnknown)); }
            finally { w.Recycle(); }

            // 見た目もすぐ変える
            var writer = client.StartRpcImmediately(target.NetId, (byte)RpcCalls.SetColor, SendOption.Reliable, clientId);
            writer.Write(data.NetId);
            writer.Write((byte)color);
            client.FinishRpcImmediately(writer);
        }
        // データを送ると名前が送り直した名前に戻る(マークが消える)ので、名前を送り直す
        NameResync.Request(target.PlayerId);
    }

    /// <summary>マッドアンノウン自身がデータを書き出している間だけ true(全員に送る時の差し替えをしない)</summary>
    public static bool SerializingDisguise { get; private set; }

    /// <summary>
    /// 全員に送るデータを書き出す直前に呼ぶ。姿を変えた人の色を元の色に戻し、戻す前の色を返す(変えなければ null)。
    /// 本人を含む全員に届くデータなので、本人の画面の色が変わらないようにする
    /// </summary>
    public static int? SwapToOriginalColor(NetworkedPlayerInfo info)
    {
        if (SerializingDisguise || info == null || Camouflage.IsCamouflage) return null;
        if (!DisguisedColor.ContainsKey(info.PlayerId)) return null;
        if (!Camouflage.PlayerSkins.TryGetValue(info.PlayerId, out var skin)) return null;
        var outfit = info.DefaultOutfit;
        if (outfit == null || outfit.ColorId == skin.ColorId) return null;
        var current = outfit.ColorId;
        outfit.ColorId = skin.ColorId;
        return current;
    }

    private static bool resendPending;
    /// <summary>
    /// 姿を変えた人のデータが全員に送り直された時に呼ぶ(GameDataSerializePatch)。
    /// ほかの人の画面が元の色・名前に戻っているので、少し後に送り直す
    /// </summary>
    public static void OnDataSerialized(byte playerId)
    {
        if (!AmongUsClient.Instance.AmHost || resendPending || !DisguisedColor.ContainsKey(playerId)) return;
        resendPending = true;
        _ = new LateTask(() =>
        {
            resendPending = false;
            if (!GameStates.IsInGame || Camouflage.IsCamouflage) return;
            foreach (var (id, color) in DisguisedColor.ToArray())
            {
                var target = Utils.GetPlayerById(id);
                if (target == null || target.Data == null || target.Data.Disconnected) continue;
                SendDisguiseColor(target, color);
            }
        }, 0.2f, "MadUnknown.ResendColors");
    }

    /// <summary>試合の終わりなどに、ホストのデータの色を元に戻す(次のロビーに持ち越さない)</summary>
    private static void RestoreHostDataColors()
    {
        foreach (var id in DisguisedColor.Keys.ToArray())
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null || !Camouflage.PlayerSkins.TryGetValue(id, out var skin)) continue;
            pc.Data.DefaultOutfit.ColorId = skin.ColorId;
        }
    }

    /// <summary>自分が死んだら、自分が姿を変えた人を元に戻す</summary>
    private void RestoreAll()
    {
        foreach (var id in Disguised.Where(kv => kv.Value == Player.PlayerId).Select(kv => kv.Key).ToArray())
        {
            Disguised.Remove(id);
            DisguisedColor.Remove(id);
            var target = Utils.GetPlayerById(id);
            if (target == null || target.Data == null || !Camouflage.PlayerSkins.TryGetValue(id, out var skin)) continue;
            // 本人の画面はもともと元の色なので、ホストとほかの人の画面だけ元の色・本当の名前に戻す
            SendDisguiseColor(target, skin.ColorId);
        }
        RefreshNames();
    }

    /// <summary>
    /// 全員の名前を、前と同じでも送り直す。
    /// 色を変えるとその人のデータが全員に送り直され、名前がマーク(ハーフキラーの◆など)の無い元の名前に戻るため。
    /// データが届いた後にもう一度送る
    /// </summary>
    private static void RefreshNames()
    {
        // 名前を個別に送らない人(普通のクルーなど)にも「？？？」を送るため、全員分を送り直す
        Utils.NotifyRoles(NoCache: true, ForceLoop: true);
        _ = new LateTask(() =>
        {
            if (GameStates.IsInGame) Utils.NotifyRoles(NoCache: true, ForceLoop: true);
        }, 0.5f, "MadUnknown.RefreshNames");
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
