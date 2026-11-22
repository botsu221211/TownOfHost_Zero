using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Impostor;

/// <summary>
/// フレンジー (TOH_Zero)
/// 視界が狭い代わりに、短いキルクールを持つインポスター。味方のインポスターもキルできる。
/// 味方をキルできるよう、本人の画面でだけ他の人をクルーに見せる(isDesyncImpostor)。
/// 仲間のインポスターとは、名前の色で今まで通りお互いを確認できる。
/// </summary>
public sealed class Frenzy : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Frenzy),
            player => new Frenzy(player),
            CustomRoles.Frenzy,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 2500,
            SetupOptionItem,
            "フレンジー",
            isDesyncImpostor: true
        );
    public Frenzy(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        killCooldown = OptionKillCooldown.GetFloat();
        vision = OptionVision.GetFloat();
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionVision;
    enum OptionName
    {
        FrenzyVision,
    }
    private static float killCooldown;
    private static float vision;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 17.5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVision = FloatOptionItem.Create(RoleInfo, 11, OptionName.FrenzyVision, new(0.1f, 5f, 0.05f), 0.25f, false)
            .SetValueFormat(OptionFormat.Multiplier);
    }

    public float CalculateKillCooldown() => killCooldown;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // 視界は設定の倍率に固定(停電の影響を受けないインポスターの視界として扱う)
        opt.SetVision(true);
        opt.SetFloat(FloatOptionNames.ImpostorLightMod, vision);
        opt.SetFloat(FloatOptionNames.CrewLightMod, vision);
    }
}
