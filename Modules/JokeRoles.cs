using System.Collections.Generic;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Modules;

/// <summary>
/// ネタ役 (TOH_Zero)
/// プリセット画面のボタンで表示/非表示を切り替える(プリセットごとに保存)。
/// ・非表示: 設定画面に出ず、出現率が入っていても抽選されない。闇鍋/属性入り闇鍋でもオンにならない
/// ・表示: 普通の役職と同じ扱い。闇鍋/属性入り闇鍋でもオンになる
/// 新しくネタ役を作ったら Roles に追加する
/// </summary>
public static class JokeRoles
{
    public static readonly HashSet<CustomRoles> Roles = new()
    {
        CustomRoles.NoLimiter,
        CustomRoles.FirstVictim,
        CustomRoles.ChainShifter,
        CustomRoles.Vampire,
    };

    /// <summary>表示/非表示の保存先(画面には出さない)</summary>
    private static OptionItem VisibleOption;

    public static void SetupCustomOption(int id)
    {
        VisibleOption = BooleanOptionItem.Create(id, "JokeRolesVisible", false, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All)
            .SetHidden(true);
    }

    public static bool Visible => VisibleOption?.GetBool() == true;

    public static bool IsJoke(CustomRoles role) => Roles.Contains(role);

    /// <summary>今は使えないネタ役か(非表示の時のネタ役)</summary>
    public static bool IsDisabled(CustomRoles role) => !Visible && IsJoke(role);

    public static void SetVisible(bool visible)
    {
        if (VisibleOption == null) return;
        VisibleOption.SetValue(visible ? 1 : 0);
    }
}
