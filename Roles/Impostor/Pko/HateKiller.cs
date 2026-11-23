using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHostY.Modules;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Impostor;

/// <summary>
/// ヘイトキラー (TownOfHost-Pko より移植)
/// 会議で自分に投票された票数 1 票につき、次のターンだけキルクールが設定値だけ短くなるインポスター。
/// (最低キルクールより短くはならない。仲間のインポスターの票・自投票を数えるかは設定で決める)
/// </summary>
public sealed class HateKiller : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(HateKiller),
            player => new HateKiller(player),
            CustomRoles.HateKiller,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 2600,
            SetupOptionItem,
            "ヘイトキラー"
        );
    public HateKiller(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        currentKillCooldown = OptionKillCooldown.GetFloat();
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionDecreaseKillCool;
    private static OptionItem OptionMinimumKillCool;
    private static OptionItem OptionCountImpostorVotes;
    private static OptionItem OptionCountSelfVotes;
    private enum OptionName
    {
        HateKillerDecreaseKillCool,
        HateKillerMinimumKillCool,
        HateKillerCountImpostorVotes,
        HateKillerCountSelfVotes,
    }
    private float currentKillCooldown;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDecreaseKillCool = FloatOptionItem.Create(RoleInfo, 11, OptionName.HateKillerDecreaseKillCool, new(0.5f, 180f, 0.5f), 4f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMinimumKillCool = FloatOptionItem.Create(RoleInfo, 12, OptionName.HateKillerMinimumKillCool, new(0.5f, 180f, 0.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCountImpostorVotes = BooleanOptionItem.Create(RoleInfo, 13, OptionName.HateKillerCountImpostorVotes, false, false);
        OptionCountSelfVotes = BooleanOptionItem.Create(RoleInfo, 14, OptionName.HateKillerCountSelfVotes, false, false);
    }

    public float CalculateKillCooldown() => currentKillCooldown;

    /// <summary>会議の終わりに、その会議で自分に入った票でキルクールを決める(MeetingVoteManager.EndMeeting から呼ぶ)</summary>
    public static void OnMeetingEnd(IReadOnlyDictionary<byte, MeetingVoteManager.VoteData> votes)
    {
        if (!AmongUsClient.Instance.AmHost || votes == null) return;
        foreach (var hateKiller in CustomRoleManager.AllActiveRoles.Values.OfType<HateKiller>())
        {
            if (!hateKiller.Player.IsAlive()) continue;
            var me = hateKiller.Player.PlayerId;
            var count = 0;
            foreach (var vote in votes.Values)
            {
                if (vote.VotedFor != me) continue;
                if (PlayerState.GetByPlayerId(vote.Voter)?.IsDead != false) continue;
                var isSelf = vote.Voter == me;
                if (isSelf && !OptionCountSelfVotes.GetBool()) continue;
                if (!isSelf && !OptionCountImpostorVotes.GetBool() && Utils.GetPlayerById(vote.Voter)?.Is(CustomRoleTypes.Impostor) == true) continue;
                count += vote.NumVotes;
            }
            var cooldown = OptionKillCooldown.GetFloat() - count * OptionDecreaseKillCool.GetFloat();
            hateKiller.currentKillCooldown = System.Math.Max(System.Math.Max(cooldown, OptionMinimumKillCool.GetFloat()), 0.1f);
            Logger.Info($"{hateKiller.Player.GetNameWithRole()} は {count} 票入ったので、次のキルクールは {hateKiller.currentKillCooldown} 秒", nameof(HateKiller));
        }
    }
}
