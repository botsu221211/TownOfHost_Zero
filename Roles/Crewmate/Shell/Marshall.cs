using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHostY.Modules;
using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Crewmate;

/// <summary>
/// マーシャル (TownOfHost-Shell より移植)
/// ・緊急会議の回数が増える
/// ・最多票が同数の時、マーシャルが投票していた候補(スキップを含む)を優先する。マーシャル同士で分かれたら通常の同数処理
/// ・会議中に起きた死亡について、名前・役職・死因を本人だけが確認できる
/// ・戒厳令: 自分に投票してから別の生存者を選ぶと、その会議の間その人の投票力を 1 減らす
///   (自投票かスキップで選択を解除。同じ人への戒厳令は全マーシャル合計で会議ごとに 1 回)
/// </summary>
public sealed class Marshall : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Marshall),
            player => new Marshall(player),
            CustomRoles.Marshall,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 2200,
            SetupOptionItem,
            "マーシャル",
            "#6EFFC7"
        );
    public Marshall(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        totalUsed = 0;
        meetingUsed = 0;
        selecting = false;
        if (!handlerAdded)
        {
            handlerAdded = true;
            CustomRoleManager.OnMurderPlayerOthers.Add(OnMurderPlayerOthers);
        }
    }
    public override void OnDestroy()
    {
        if (CustomRoleManager.AllActiveRoles.Values.OfType<Marshall>().Any(m => m != this)) return;
        CustomRoleManager.OnMurderPlayerOthers.Remove(OnMurderPlayerOthers);
        handlerAdded = false;
        Suppressed.Clear();
        AliveAtMeetingStart.Clear();
    }

    private static OptionItem OptionAdditionalButtons;
    private static OptionItem OptionTotalUses;
    private static OptionItem OptionMeetingUses;
    enum OptionName
    {
        MarshallAdditionalButtons,
        MarshallTotalUses,
        MarshallMeetingUses,
    }

    private static bool handlerAdded;
    /// <summary>この会議で戒厳令を受けている人 (全マーシャル共通)</summary>
    private static readonly HashSet<byte> Suppressed = new();
    /// <summary>会議が始まった時に生きていた人(会議中の死亡の報告用)</summary>
    private static readonly HashSet<byte> AliveAtMeetingStart = new();

    private int totalUsed;
    private int meetingUsed;
    private bool selecting;

    private static void SetupOptionItem()
    {
        OptionAdditionalButtons = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MarshallAdditionalButtons, new(0, 99, 1), 2, false)
            .SetValueFormat(OptionFormat.Times);
        OptionTotalUses = IntegerOptionItem.Create(RoleInfo, 11, OptionName.MarshallTotalUses, new(0, 99, 1), 5, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMeetingUses = IntegerOptionItem.Create(RoleInfo, 12, OptionName.MarshallMeetingUses, new(0, 99, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
    }
    private static int Total => OptionTotalUses.GetInt();
    /// <summary>会議ごとの回数。0 は無制限</summary>
    private static int PerMeeting => OptionMeetingUses.GetInt() == 0 ? int.MaxValue : OptionMeetingUses.GetInt();
    private bool Available => Player.IsAlive() && totalUsed < Total && meetingUsed < PerMeeting;

    private static string Title => "<color=#6EFFC7>【マーシャル】</color>";
    private void Notice(string text)
    {
        if (!Player.IsAlive()) return;
        Utils.SendMessageAutoSplit(text, true, Title, Player.PlayerId);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // 緊急会議の回数を増やす(部屋の設定が 0 回の時は増やさない)
        var baseCount = Main.NormalOptions.NumEmergencyMeetings;
        if (baseCount > 0)
            opt.SetInt(Int32OptionNames.NumEmergencyMeetings, baseCount + OptionAdditionalButtons.GetInt());
    }

    public override void OnStartMeeting()
    {
        meetingUsed = 0;
        selecting = false;
        Suppressed.Clear();
        AliveAtMeetingStart.Clear();
        foreach (var pc in Main.AllAlivePlayerControls) AliveAtMeetingStart.Add(pc.PlayerId);
        if (AmongUsClient.Instance.AmHost && Available)
            Notice("戒厳令: 自分に投票した後、生存者を選ぶと、その人のこの会議での投票力を 1 減らせます。\n能力の操作は投票になりません。操作の後で改めて投票してください。");
    }

    public override bool CheckVoteAsVoter(PlayerControl votedFor)
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return true;
        if (!selecting)
        {
            // 自投票で戒厳令の対象選びを始める
            if (votedFor == null || votedFor.PlayerId != Player.PlayerId || !Available) return true;
            selecting = true;
            Notice("戒厳令の対象にする生存者に投票してください。自投票かスキップで解除します。");
            return false;
        }

        // 対象選び中
        if (votedFor == null || votedFor.PlayerId == Player.PlayerId)
        {
            selecting = false;
            Notice("戒厳令をやめました。改めて投票してください。");
            return false;
        }
        if (!votedFor.IsAlive() || Suppressed.Contains(votedFor.PlayerId))
        {
            Notice("その人には使えません(回数は減っていません)。別の人を選ぶか、自投票かスキップで解除してください。");
            return false;
        }
        Suppressed.Add(votedFor.PlayerId);
        totalUsed++;
        meetingUsed++;
        selecting = false;
        Logger.Info($"{Player.GetNameWithRole()} が {votedFor.GetNameWithRole()} に戒厳令", nameof(Marshall));
        Notice($"{votedFor.Data.PlayerName} に戒厳令を出しました。この会議の間、その人の投票力が 1 減ります。\n続けて通常の投票をしてください。");
        return false;
    }

    /// <summary>票を数える時に呼ぶ。戒厳令を受けている人の票を 1 減らす</summary>
    public static int AdjustPower(byte voter, int numVotes)
        => Suppressed.Contains(voter) ? System.Math.Max(0, numVotes - 1) : numVotes;

    /// <summary>
    /// 最多票が同数の時に呼ぶ。生きているマーシャルの投票先が同数の候補の 1 つにまとまっていれば、それを優先する
    /// </summary>
    public static (bool isTie, NetworkedPlayerInfo exiled) PreferOnTie(bool isTie, NetworkedPlayerInfo exiled, IReadOnlyDictionary<byte, int> votedCounts)
    {
        if (!isTie || votedCounts == null || votedCounts.Count == 0 || MeetingVoteManager.Instance == null) return (isTie, exiled);
        var max = votedCounts.Values.Max();
        if (max <= 0) return (isTie, exiled);
        var tied = votedCounts.Where(p => p.Value == max).Select(p => p.Key).ToHashSet();
        if (tied.Count < 2) return (isTie, exiled);

        var ballots = new HashSet<byte>();
        foreach (var marshall in CustomRoleManager.AllActiveRoles.Values.OfType<Marshall>())
        {
            if (!marshall.Player.IsAlive()) continue;
            if (!MeetingVoteManager.Instance.AllVotes.TryGetValue(marshall.Player.PlayerId, out var vote)) continue;
            if (vote.VotedFor == MeetingVoteManager.NoVote || AdjustPower(vote.Voter, vote.NumVotes) <= 0) continue;
            if (tied.Contains(vote.VotedFor)) ballots.Add(vote.VotedFor);
        }
        if (ballots.Count != 1) return (isTie, exiled);
        var preferred = ballots.First();
        Logger.Info($"同数のため、マーシャルの投票先 {MeetingVoteManager.GetVoteName(preferred)} を優先", nameof(Marshall));
        return preferred == MeetingVoteManager.Skip
            ? (false, null)
            : (false, GameData.Instance.GetPlayerById(preferred));
    }

    /// <summary>会議中に誰かが死んだら、マーシャルにだけ名前・役職・死因を知らせる</summary>
    private static void OnMurderPlayerOthers(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost || !info.IsMeeting) return;
        var target = info.AttemptTarget;
        if (target == null || !AliveAtMeetingStart.Remove(target.PlayerId)) return;
        var name = target.Data?.PlayerName ?? "不明";
        var role = Utils.GetRoleName(target.GetCustomRole());
        var state = PlayerState.GetByPlayerId(target.PlayerId);
        var reason = state != null ? Utils.GetDeathReason(state.DeathReason) : "不明";
        foreach (var marshall in CustomRoleManager.AllActiveRoles.Values.OfType<Marshall>().ToArray())
        {
            if (marshall.Player.PlayerId == target.PlayerId) continue;
            marshall.Notice($"会議中の死亡: {name}\n役職: {role}\n死因: {reason}");
        }
    }

    public override string GetProgressText(bool comms = false)
    {
        var leftTotal = System.Math.Max(0, Total - totalUsed);
        var leftMeeting = OptionMeetingUses.GetInt() == 0 ? "∞" : System.Math.Max(0, PerMeeting - meetingUsed).ToString();
        return Utils.ColorString(RoleInfo.RoleColor, $"({leftTotal}/{leftMeeting})");
    }
}
