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

    /// <summary>次の会議で知らせる「捕食された」(通常のフライドポテト / マッドフライドポテト)</summary>
    private static readonly List<string> pendingNotices = new();

    private static void SetupOptionItem()
    {
        OptionHideNameInWhisper = BooleanOptionItem.Create(RoleInfo, 10, OptionName.FriedPotatoHideNameInWhisper, true, false);
        OptionNotifyEaten = BooleanOptionItem.Create(RoleInfo, 11, OptionName.FriedPotatoNotifyEaten, true, false);
    }

    [GameModuleInitializer]
    public static void Initialize() => pendingNotices.Clear();

    /// <summary>
    /// 個人秘匿で、この人の名前を「フライドポテト」にするか。
    /// マッドフライドポテトも同じ名前・同じ色で届く(見分けがつかない。設定は無く、いつもフライドポテトになる)
    /// </summary>
    public static bool HidesNameInWhisper(PlayerControl player)
        => player != null
        && ((player.Is(CustomRoles.FriedPotato) && OptionHideNameInWhisper.GetBool())
            || player.Is(CustomRoles.MadFriedPotato));

    /// <summary>個人秘匿の送り主として表示する名前(役職の色)</summary>
    public static string WhisperName => Utils.ColorString(RoleInfo.RoleColor, GetString("FriedPotato"));

    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost || !OptionNotifyEaten.GetBool()) return;
        // 自殺など、誰かにキルされたのでなければ知らせない
        if (info.AttemptKiller == null || info.AttemptKiller.PlayerId == Player.PlayerId) return;
        pendingNotices.Add(string.Format(GetString("FriedPotatoEatenNotice"), WhisperName));
        Logger.Info($"{Player.GetNameWithRole()} が捕食された(次の会議で通知)", nameof(FriedPotato));
    }

    /// <summary>マッドフライドポテトが捕食された時(通常のフライドポテトとは違うお知らせ)</summary>
    public static void AddMadEatenNotice()
        => pendingNotices.Add(string.Format(GetString("MadFriedPotatoEatenNotice"), WhisperName));

    /// <summary>会議の始めに出すお知らせを渡して、控えを空にする</summary>
    public static IEnumerable<string> TakeMeetingNotices()
    {
        var list = new List<string>(pendingNotices);
        pendingNotices.Clear();
        return list;
    }
}

/// <summary>
/// マッドフライドポテト (TOH_Zero)
/// ・インポスター陣営(マッドメイト)。判定・カウントはクルー
/// ・フライドポテトと同じく、個人秘匿(/c)で送ると名前が「フライドポテト」で届く(見分ける方法は無い)
/// ・キルされると、次の会議で通常のフライドポテトとは違うお知らせが出る
/// ・設定でベントが使える(クールダウン・滞在時間はマッドメイト共通の設定)
/// </summary>
public sealed class MadFriedPotato : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadFriedPotato),
            player => new MadFriedPotato(player),
            CustomRoles.MadFriedPotato,
            () => OptionCanVent?.GetBool() == true ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadY + 1100,
            SetupOptionItem,
            "マッドフライドポテト"
        );
    public MadFriedPotato(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    { }

    private static OptionItem OptionCanVent;
    enum OptionName
    {
        MadFriedPotatoCanVent,
    }
    private static void SetupOptionItem()
    {
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 10, OptionName.MadFriedPotatoCanVent, false, false);
    }

    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // 自殺など、誰かにキルされたのでなければ知らせない
        if (info.AttemptKiller == null || info.AttemptKiller.PlayerId == Player.PlayerId) return;
        FriedPotato.AddMadEatenNotice();
        Logger.Info($"{Player.GetNameWithRole()} が捕食された(次の会議で通知)", nameof(MadFriedPotato));
    }
}
