using AmongUs.GameOptions;

using TownOfHostY.Modules;
using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Crewmate;
public sealed class Chairman : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Chairman),
            player => new Chairman(player),
            CustomRoles.Chairman,
            () => RoleTypes.Crewmate, // ボタンはベントではなくペットで使う
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 300,
            SetupOptionItem,
            "チェアマン",
            "#204d42",
            introSound: () => GetIntroSound(RoleTypes.Crewmate)
        );
    public Chairman(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        NumOfUseButton = OptionNumOfUseButton.GetInt();
        IgnoreSkip = OptionIgnoreSkip.GetBool();

        LeftButtonCount = NumOfUseButton;
    }

    private static OptionItem OptionNumOfUseButton;
    private static OptionItem OptionIgnoreSkip;
    enum OptionName
    {
        MayorNumOfUseButton,
        ChairmanIgnoreSkip,
    }
    public static int NumOfUseButton;
    public static bool IgnoreSkip;

    public int LeftButtonCount;
    private static void SetupOptionItem()
    {
        OptionNumOfUseButton = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MayorNumOfUseButton, new(1, 20, 1), 2, false)
            .SetValueFormat(OptionFormat.Times);
        OptionIgnoreSkip = BooleanOptionItem.Create(RoleInfo, 11, OptionName.ChairmanIgnoreSkip, false, false);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        Logger.Warn($"{LeftButtonCount} <= 0", "Mayor.ApplyGameOptions");
        AURoleOptions.EngineerCooldown =
            LeftButtonCount <= 0
            ? 255f
            : opt.GetInt(Int32OptionNames.EmergencyCooldown);
        AURoleOptions.EngineerInVentMaxTime = 1;
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (reporter == Player && target == null) //ボタン
            LeftButtonCount--;
    }
    // ===== ボタン: ペットを撫でると緊急会議(元はベント) =====
    public override bool UsePetAbility => true;
    public override float PetCooldown => LeftButtonCount <= 0 ? PetAbility.Unusable : Main.NormalOptions.EmergencyCooldown;
    public override void OnPet()
    {
        if (LeftButtonCount > 0) Player.ReportDeadBody(null);
    }

    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        // 既定値
        var (votedForId, numVotes, doVote) = base.ModifyVote(voterId, sourceVotedForId, isIntentional);
        var baseVote = (votedForId, numVotes, doVote);
        if (IgnoreSkip || voterId != Player.PlayerId || sourceVotedForId == Player.PlayerId || sourceVotedForId >= 253 || !Player.IsAlive())
        {
            return baseVote;
        }
        MeetingVoteManager.Instance.ClearAndExile(Player.PlayerId, 253);
        return (votedForId, numVotes, false);
    }

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        if (!isForMeeting || !Player.IsAlive() || IgnoreSkip) return string.Empty;

        //seenが省略の場合seer
        seen ??= seer;
        //seeおよびseenが自分である場合以外は関係なし
        if (!Is(seer) || !Is(seen)) return "";

        return Translator.GetString("ChairmanVote").Color(RoleInfo.RoleColor);
    }

    public override void AfterMeetingTasks()=> Player.RpcResetAbilityCooldown();
}