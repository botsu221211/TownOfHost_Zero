using System.Linq;
using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// ファーストビィクティム / First victim (TOH_Zero・ネタ役)
/// ・試合で一番最初のキルで自分が死亡すると、その時点で単独勝利
/// ・一番最初のキルで別の人が死亡したら、自分は死亡する(死因: 絶望死)
/// ・自殺(自爆・餓死など、キルした人とされた人が同じ)は「キル」に数えない
/// ・勝利優先度: ほかの勝利と同時になった時、優先度の高い方が勝つ
/// </summary>
public sealed class FirstVictim : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(FirstVictim),
            player => new FirstVictim(player),
            CustomRoles.FirstVictim,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1700,
            SetupOptionItem,
            "ファーストビィクティム",
            "#c9a0dc"
        );
    public FirstVictim(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        firstKillDone = false;
        CustomRoleManager.OnMurderPlayerOthers.Add(OnAnyMurder);
    }
    public override void OnDestroy()
    {
        CustomRoleManager.OnMurderPlayerOthers.Remove(OnAnyMurder);
    }

    private static OptionItem OptionWinPriority;
    private enum OptionName
    {
        FirstVictimWinPriority,
    }

    /// <summary>この試合の一番最初のキルが終わったか(全員で共通)</summary>
    private static bool firstKillDone;

    public static int WinPriority => OptionWinPriority?.GetInt() ?? 50;

    private static void SetupOptionItem()
    {
        OptionWinPriority = IntegerOptionItem.Create(RoleInfo, 10, OptionName.FirstVictimWinPriority, new(0, 100, 1), 50, false);
    }

    /// <summary>誰かがキルされた時(自分が登録した分ごとに呼ばれる)</summary>
    private void OnAnyMurder(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (info.IsSuicide) return;
        // ファーストビィクティムが複数いても、最初のキルの判定は 1 回だけ
        if (firstKillDone) return;
        firstKillDone = true;

        var victim = info.AttemptTarget;
        Logger.Info($"最初のキル: {info.AttemptKiller?.GetNameWithRole()} => {victim?.GetNameWithRole()}", nameof(FirstVictim));

        var all = CustomRoleManager.AllActiveRoles.Values.OfType<FirstVictim>().ToList();
        if (victim != null && victim.Is(CustomRoles.FirstVictim))
        {
            // 最初のキルで死んだファーストビィクティムの単独勝利
            if (CustomWinnerHolder.WinnerTeam == CustomWinner.Default || CustomWinnerHolder.WinPriority < WinPriority)
            {
                Logger.Info($"{victim.GetNameWithRole()} が最初のキルで死亡したので単独勝利", nameof(FirstVictim));
                CustomWinnerHolder.SetPriorityWinner(CustomWinner.FirstVictim, WinPriority, new[] { victim.PlayerId });
            }
        }

        // 最初のキルで死ななかったファーストビィクティムは絶望死する
        foreach (var role in all)
        {
            var pc = role.Player;
            if (pc == null || !pc.IsAlive() || (victim != null && pc.PlayerId == victim.PlayerId)) continue;
            Logger.Info($"{pc.GetNameWithRole()} : 最初のキルで死ねなかったので絶望死", nameof(FirstVictim));
            // キルの処理の途中なので、少し後に死亡させる
            _ = new LateTask(() =>
            {
                if (pc == null || !pc.IsAlive() || !GameStates.IsInGame) return;
                if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
                PlayerState.GetByPlayerId(pc.PlayerId).DeathReason = CustomDeathReason.Despair;
                pc.RpcMurderPlayer(pc);
            }, 0.2f, "FirstVictim.Despair");
        }
    }
}
