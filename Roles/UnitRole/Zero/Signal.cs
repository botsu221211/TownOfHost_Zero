using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using TownOfHostY.Roles.Unit;

namespace TownOfHostY.Roles.Impostor;

/// <summary>
/// シグナル (TOH_Zero) / シグナル＆コーリングのインポスター側
/// ・普通のインポスター。コーリングが交換要請を出すと、次のターンからコーリングになる
/// </summary>
public sealed class Signal : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Signal),
            player => new Signal(player),
            CustomRoles.Signal,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.UnitMix + 100,//使用しない
            null,
            "シグナル"
        );
    public Signal(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }

    public override void Add()
    {
        if (!SignalAndCalling.Changing) SignalAndCalling.ResetForNewGame();
        SignalAndCalling.SignalId = Player.PlayerId;
    }

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (SignalAndCalling.CanSeeEachOther && SignalAndCalling.IsPartner(Player, seen)) enabled = true;
    }

    public override void OnStartMeeting()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        var calling = SignalAndCalling.CallingPlayer;
        if (calling == null || !calling.IsAlive() || !calling.Is(CustomRoles.Calling)) return;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsMeeting || Player == null || !Player.IsAlive()) return;
            Utils.SendMessageAutoSplit("コーリングが自投票で交換要請を出すと、次のターンからあなたとコーリングの役職が入れ替わります。", true,
                SignalAndCalling.Title, Player.PlayerId);
        }, 5f, "Signal.Guide");
    }
}
