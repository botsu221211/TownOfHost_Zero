using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Impostor;

/// <summary>
/// イビルアント / Evil Ant (TOH_Zero)
/// ・死体の近くで能力(シェイプ)を使い、設定秒数その場で立ち止まるとカウントが 1 増える(変身はしない)
///   動いたり会議が始まったりすると失敗。カウントは自分の名前の横に出る
/// ・「死体を通報不可にする」がオンなら、カウントを増やした死体は通報できなくなる
/// ・会議中に自投票してから誰かに投票すると、カウントの数だけその人に票が入る(カウントは 0 に戻る)。設定回数まで
/// </summary>
public sealed class EvilAnt : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilAnt),
            player => new EvilAnt(player),
            CustomRoles.EvilAnt,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 2600,
            SetupOptionItem,
            "イビルアント"
        );
    public EvilAnt(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        count = 0;
        usesLeft = OptionMeetingUses.GetInt();
        carrying = false;
        selecting = false;
        boostTarget = byte.MaxValue;
        boostVotes = 0;
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionAbilityCooldown;
    private static OptionItem OptionStandSeconds;
    private static OptionItem OptionMeetingUses;
    private static OptionItem OptionMakeUnreportable;
    enum OptionName
    {
        EvilAntAbilityCooldown,
        EvilAntStandSeconds,
        EvilAntMeetingUses,
        EvilAntMakeUnreportable,
    }

    /// <summary>死体のそばと判定する距離</summary>
    private const float BodyRange = 1.5f;
    /// <summary>これ以上動いたら「立ち止まっていない」</summary>
    private const float MoveTolerance = 0.15f;

    private int count;
    private int usesLeft;
    // 死体を盗んでいる(立ち止まっている)最中
    private bool carrying;
    private byte carryingBody;
    private Vector2 carryPos;
    private float carryEndAt;
    private int lastShownSecond = -1;
    // 会議の自投票
    private bool selecting;
    private byte boostTarget;
    private int boostVotes;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAbilityCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.EvilAntAbilityCooldown, new(0f, 255f, 1f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionStandSeconds = FloatOptionItem.Create(RoleInfo, 12, OptionName.EvilAntStandSeconds, new(0f, 30f, 0.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMeetingUses = IntegerOptionItem.Create(RoleInfo, 13, OptionName.EvilAntMeetingUses, new(1, 3, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMakeUnreportable = BooleanOptionItem.Create(RoleInfo, 14, OptionName.EvilAntMakeUnreportable, true, false);
    }

    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = OptionAbilityCooldown.GetFloat();
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    // ───── 能力(シェイプ): 死体のそばで立ち止まる ─────
    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        animate = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || !GameStates.IsInTask) return false;
        if (carrying) return false;
        var body = NearestBody();
        if (body == null)
        {
            Logger.Info($"{Player.GetNameWithRole()} : 近くに死体がない", nameof(EvilAnt));
            Utils.SendMessageAutoSplit("近くに死体がありません。", true, Utils.ColorString(Palette.ImpostorRed, "【イビルアント】"), Player.PlayerId);
            return false;
        }
        carrying = true;
        carryingBody = body.ParentId;
        carryPos = Player.GetTruePosition();
        carryEndAt = Time.realtimeSinceStartup + OptionStandSeconds.GetFloat();
        lastShownSecond = -1;
        Logger.Info($"{Player.GetNameWithRole()} : 死体({carryingBody})のそばで立ち止まり始めた", nameof(EvilAnt));
        // 使ったので能力のクールダウンを数え直す
        Player.RpcResetAbilityCooldown();
        Utils.NotifyRoles(SpecifySeer: Player);
        return false;
    }

    private DeadBody NearestBody()
    {
        var pos = Player.GetTruePosition();
        return Object.FindObjectsOfType<DeadBody>()
            .Where(b => b != null && !b.Reported && Vector2.Distance(pos, b.TruePosition) <= BodyRange)
            .OrderBy(b => Vector2.Distance(pos, b.TruePosition))
            .FirstOrDefault();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!carrying || !AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive() || !GameStates.IsInTask || MeetingHud.Instance != null)
        {
            StopCarrying("中断");
            return;
        }
        if (Vector2.Distance(Player.GetTruePosition(), carryPos) > MoveTolerance)
        {
            StopCarrying("動いたので失敗");
            return;
        }
        var left = carryEndAt - Time.realtimeSinceStartup;
        if (left <= 0f)
        {
            carrying = false;
            count++;
            Logger.Info($"{Player.GetNameWithRole()} : カウント +1 (今 {count})", nameof(EvilAnt));
            if (OptionMakeUnreportable.GetBool() && !ReportDeadBodyPatch.CannotReportByDeadBodyList.Contains(carryingBody))
                ReportDeadBodyPatch.CannotReportByDeadBodyList.Add(carryingBody);
            Utils.NotifyRoles(SpecifySeer: Player);
            return;
        }
        var shown = Mathf.CeilToInt(left);
        if (shown != lastShownSecond)
        {
            lastShownSecond = shown;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
    }

    private void StopCarrying(string reason)
    {
        carrying = false;
        Logger.Info($"{Player.GetNameWithRole()} : 死体を盗むのを{reason}", nameof(EvilAnt));
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!carrying || isForMeeting || seer != Player || seen != Player) return "";
        return Utils.ColorString(Palette.ImpostorRed, $"死体を盗んでいる… あと{Mathf.Max(lastShownSecond, 0)}秒 (動くと失敗)");
    }

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(usesLeft > 0 ? Palette.ImpostorRed : Color.gray, $"({count})");

    // ───── 会議: 自投票 → 投票した人にカウント分の票 ─────
    public override void OnStartMeeting()
    {
        carrying = false;
        selecting = false;
        boostTarget = byte.MaxValue;
        boostVotes = 0;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || usesLeft <= 0 || count <= 0) return;
        var c = count;
        var u = usesLeft;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsMeeting || Player == null || !Player.IsAlive()) return;
            Utils.SendMessageAutoSplit($"自投票してから誰かに投票すると、その人に{c}票入ります(あと{u}回)。", true,
                Utils.ColorString(Palette.ImpostorRed, "【イビルアント】"), Player.PlayerId);
        }, 5f, "EvilAnt.Guide");
    }

    public override bool CheckVoteAsVoter(PlayerControl votedFor)
    {
        if (!Player.IsAlive()) return true;
        var title = Utils.ColorString(Palette.ImpostorRed, "【イビルアント】");
        if (selecting)
        {
            if (votedFor == null)
            {
                selecting = false;
                Utils.SendMessageAutoSplit("能力をやめました。", true, title, Player.PlayerId);
                return false;
            }
            selecting = false;
            if (votedFor.PlayerId == Player.PlayerId) return true; // もう一度自投票したら普通の自投票
            // 能力の票は、自分の普通の投票とは別に足す(この投票は取り消して、普通の投票はこの後にできる)
            boostTarget = votedFor.PlayerId;
            boostVotes = count;
            count = 0;
            usesLeft--;
            Logger.Info($"{Player.GetNameWithRole()} が能力で {votedFor.GetNameWithRole()} に {boostVotes} 票", nameof(EvilAnt));
            Utils.SendMessageAutoSplit($"{votedFor.GetRealName(true)} に{boostVotes}票入れました。\n続けて普通の投票をしてください。", true, title, Player.PlayerId);
            Utils.NotifyRoles(SpecifySeer: Player);
            return false;
        }
        if (votedFor != null && votedFor.PlayerId == Player.PlayerId && usesLeft > 0 && count > 0)
        {
            selecting = true;
            Utils.SendMessageAutoSplit($"票を入れる人に投票してください({count}票入ります)。\n(スキップでやめる・もう一度自投票すると普通の自投票になる)", true, title, Player.PlayerId);
            return false;
        }
        return true;
    }

    /// <summary>会議の集計で足す能力の票(投票者 = イビルアント、投票先、票数)。MeetingVoteManager から呼ぶ</summary>
    public static System.Collections.Generic.IEnumerable<(byte voter, byte target, int votes)> BonusVotes()
    {
        foreach (var ant in CustomRoleManager.AllActiveRoles.Values.OfType<EvilAnt>())
        {
            if (ant.Player == null || !ant.Player.IsAlive()) continue;
            if (ant.boostTarget == byte.MaxValue || ant.boostVotes <= 0) continue;
            yield return (ant.Player.PlayerId, ant.boostTarget, ant.boostVotes);
        }
    }
}
