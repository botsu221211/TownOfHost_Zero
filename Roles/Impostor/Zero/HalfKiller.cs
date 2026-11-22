using System.Collections.Generic;
using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Impostor;

/// <summary>
/// ハーフキラー (TOH_Zero)
/// キルクールが短い代わりに、同じ相手を2回キルしないと倒せない。
/// 1回目のキルでは相手は死なず、自分にだけ見える印(◆)が付く。印の付いた相手をキルすると死亡する。
/// </summary>
public sealed class HalfKiller : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(HalfKiller),
            player => new HalfKiller(player),
            CustomRoles.HalfKiller,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 2400,
            SetupOptionItem,
            "ハーフキラー"
        );
    public HalfKiller(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        killCooldown = OptionKillCooldown.GetFloat();
        marked = new();
    }

    private static OptionItem OptionKillCooldown;
    private static float killCooldown;

    /// <summary>1回目のキルで印を付けた相手</summary>
    private HashSet<byte> marked;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 90f, 0.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public float CalculateKillCooldown() => killCooldown;

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!info.CanKill) return;
        var (killer, target) = info.AttemptTuple;

        // 印の付いた相手なら、そのままキルする
        if (marked.Remove(target.PlayerId))
        {
            Logger.Info($"{killer.GetNameWithRole()} → {target.GetNameWithRole()}: 2回目のキル", nameof(HalfKiller));
            return;
        }

        // 1回目: キルせずに印を付ける
        info.DoKill = false;
        marked.Add(target.PlayerId);
        Logger.Info($"{killer.GetNameWithRole()} → {target.GetNameWithRole()}: 1回目なので印を付ける", nameof(HalfKiller));
        Player.SetKillCooldown();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    /// <summary>印は自分にだけ見える</summary>
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer != Player || !marked.Contains(seen.PlayerId)) return "";
        return Utils.ColorString(Palette.ImpostorRed, "◆");
    }
}
