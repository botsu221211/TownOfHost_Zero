using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Unit;

namespace TownOfHostY.Roles.Madmate;

/// <summary>
/// コーリング (TOH_Zero) / シグナル＆コーリングのマッドメイト側
/// ・会議中に自投票すると交換要請。次のターンからシグナルと役職が入れ替わる
/// </summary>
public sealed class Calling : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Calling),
            player => new Calling(player),
            CustomRoles.Calling,
            () => BaseRole,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.UnitMix + 100,//使用しない
            null,
            "コーリング",
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );
    public Calling(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }

    /// <summary>ベントが使える設定ならエンジニア、使えないならクルー</summary>
    public static RoleTypes BaseRole => SignalAndCalling.CanVent ? RoleTypes.Engineer : RoleTypes.Crewmate;

    public override void Add()
    {
        SignalAndCalling.CallingId = Player.PlayerId;
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        if (SignalAndCalling.CanVent)
        {
            AURoleOptions.EngineerCooldown = SignalAndCalling.OptionVentCooldown.GetFloat();
            AURoleOptions.EngineerInVentMaxTime = SignalAndCalling.OptionVentMaxTime.GetFloat();
        }
        if (!SignalAndCalling.UseMadmateSettings)
        {
            // マッドメイト共通の設定(インポスターの視界・投票が見える)を使わない
            opt.SetVision(false);
            opt.SetBool(BoolOptionNames.AnonymousVotes, Main.RealOptionsData.GetBool(BoolOptionNames.AnonymousVotes));
        }
    }

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (SignalAndCalling.CanSeeEachOther && SignalAndCalling.IsPartner(Player, seen)) enabled = true;
    }

    public override void OnStartMeeting()
    {
        SignalAndCalling.SwapRequested = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        var signal = SignalAndCalling.SignalPlayer;
        if (signal == null || !signal.IsAlive() || !signal.Is(CustomRoles.Signal)) return;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsMeeting || Player == null || !Player.IsAlive()) return;
            Utils.SendMessageAutoSplit("自投票すると交換要請。次のターンからシグナル(インポスター)と役職が入れ替わります。\n(もう一度自投票すると普通の自投票)", true,
                SignalAndCalling.Title, Player.PlayerId);
        }, 5f, "Calling.Guide");
    }

    public override bool CheckVoteAsVoter(PlayerControl votedFor)
    {
        if (!Player.IsAlive() || votedFor == null || votedFor.PlayerId != Player.PlayerId) return true;
        if (SignalAndCalling.SwapRequested) return true; // もう出している: 普通の自投票
        var signal = SignalAndCalling.SignalPlayer;
        if (signal == null || !signal.IsAlive() || !signal.Is(CustomRoles.Signal))
        {
            Utils.SendMessageAutoSplit("シグナルが生きていないので、交換要請は使えません。", true, SignalAndCalling.Title, Player.PlayerId);
            return true;
        }
        SignalAndCalling.SwapRequested = true;
        Logger.Info($"{Player.GetNameWithRole()} が交換要請を出した", nameof(Calling));
        Utils.SendMessageAutoSplit("交換要請を出しました。次のターンからシグナルになります。\n続けて普通の投票をしてください。", true, SignalAndCalling.Title, Player.PlayerId);
        Utils.SendMessageAutoSplit("コーリングが交換要請を出しました。次のターンからあなたはコーリングになります。", true, SignalAndCalling.Title, signal.PlayerId);
        return false;
    }
}
