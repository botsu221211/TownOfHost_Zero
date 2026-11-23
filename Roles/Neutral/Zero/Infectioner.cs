using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using TownOfHostY.Modules;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// インフェクショナー (TOH_Zero オリジナル)
/// ・会議で自分が投票した相手を感染させる(設定回数まで)
/// ・感染している人が投票した相手にも感染が広がる
/// ・感染状況の可視化がオンなら、感染者にはインフェクショナーにだけ見えるマークがつく
/// ・自分以外の生存者が全員感染したら単独勝利(追加勝利にもできる)
/// ・勝利優先度: ほかの勝利と同時になった時、優先度の高い方が勝つ(通常の陣営勝利は 0)
/// </summary>
public sealed class Infectioner : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Infectioner),
            player => new Infectioner(player),
            CustomRoles.Infectioner,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1200,
            SetupOptionItem,
            "インフェクショナー",
            "#8fbc3f"
        );
    public Infectioner(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        remainingInfects = OptionInfectCount.GetInt();
        visualize = OptionVisualize.GetBool();
        additionalWin = OptionAdditionalWin.GetBool();
        infected.Clear();
    }

    private static OptionItem OptionInfectCount;
    private static OptionItem OptionWinPriority;
    private static OptionItem OptionVisualize;
    private static OptionItem OptionAdditionalWin;
    private enum OptionName
    {
        InfectionerInfectCount,
        InfectionerWinPriority,
        InfectionerVisualize,
        InfectionerAdditionalWin,
    }

    private int remainingInfects;
    private bool visualize;
    private bool additionalWin;
    /// <summary>感染している人 (全インフェクショナーで共通)</summary>
    private static readonly HashSet<byte> infected = new();

    public static int WinPriority => OptionWinPriority?.GetInt() ?? 15;

    private static void SetupOptionItem()
    {
        OptionInfectCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.InfectionerInfectCount, new(1, 15, 1), 2, false)
            .SetValueFormat(OptionFormat.Times);
        OptionWinPriority = IntegerOptionItem.Create(RoleInfo, 11, OptionName.InfectionerWinPriority, new(1, 50, 1), 15, false);
        OptionVisualize = BooleanOptionItem.Create(RoleInfo, 12, OptionName.InfectionerVisualize, true, false);
        OptionAdditionalWin = BooleanOptionItem.Create(RoleInfo, 13, OptionName.InfectionerAdditionalWin, false, false);
    }

    public static bool IsInfected(byte playerId) => infected.Contains(playerId);

    /// <summary>
    /// 会議の終わりに、その会議の投票で感染を広げる(MeetingVoteManager.EndMeeting から呼ぶ)。
    /// 会議が始まった時点の感染者の票だけを数える(同じ会議の中で連鎖はしない)
    /// </summary>
    public static void OnMeetingEnd(IReadOnlyDictionary<byte, MeetingVoteManager.VoteData> votes)
    {
        if (!AmongUsClient.Instance.AmHost || votes == null) return;
        var infectioners = CustomRoleManager.AllActiveRoles.Values.OfType<Infectioner>().ToList();
        if (infectioners.Count == 0) return;

        var infectedAtStart = new HashSet<byte>(infected);
        var newlyInfected = new List<byte>();
        foreach (var vote in votes.Values)
        {
            var target = vote.VotedFor;
            if (target is MeetingVoteManager.Skip or MeetingVoteManager.NoVote) continue;
            if (PlayerState.GetByPlayerId(vote.Voter)?.IsDead != false) continue;
            var targetPc = Utils.GetPlayerById(target);
            if (targetPc == null) continue;

            var voterInfectioner = infectioners.FirstOrDefault(r => r.Player.PlayerId == vote.Voter);
            if (voterInfectioner != null)
            {
                // インフェクショナー自身の投票: 回数が残っていて、まだ感染していない相手なら感染させる
                if (target == vote.Voter || targetPc.Is(CustomRoles.Infectioner) || infected.Contains(target)) continue;
                if (voterInfectioner.remainingInfects <= 0) continue;
                voterInfectioner.remainingInfects--;
                infected.Add(target);
                newlyInfected.Add(target);
                Logger.Info($"{voterInfectioner.Player.GetNameWithRole()} が {targetPc.GetNameWithRole()} を感染させた (残り{voterInfectioner.remainingInfects}回)", nameof(Infectioner));
            }
            else if (infectedAtStart.Contains(vote.Voter))
            {
                // 感染者の投票: 投票先にも感染が広がる
                if (targetPc.Is(CustomRoles.Infectioner) || infected.Contains(target)) continue;
                infected.Add(target);
                newlyInfected.Add(target);
                Logger.Info($"感染者 {Utils.GetPlayerById(vote.Voter)?.GetNameWithRole()} の投票で {targetPc.GetNameWithRole()} に感染した", nameof(Infectioner));
            }
        }
    }

    /// <summary>このインフェクショナーの勝利条件: 自分が生きていて、自分以外の生存者が全員感染している</summary>
    private bool IsWinConditionMet()
    {
        if (!Player.IsAlive()) return false;
        var others = Main.AllAlivePlayerControls.Where(pc => !pc.Is(CustomRoles.Infectioner)).ToList();
        return others.Count > 0 && others.All(pc => infected.Contains(pc.PlayerId));
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || additionalWin) return;
        if (!Main.IsSetRoleFinished || ExileController.Instance != null) return;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
        if (!IsWinConditionMet()) return;

        Logger.Info($"{Player.GetNameWithRole()} 以外の生存者が全員感染したので単独勝利", nameof(Infectioner));
        SetSoloWinners();
    }

    private static void SetSoloWinners()
    {
        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Infectioner);
        foreach (var role in CustomRoleManager.AllActiveRoles.Values.OfType<Infectioner>())
        {
            if (!role.additionalWin && role.IsWinConditionMet())
                CustomWinnerHolder.WinnerIds.Add(role.Player.PlayerId);
        }
    }

    /// <summary>
    /// ほかの勝利でゲームが終わる時に呼ぶ。インフェクショナーの勝利条件も満たしていれば、
    /// 勝利優先度を比べて(通常の陣営勝利は 0)インフェクショナーの単独勝利に置き換える
    /// </summary>
    public static bool CheckWinOnGameEnd(CustomWinner currentWinner)
    {
        if (currentWinner is CustomWinner.Infectioner or CustomWinner.Draw or CustomWinner.None) return false;
        var winners = CustomRoleManager.AllActiveRoles.Values.OfType<Infectioner>()
            .Where(r => !r.additionalWin && r.IsWinConditionMet()).ToList();
        if (winners.Count == 0) return false;
        if (WinPriority <= 0) return false; // 通常の勝利の優先度(0)以下なら奪わない
        Logger.Info($"{currentWinner} の勝利と同時に感染が完了したので、優先度 {WinPriority} のインフェクショナーが勝利", nameof(Infectioner));
        SetSoloWinners();
        return true;
    }

    // 追加勝利にする がオンの時: ゲーム終了時に条件を満たしていれば追加勝利
    public bool CheckWin(ref CustomRoles winnerRole)
        => additionalWin && IsWinConditionMet();

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!visualize || seer != Player || seen == seer) return "";
        return infected.Contains(seen.PlayerId) ? Utils.ColorString(RoleInfo.RoleColor, "◆") : "";
    }

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(remainingInfects > 0 ? RoleInfo.RoleColor : UnityEngine.Color.gray, $"({remainingInfects})");
}
