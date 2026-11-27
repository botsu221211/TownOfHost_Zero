using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using TownOfHostY.Modules;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// スカーレット / Scarlet (TOH_Zero・Nebula on the Ship から移植)
/// ・ファントムボタン(ワンクリック): 一番近くの人を「本命」にする(1人だけ)
/// ・キルボタン: 相手を「キープ」にする(設定人数まで・キルはしない)
/// ・自分の画面では本命に濃いハート、キープに薄いハートが付き、キープの役職が分かる。
///   本命とキープの画面では、スカーレットに濃いハートが付く
/// ・本命が死ぬとスカーレットは後追いし、スカーレットが死ぬと本命が後追いする
/// ・深紅の号令: 会議で自投票してから誰かに投票すると、キープと本命の投票先を自分と同じ人に変える(設定回数まで)
/// ・ベント・サボタージュは使えない
/// ・本命と自分が生きていて本命が勝った時、スカーレットの勝利で上書きし、自分と本命が勝つ(勝利優先度で比べる)
/// </summary>
public sealed class Scarlet : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Scarlet),
            player => new Scarlet(player),
            CustomRoles.Scarlet,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 2000,
            SetupOptionItem,
            "スカーレット",
            "#c8102e",
            isDesyncImpostor: true
        );
    public Scarlet(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        honmei = byte.MaxValue;
        keeps = new();
        commandsLeft = OptionCommandUses.GetInt();
        selecting = false;
        commandTarget = byte.MaxValue;
        followDone = false;
        Instances.Add(this);
        if (Instances.Count == 1) CustomRoleManager.MarkOthers.Add(GetMarkOthers);
    }
    public override void OnDestroy()
    {
        Instances.Remove(this);
        if (Instances.Count == 0) CustomRoleManager.MarkOthers.Remove(GetMarkOthers);
    }

    private static OptionItem OptionWinPriority;
    private static OptionItem OptionHonmeiCooldown;
    private static OptionItem OptionKeepCooldown;
    private static OptionItem OptionMaxKeeps;
    private static OptionItem OptionCommandUses;
    enum OptionName
    {
        ScarletWinPriority,
        ScarletHonmeiCooldown,
        ScarletKeepCooldown,
        ScarletMaxKeeps,
        ScarletCommandUses,
    }

    private static readonly List<Scarlet> Instances = new();

    private byte honmei;
    private HashSet<byte> keeps;
    private int commandsLeft;
    private bool selecting;
    /// <summary>この会議の号令の投票先(なければ byte.MaxValue)</summary>
    private byte commandTarget;
    /// <summary>後追いの処理をしたか</summary>
    private bool followDone;

    private static readonly Color DeepHeart = new Color32(200, 16, 46, 255);
    private static readonly Color LightHeart = new Color32(255, 158, 181, 255);
    /// <summary>ファントムボタンで本命にできる距離</summary>
    private const float HonmeiRange = 1.8f;

    private static void SetupOptionItem()
    {
        OptionWinPriority = IntegerOptionItem.Create(RoleInfo, 10, OptionName.ScarletWinPriority, new(0, 50, 1), 18, false);
        OptionHonmeiCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.ScarletHonmeiCooldown, new(0f, 180f, 0.5f), 2f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionKeepCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.ScarletKeepCooldown, new(0f, 180f, 0.5f), 2f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMaxKeeps = IntegerOptionItem.Create(RoleInfo, 13, OptionName.ScarletMaxKeeps, new(0, 15, 1), 2, false)
            .SetValueFormat(OptionFormat.Players);
        OptionCommandUses = IntegerOptionItem.Create(RoleInfo, 14, OptionName.ScarletCommandUses, new(0, 15, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
    }

    private bool IsHonmei(byte id) => honmei != byte.MaxValue && honmei == id;
    private bool IsLover(byte id) => IsHonmei(id) || keeps.Contains(id);

    // ───── キルボタン = キープにする ─────
    public bool IsKiller => false;
    public bool CanUseKillButton() => Player.IsAlive() && keeps.Count < OptionMaxKeeps.GetInt();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public float CalculateKillCooldown() => OptionKeepCooldown.GetFloat();
    public bool OverrideKillButtonText(out string text)
    {
        text = "キープ";
        return true;
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsSuicide) return;
        info.DoKill = false;
        var target = info.AttemptTarget;
        if (target == null || !target.IsAlive() || IsLover(target.PlayerId) || keeps.Count >= OptionMaxKeeps.GetInt()) return;
        keeps.Add(target.PlayerId);
        Logger.Info($"{Player.GetNameWithRole()} が {target.GetNameWithRole()} をキープにした ({keeps.Count}/{OptionMaxKeeps.GetInt()})", nameof(Scarlet));
        Player.SetKillCooldown();
        Utils.NotifyRoles(SpecifySeer: Player);
        Utils.NotifyRoles(SpecifySeer: target);
    }

    // ───── ファントムボタン(ワンクリック) = 一番近くの人を本命にする ─────
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = OptionHonmeiCooldown.GetFloat();
    }
    public override bool CanUseAbilityButton() => Player.IsAlive() && honmei == byte.MaxValue;

    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        // 透明にはならない。キルクールは変えない
        killCooldown = -1f;
        canResetAbilityCooldown = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || !GameStates.IsInTask || honmei != byte.MaxValue) return false;
        var pos = Player.GetTruePosition();
        var target = Main.AllAlivePlayerControls
            .Where(pc => pc.PlayerId != Player.PlayerId && !CustomNetObject.Is(pc))
            .Where(pc => Vector2.Distance(pos, pc.GetTruePosition()) <= HonmeiRange)
            .OrderBy(pc => Vector2.Distance(pos, pc.GetTruePosition()))
            .FirstOrDefault();
        if (target == null)
        {
            Utils.SendMessageAutoSplit("近くに本命にできる人がいません。", true, Utils.ColorString(RoleInfo.RoleColor, "【スカーレット】"), Player.PlayerId);
            return false;
        }
        honmei = target.PlayerId;
        keeps.Remove(target.PlayerId);
        canResetAbilityCooldown = true;
        Logger.Info($"{Player.GetNameWithRole()} が {target.GetNameWithRole()} を本命にした", nameof(Scarlet));
        Utils.NotifyRoles(SpecifySeer: Player);
        Utils.NotifyRoles(SpecifySeer: target);
        return false;
    }

    // ───── 後追い ─────
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || followDone || honmei == byte.MaxValue) return;
        if (!GameStates.IsInGame || CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
        var partner = Utils.GetPlayerById(honmei);
        if (partner == null) return;
        var meAlive = Player.IsAlive();
        var partnerAlive = partner.IsAlive();
        if (meAlive == partnerAlive) return;
        followDone = true;
        var follower = meAlive ? Player : partner;
        Logger.Info($"{follower.GetNameWithRole()} が後追いする({(meAlive ? "本命が死亡" : "スカーレットが死亡")})", nameof(Scarlet));
        if (GameStates.IsMeeting || ExileController.Instance != null)
        {
            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.FollowingSuicide, follower.PlayerId);
            return;
        }
        PlayerState.GetByPlayerId(follower.PlayerId).DeathReason = CustomDeathReason.FollowingSuicide;
        follower.RpcMurderPlayer(follower);
    }

    // ───── 深紅の号令 ─────
    public override void OnStartMeeting()
    {
        selecting = false;
        commandTarget = byte.MaxValue;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || commandsLeft <= 0 || !keeps.Any() && honmei == byte.MaxValue) return;
        var left = commandsLeft;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsMeeting || Player == null || !Player.IsAlive()) return;
            Utils.SendMessageAutoSplit($"自投票してから誰かに投票すると、深紅の号令で本命とキープの投票先もその人にできます(あと{left}回)。", true,
                Utils.ColorString(RoleInfo.RoleColor, "【スカーレット】"), Player.PlayerId);
        }, 5f, "Scarlet.Guide");
    }

    public override bool CheckVoteAsVoter(PlayerControl votedFor)
    {
        if (!Player.IsAlive() || commandsLeft <= 0 || commandTarget != byte.MaxValue) return true;
        var title = Utils.ColorString(RoleInfo.RoleColor, "【スカーレット】");
        if (selecting)
        {
            selecting = false;
            if (votedFor == null)
            {
                Utils.SendMessageAutoSplit("深紅の号令をやめました。", true, title, Player.PlayerId);
                return false;
            }
            if (votedFor.PlayerId == Player.PlayerId) return true; // もう一度自投票したら普通の自投票
            commandTarget = votedFor.PlayerId;
            commandsLeft--;
            Logger.Info($"{Player.GetNameWithRole()} の深紅の号令: {votedFor.GetNameWithRole()}", nameof(Scarlet));
            // もう投票した本命・キープの票も書き換える
            var manager = MeetingVoteManager.Instance;
            if (manager != null)
            {
                foreach (var (voter, vote) in manager.AllVotes.ToArray())
                {
                    if (!IsLover(voter) || !vote.HasVoted || vote.VotedFor == MeetingVoteManager.NoVote) continue;
                    if (Utils.GetPlayerById(voter)?.IsAlive() != true) continue;
                    manager.SetVote(voter, commandTarget, isIntentional: false);
                }
            }
            Utils.SendMessageAutoSplit($"深紅の号令: 本命とキープの投票先を {votedFor.GetRealName(true)} にしました。", true, title, Player.PlayerId);
            return true;
        }
        if (votedFor != null && votedFor.PlayerId == Player.PlayerId && (keeps.Any() || honmei != byte.MaxValue))
        {
            selecting = true;
            Utils.SendMessageAutoSplit("深紅の号令: 投票先にする人に投票してください。\n(スキップでやめる・もう一度自投票すると普通の自投票になる)", true, title, Player.PlayerId);
            return false;
        }
        return true;
    }

    /// <summary>号令の後は、本命とキープの投票先を号令の相手にする</summary>
    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        var result = base.ModifyVote(voterId, sourceVotedForId, isIntentional);
        if (commandTarget == byte.MaxValue || !Player.IsAlive() || !IsLover(voterId)) return result;
        if (sourceVotedForId == MeetingVoteManager.NoVote) return result;
        return (commandTarget, result.numVotes, result.doVote);
    }

    // ───── 表示 ─────
    /// <summary>自分の画面: 本命に濃いハート、キープに薄いハート</summary>
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer != Player) return "";
        if (IsHonmei(seen.PlayerId)) return Utils.ColorString(DeepHeart, "♥");
        if (keeps.Contains(seen.PlayerId)) return Utils.ColorString(LightHeart, "♥");
        return "";
    }
    /// <summary>本命とキープの画面: スカーレットに濃いハート</summary>
    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer == null || seen == null) return "";
        return Instances.Any(s => s.Player == seen && s.IsLover(seer.PlayerId)) ? Utils.ColorString(DeepHeart, "♥") : "";
    }
    /// <summary>スカーレットか、スカーレットの本命・キープか(ほかの人の名前を個別に送る相手にする)</summary>
    public static bool NeedsOthersNames(PlayerControl seer)
        => seer != null && (seer.Is(CustomRoles.Scarlet) || Instances.Any(s => s.IsLover(seer.PlayerId)));

    /// <summary>キープの役職が分かる</summary>
    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (seen != null && keeps.Contains(seen.PlayerId)) enabled = true;
    }

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(commandsLeft > 0 ? RoleInfo.RoleColor : Color.gray, $"({keeps.Count}/{OptionMaxKeeps.GetInt()})");

    // ───── 勝利 ─────
    /// <summary>
    /// ほかの勝利でゲームが終わる時に呼ぶ。本命と自分が生きていて本命が勝っていれば、
    /// 勝利優先度を比べてスカーレット(自分と本命)の勝利で上書きする
    /// </summary>
    public static bool CheckWinOnGameEnd()
    {
        var any = false;
        foreach (var s in Instances.ToArray())
        {
            if (s.Player == null || !s.Player.IsAlive() || s.honmei == byte.MaxValue) continue;
            var partner = Utils.GetPlayerById(s.honmei);
            if (partner == null || !partner.IsAlive()) continue;
            if (!CustomWinnerHolder.WinnerIds.Contains(s.honmei)) continue;
            if (!CustomWinnerHolder.SetPriorityWinner(CustomWinner.Scarlet, OptionWinPriority.GetInt(), new[] { s.Player.PlayerId, s.honmei })) continue;
            Logger.Info($"本命 {partner.GetNameWithRole()} が勝ったので、スカーレットの勝利で上書き", nameof(Scarlet));
            any = true;
        }
        return any;
    }
}
