using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Modules;

/// <summary>
/// ゲームマスター(GM)の詳細設定 (TOH_Zero・TownOfHost-hamo を参考)
/// 「ゲームマスター」がオンの時だけ、その下に表示される。
/// GM は霊界にいる扱いだが、見えるものは霊界の設定ではなくここの設定で決める。
/// </summary>
public static class GMSettings
{
    public static OptionItem AutoChat;
    public static OptionItem AutoPossess;
    public static OptionItem AutoPossessPreferImpostor;
    public static OptionItem HideTaskPanel;
    public static OptionItem ShowOverallTaskProgress;
    public static OptionItem CanSeeRoles;
    public static OptionItem CanSeeTaskProgress;
    public static OptionItem CanSeeVotes;
    public static OptionItem CanSeeDeathReason;
    public static OptionItem CanSeeKillFlash;

    public static void SetupCustomOption(OptionItem enableGM)
    {
        var id = (int)Options.offsetId.GM;
        AutoChat = Create(id + 10, "GMAutoChat", false, enableGM);
        AutoPossess = Create(id + 20, "GMAutoPossess", false, enableGM);
        AutoPossessPreferImpostor = Create(id + 21, "GMAutoPossessPreferImpostor", false, AutoPossess);
        HideTaskPanel = Create(id + 30, "GMHideTaskPanel", false, enableGM);
        ShowOverallTaskProgress = Create(id + 31, "GMShowOverallTaskProgress", true, enableGM);
        CanSeeRoles = Create(id + 40, "GMCanSeeRoles", true, enableGM);
        CanSeeTaskProgress = Create(id + 41, "GMCanSeeTaskProgress", true, enableGM);
        CanSeeVotes = Create(id + 42, "GMCanSeeVotes", true, enableGM);
        CanSeeDeathReason = Create(id + 43, "GMCanSeeDeathReason", true, enableGM);
        CanSeeKillFlash = Create(id + 44, "GMCanSeeKillFlash", true, enableGM);
    }
    private static OptionItem Create(int id, string name, bool defaultValue, OptionItem parent)
        => BooleanOptionItem.Create(id, name, defaultValue, TabGroup.ModMainSettings, false)
            .SetParent(parent)
            .SetColor(new Color32(255, 91, 112, 255))
            .SetGameMode(CustomGameMode.All);

    public static bool IsGM(PlayerControl pc) => pc != null && pc.Is(CustomRoles.GM);
    public static bool IsLocalGM => IsGM(PlayerControl.LocalPlayer);
}
