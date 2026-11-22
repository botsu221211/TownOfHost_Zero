using System.Collections.Generic;
using AmongUs.GameOptions;

using TownOfHostY.Attributes;
using TownOfHostY.Roles.Core;
using static TownOfHostY.Translator;

namespace TownOfHostY.Roles.Crewmate;

/// <summary>
/// フライドポテト (TOH_Zero)
/// ・個人秘匿(/c)で送ると、送り主の名前が「フライドポテト」になる
/// ・キルされると、次の会議で「フライドポテトが捕食された」と全員に通知される
/// </summary>
public sealed class FriedPotato : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(FriedPotato),
            player => new FriedPotato(player),
            CustomRoles.FriedPotato,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 2000,
            SetupOptionItem,
            "フライドポテト",
            "#e8b84a"
        );
    public FriedPotato(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }

    private static OptionItem OptionHideNameInWhisper;
    private static OptionItem OptionNotifyEaten;
    enum OptionName
    {
        FriedPotatoHideNameInWhisper,
        FriedPotatoNotifyEaten,
    }

    /// <summary>次の会議で知らせる「捕食された」の件数</summary>
    private static int pendingEatenNotices;

    private static void SetupOptionItem()
    {
        OptionHideNameInWhisper = BooleanOptionItem.Create(RoleInfo, 10, OptionName.FriedPotatoHideNameInWhisper, true, false);
        OptionNotifyEaten = BooleanOptionItem.Create(RoleInfo, 11, OptionName.FriedPotatoNotifyEaten, true, false);
    }

    [GameModuleInitializer]
    public static void Initialize() => pendingEatenNotices = 0;

    /// <summary>個人秘匿で、この人の名前を「フライドポテト」にするか</summary>
    public static bool HidesNameInWhisper(PlayerControl player)
        => player != null && player.Is(CustomRoles.FriedPotato) && OptionHideNameInWhisper.GetBool();

    /// <summary>個人秘匿の送り主として表示する名前(役職の色)</summary>
    public static string WhisperName => Utils.ColorString(RoleInfo.RoleColor, GetString("FriedPotato"));

    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost || !OptionNotifyEaten.GetBool()) return;
        // 自殺など、誰かにキルされたのでなければ知らせない
        if (info.AttemptKiller == null || info.AttemptKiller.PlayerId == Player.PlayerId) return;
        pendingEatenNotices++;
        Logger.Info($"{Player.GetNameWithRole()} が捕食された(次の会議で通知)", nameof(FriedPotato));
    }

    /// <summary>会議の始めに出すお知らせを渡して、控えを空にする</summary>
    public static IEnumerable<string> TakeMeetingNotices()
    {
        var list = new List<string>();
        for (int i = 0; i < pendingEatenNotices; i++)
            list.Add(string.Format(GetString("FriedPotatoEatenNotice"), WhisperName));
        pendingEatenNotices = 0;
        return list;
    }
}
