namespace TownOfHostY.Roles.Core.Interfaces;

/// <summary>
/// 覚醒役職 (TOH_Zero)。覚醒するまでは自分でも役職を自覚できず、自分の画面では「クルーメイト(?)」に見える。
/// 新しく覚醒役職を作る時は、このインターフェースを付けて IsAwakened を返すだけでよい
/// (名前の上の役職名・イントロ・役職説明は AwakeningRole が書き換える)
/// </summary>
public interface IAwakeningRole
{
    /// <summary>覚醒したか</summary>
    bool IsAwakened { get; }
}

public static class AwakeningRole
{
    /// <summary>覚醒前に見せる役職名の翻訳キー(設定で ? を付けるなら「クルーメイト(?)」、付けないなら「クルーメイト」)</summary>
    public static string UnawareNameKey => Options.AwakeningRoleMark?.GetBool() != false ? "UnawareCrewmate" : "Crewmate";
    public static string UnawareName => Translator.GetString(UnawareNameKey);

    /// <summary>覚醒前の覚醒役職か(試合が終わった後は本当の役職を見せるので false)</summary>
    public static bool IsUnaware(PlayerControl pc)
        => pc != null
            && pc.GetRoleClass() is IAwakeningRole role && !role.IsAwakened
            && GameStates.IsInGame && CustomWinnerHolder.WinnerTeam == CustomWinner.Default;

    /// <summary>自分の画面で見せる役職(覚醒前はクルーメイト)</summary>
    public static CustomRoles ShownRole(PlayerControl pc)
        => IsUnaware(pc) ? CustomRoles.Crewmate : pc.GetCustomRole();
}
