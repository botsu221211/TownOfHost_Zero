using TownOfHostY.Attributes;
using UnityEngine;

namespace TownOfHostY.Modules;

/// <summary>
/// 廃村(引き分けで試合を終わらせる)
/// ・ホストの /end コマンド(Shift+Enter+L と同じ)
/// ・試合中に生きていた人が設定の人数だけ切断したら自動で廃村(MODの設定)
/// 参考: TownOfHost_L の AutoAbort / ChatCommandTable.EndAsDraw
/// </summary>
public static class AutoAbort
{
    public static OptionItem Enabled;
    public static OptionItem Count;

    private static int disconnectedAlive;

    public static void SetupCustomOption(int id)
    {
        Enabled = StringOptionItem.Create(id, "AutoAbortOnDisconnect", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.All);
        Count = IntegerOptionItem.Create(id + 1, "AutoAbortDisconnectCount", new(1, 15, 1), 2, TabGroup.ModMainSettings, false)
            .SetParent(Enabled)
            .SetValueFormat(OptionFormat.Players)
            .SetGameMode(CustomGameMode.All);
    }

    [GameModuleInitializer]
    public static void Initialize() => disconnectedAlive = 0;

    /// <summary>廃村にする。試合中で役職の割り当てが済んでいる時だけ。成功したら true</summary>
    public static bool EndAsDraw(string reason)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInGame || !Main.IsSetRoleFinished) return false;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return false;
        Logger.Info($"廃村にします: {reason}", "AutoAbort");
        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
        GameManager.Instance.LogicFlow.CheckEndCriteria();
        return true;
    }

    /// <summary>試合中に、生きていた人が切断した時に呼ぶ(ホストだけ)</summary>
    public static void OnAliveDisconnected()
    {
        if (!AmongUsClient.Instance.AmHost || Enabled == null || !Enabled.GetBool()) return;
        disconnectedAlive++;
        if (disconnectedAlive < Count.GetInt()) return;
        EndAsDraw($"生きていた {disconnectedAlive} 人が切断");
    }
}
