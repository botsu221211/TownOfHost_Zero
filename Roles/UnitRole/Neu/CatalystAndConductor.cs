using AmongUs.GameOptions;
using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Unit;

/// <summary>
/// カタリスト＆コンダクター (TownOfHost-hamo より移植)
/// 必ず二人一組でアサインされる第三陣営のユニット。設定はここにまとめて置く。
/// 役職の中身は Roles/Neutral/Hamo/Catalyst.cs
/// </summary>
public sealed class CatalystAndConductor : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(CatalystAndConductor),
            player => new CatalystAndConductor(player),
            CustomRoles.CatalystAndConductor,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Unit,
            (int)Options.offsetId.UnitNeu + 100,
            SetupOptionItem,
            "カタリスト&コンダクター",
            "#E60049",
            tab: TabGroup.UnitRoles,
            assignInfo: new RoleAssignInfo(CustomRoles.CatalystAndConductor, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = new CustomRoles[2] { CustomRoles.Catalyst, CustomRoles.Conductor }
            }
        );
    public CatalystAndConductor(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }

    public static OptionItem OptionResonanceCooldown;
    public static OptionItem OptionCatalystCanKill;
    public static OptionItem OptionCatalystKillCooldown;
    public static OptionItem OptionCatalystToOpportunist;
    public static OptionItem OptionDissonanceCooldown;
    public static OptionItem OptionDissonanceDuration;
    public static OptionItem OptionConductorCanKill;
    public static OptionItem OptionConductorKillCooldown;
    public static OptionItem OptionConductorToOpportunist;
    enum OptionName
    {
        CatalystResonanceCooldown,
        CatalystCanKill,
        CatalystKillCooldown,
        CatalystToOpportunist,
        ConductorDissonanceCooldown,
        ConductorDissonanceDuration,
        ConductorCanKill,
        ConductorKillCooldown,
        ConductorToOpportunist,
    }

    private static void SetupOptionItem()
    {
        // カタリストの設定
        OptionResonanceCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.CatalystResonanceCooldown, new(2.5f, 100f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCatalystCanKill = BooleanOptionItem.Create(RoleInfo, 11, OptionName.CatalystCanKill, false, false);
        OptionCatalystKillCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.CatalystKillCooldown, new(2.5f, 100f, 2.5f), 30f, false, OptionCatalystCanKill)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCatalystToOpportunist = BooleanOptionItem.Create(RoleInfo, 13, OptionName.CatalystToOpportunist, false, false);
        // コンダクターの設定
        OptionDissonanceCooldown = FloatOptionItem.Create(RoleInfo, 14, OptionName.ConductorDissonanceCooldown, new(2.5f, 100f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDissonanceDuration = FloatOptionItem.Create(RoleInfo, 15, OptionName.ConductorDissonanceDuration, new(2.5f, 60f, 2.5f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionConductorCanKill = BooleanOptionItem.Create(RoleInfo, 16, OptionName.ConductorCanKill, false, false);
        OptionConductorKillCooldown = FloatOptionItem.Create(RoleInfo, 17, OptionName.ConductorKillCooldown, new(2.5f, 100f, 2.5f), 30f, false, OptionConductorCanKill)
            .SetValueFormat(OptionFormat.Seconds);
        OptionConductorToOpportunist = BooleanOptionItem.Create(RoleInfo, 18, OptionName.ConductorToOpportunist, false, false);
    }
}
