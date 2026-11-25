using TownOfHostY.Roles.Core;

namespace TownOfHostY.Modules;

/// <summary>
/// バニラ役 (TOH_Zero)。ネタ役と同じく、プリセット画面のボタンで表示/非表示を切り替える(プリセットごとに保存)。
/// ・非表示: 設定画面に出ず、出現率が入っていても抽選されない。闇鍋/属性入り闇鍋でもオンにならない
/// ・表示: 今まで通り(普通の役職と同じ扱い)。初期値は表示
/// </summary>
public static class VanillaRoles
{
    /// <summary>表示/非表示の保存先(画面には出さない)</summary>
    private static OptionItem VisibleOption;

    public static void SetupCustomOption(int id)
    {
        VisibleOption = BooleanOptionItem.Create(id, "VanillaRolesVisible", true, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All)
            .SetHidden(true);
    }

    public static bool Visible => VisibleOption?.GetBool() != false;

    /// <summary>バニラ(本編)の役職を MOD の役職として出すもの(エンジニア・科学者・シェイプシフターなど)</summary>
    public static bool IsVanilla(CustomRoles role) => role.IsNormalVanillaRole();

    /// <summary>今は使えないバニラ役か(非表示の時のバニラ役)</summary>
    public static bool IsDisabled(CustomRoles role) => !Visible && IsVanilla(role);

    public static void SetVisible(bool visible)
    {
        if (VisibleOption == null) return;
        VisibleOption.SetValue(visible ? 1 : 0);
    }
}
