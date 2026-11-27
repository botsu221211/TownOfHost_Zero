using System.Linq;
using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// ニート / NEET (TOH_Zero・ExtremeRoles のニートを移植)
/// ・何もできない第三陣営。タスクを持たず、カメラ・アドミン・バイタル・ドアログ・ベントが使えない
/// ・設定で、緊急会議を開けない・サボタージュを直せない
/// ・ゲーム終了時に生きていれば追加勝利。設定で単独勝利にもできる(勝利優先度で比べる)
/// </summary>
public sealed class Neet : RoleBase, IAdditionalWinner, ISystemTypeUpdateHook
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Neet),
            player => new Neet(player),
            CustomRoles.Neet,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1800,
            SetupOptionItem,
            "ニート",
            "#9e9e9e"
        );
    public Neet(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    { }

    private static OptionItem OptionNoEmergency;
    private static OptionItem OptionNoRepair;
    private static OptionItem OptionSoloWin;
    private static OptionItem OptionWinPriority;
    enum OptionName
    {
        NeetNoEmergency,
        NeetNoRepair,
        NeetSoloWin,
        NeetWinPriority,
    }

    private static void SetupOptionItem()
    {
        OptionNoEmergency = BooleanOptionItem.Create(RoleInfo, 10, OptionName.NeetNoEmergency, true, false);
        OptionNoRepair = BooleanOptionItem.Create(RoleInfo, 11, OptionName.NeetNoRepair, true, false);
        OptionSoloWin = BooleanOptionItem.Create(RoleInfo, 12, OptionName.NeetSoloWin, false, false);
        OptionWinPriority = IntegerOptionItem.Create(RoleInfo, 13, OptionName.NeetWinPriority, new(0, 100, 1), 10, false, OptionSoloWin);
    }

    /// <summary>緊急会議を開けないニートか</summary>
    public static bool CannotCallEmergency(PlayerControl pc)
        => pc != null && pc.Is(CustomRoles.Neet) && OptionNoEmergency.GetBool();

    // ───── サボタージュを直せない ─────
    private bool CanRepair => !OptionNoRepair.GetBool();
    bool ISystemTypeUpdateHook.UpdateReactorSystem(ReactorSystemType reactorSystem, byte amount) => CanRepair;
    bool ISystemTypeUpdateHook.UpdateHeliSabotageSystem(HeliSabotageSystem heliSabotageSystem, byte amount) => CanRepair;
    bool ISystemTypeUpdateHook.UpdateLifeSuppSystem(LifeSuppSystemType lifeSuppSystem, byte amount) => CanRepair;
    bool ISystemTypeUpdateHook.UpdateHudOverrideSystem(HudOverrideSystemType hudOverrideSystem, byte amount) => CanRepair;
    bool ISystemTypeUpdateHook.UpdateHqHudSystem(HqHudSystemType hqHudSystemType, byte amount) => CanRepair;
    bool ISystemTypeUpdateHook.UpdateSwitchSystem(SwitchSystem switchSystem, byte amount) => CanRepair;

    // ───── 勝利 ─────
    /// <summary>生きていれば追加勝利(単独勝利の設定がオフの時)</summary>
    public bool CheckWin(ref CustomRoles winnerRole)
        => !OptionSoloWin.GetBool() && Player.IsAlive();

    /// <summary>
    /// ほかの勝利でゲームが終わる時に呼ぶ。単独勝利の設定がオンで生きているニートがいれば、
    /// 勝利優先度を比べて(通常の陣営勝利は 0)ニートの単独勝利に置き換える
    /// </summary>
    public static bool CheckWinOnGameEnd(CustomWinner currentWinner)
    {
        if (OptionSoloWin == null || !OptionSoloWin.GetBool()) return false;
        if (currentWinner is CustomWinner.Neet or CustomWinner.Draw or CustomWinner.None) return false;
        var ids = CustomRoleManager.AllActiveRoles.Values.OfType<Neet>()
            .Where(r => r.Player != null && r.Player.IsAlive())
            .Select(r => r.Player.PlayerId).ToList();
        if (ids.Count == 0) return false;
        if (!CustomWinnerHolder.SetPriorityWinner(CustomWinner.Neet, OptionWinPriority.GetInt(), ids)) return false;
        Logger.Info($"{currentWinner} の勝利の時にニートが生きていたので、優先度 {OptionWinPriority.GetInt()} のニートが単独勝利", nameof(Neet));
        return true;
    }
}
