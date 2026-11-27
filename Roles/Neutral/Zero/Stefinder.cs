using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// ステファインダー / Stefinder (TOH_Zero・SuperNewRoles から移植)
/// ・1回だけキルできる。サボタージュは使えない(ベントは設定)
/// ・キルしていない間は自分の名前がライム色、キルすると赤になる(インポスターとは互いに分からない)
/// ・キルしていない時はクルーの勝利を、キルした時はインポスターの勝利を乗っ取る(生きていれば)
/// ・勝利優先度で比べる。設定で追加勝利にもできる
/// </summary>
public sealed class Stefinder : RoleBase, IKiller, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Stefinder),
            player => new Stefinder(player),
            CustomRoles.Stefinder,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1900,
            SetupOptionItem,
            "ステファインダー",
            "#7fff00",
            isDesyncImpostor: true
        );
    public Stefinder(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        killed = false;
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionCanVent;
    private static OptionItem OptionWinPriority;
    private static OptionItem OptionAdditionalWin;
    enum OptionName
    {
        StefinderCanVent,
        StefinderWinPriority,
        StefinderAdditionalWin,
    }

    /// <summary>キルしたか</summary>
    private bool killed;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0.5f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 11, OptionName.StefinderCanVent, false, false);
        OptionWinPriority = IntegerOptionItem.Create(RoleInfo, 12, OptionName.StefinderWinPriority, new(0, 50, 1), 30, false);
        OptionAdditionalWin = BooleanOptionItem.Create(RoleInfo, 13, OptionName.StefinderAdditionalWin, false, false);
    }

    // ───── キル(1回だけ) ─────
    public bool CanUseKillButton() => Player.IsAlive() && !killed;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => OptionCanVent.GetBool();
    public float CalculateKillCooldown() => killed ? 255f : OptionKillCooldown.GetFloat();

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsSuicide) return;
        if (killed) info.DoKill = false;
    }
    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (info.IsSuicide || killed) return;
        killed = true;
        Logger.Info($"{Player.GetNameWithRole()} : 1回キルしたので、インポスターの勝利を乗っ取る側になった", nameof(Stefinder));
        Player.ResetKillCooldown();
        Player.MarkDirtySettings();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    // ───── 自分の名前の色: キル前はライム、キル後は赤 ─────
    public override void OverrideShowMainRoleText(ref Color roleColor, ref string roleText)
    {
        base.OverrideShowMainRoleText(ref roleColor, ref roleText);
        if (killed) roleColor = Palette.ImpostorRed;
    }

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(killed ? Palette.ImpostorRed : RoleInfo.RoleColor, killed ? "(済)" : "(1)");

    // ───── 勝利 ─────
    /// <summary>今の勝者を乗っ取れるか(キル前はクルー、キル後はインポスター)</summary>
    private bool CanHijack(CustomWinner winner)
        => Player.IsAlive() && (killed ? winner == CustomWinner.Impostor : winner == CustomWinner.Crewmate);

    /// <summary>追加勝利にする がオンの時: 乗っ取れる勝利なら追加勝利</summary>
    public bool CheckWin(ref CustomRoles winnerRole)
        => OptionAdditionalWin.GetBool() && CanHijack(CustomWinnerHolder.WinnerTeam);

    /// <summary>
    /// ほかの勝利でゲームが終わる時に呼ぶ。乗っ取れる勝利なら、勝利優先度を比べて(通常の陣営勝利は 0)
    /// ステファインダーの単独勝利に置き換える(追加勝利にする がオフの時)
    /// </summary>
    public static bool CheckWinOnGameEnd(CustomWinner currentWinner)
    {
        if (OptionAdditionalWin == null || OptionAdditionalWin.GetBool()) return false;
        var ids = CustomRoleManager.AllActiveRoles.Values.OfType<Stefinder>()
            .Where(r => r.CanHijack(currentWinner))
            .Select(r => r.Player.PlayerId).ToList();
        if (ids.Count == 0) return false;
        if (!CustomWinnerHolder.SetPriorityWinner(CustomWinner.Stefinder, OptionWinPriority.GetInt(), ids)) return false;
        Logger.Info($"{currentWinner} の勝利をステファインダーが乗っ取った (優先度 {OptionWinPriority.GetInt()})", nameof(Stefinder));
        return true;
    }
}
