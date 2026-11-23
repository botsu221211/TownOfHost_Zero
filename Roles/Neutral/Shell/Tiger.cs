using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// タイガー (TownOfHost-Shell より移植)
/// ・通常は長いキルクールでキルする第三陣営
/// ・ファントムボタンで「捕食」状態になる: 足が速くなり視界が狭くなり、すぐにキルできるようになる
/// ・捕食中のキルは死体を残さずにその場で消す(通報も会議での表示もされない)。キルに成功すると捕食は終わる
/// ・捕食の時間内にキルできなければ自滅する。会議が始まると捕食は解除
/// ・自分以外に生きているキル可能者がいなくなると単独勝利
///   (ほかのキル可能者を一度でも確認したか、自分が一度キルした後から判定する)
/// ・タイガーが生きている間は、人数による陣営勝利は保留になる(タスク・サボタージュ勝利はそのまま)
/// </summary>
public sealed class Tiger : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Tiger),
            player => new Tiger(player),
            CustomRoles.Tiger,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1300,
            SetupOptionItem,
            "タイガー",
            "#FFD400",
            isDesyncImpostor: true,
            assignInfo: new RoleAssignInfo(CustomRoles.Tiger, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1)
            }
        );
    public Tiger(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        hunting = false;
        sawOtherKiller = false;
        hasKilled = false;
    }

    private static OptionItem OptionWinPriority;
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionAbilityCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionSpeed;
    private static OptionItem OptionVision;
    enum OptionName
    {
        TigerWinPriority,
        TigerKillCooldown,
        TigerAbilityCooldown,
        TigerDuration,
        TigerSpeed,
        TigerVision,
    }

    private bool hunting;
    private float remaining;
    private float normalSpeed;
    private bool sawOtherKiller;
    private bool hasKilled;
    private int generation;

    public static int WinPriority => OptionWinPriority?.GetInt() ?? 10;

    private static void SetupOptionItem()
    {
        OptionWinPriority = IntegerOptionItem.Create(RoleInfo, 9, OptionName.TigerWinPriority, new(1, 30, 1), 10, false);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.TigerKillCooldown, new(20f, 60f, 2.5f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAbilityCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.TigerAbilityCooldown, new(10f, 40f, 2.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, 12, OptionName.TigerDuration, new(1f, 8f, 0.5f), 3f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionSpeed = FloatOptionItem.Create(RoleInfo, 13, OptionName.TigerSpeed, new(1f, 10f, 0.25f), 1.75f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionVision = FloatOptionItem.Create(RoleInfo, 14, OptionName.TigerVision, new(0.01f, 0.5f, 0.01f), 0.5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
    }

    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = OptionAbilityCooldown.GetFloat();
        opt.SetVision(true);
        if (hunting && Player.IsAlive())
        {
            // 捕食中は視界が狭くなる
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, opt.GetFloat(FloatOptionNames.ImpostorLightMod) * OptionVision.GetFloat());
            opt.SetFloat(FloatOptionNames.CrewLightMod, opt.GetFloat(FloatOptionNames.CrewLightMod) * OptionVision.GetFloat());
        }
    }

    // ===== 捕食(ファントムボタン) =====
    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        canResetAbilityCooldown = true;
        if (!Player.IsAlive() || !GameStates.IsInTask || hunting)
        {
            killCooldown = -1f;
            return false;
        }

        hunting = true;
        remaining = OptionDuration.GetFloat();
        var token = ++generation;
        Logger.Info($"{Player.GetNameWithRole()} が捕食状態になった", nameof(Tiger));

        // 足を速くする(元の速さが取れなかった時はロビーの設定の速さ)
        normalSpeed = Main.AllPlayerSpeed.TryGetValue(Player.PlayerId, out var speed) && speed > 0f
            ? speed : Main.RealOptionsData.GetFloat(FloatOptionNames.PlayerSpeedMod);
        Main.AllPlayerSpeed[Player.PlayerId] = normalSpeed * OptionSpeed.GetFloat();
        Player.MarkDirtySettings();

        // すぐにキルできるようにする。本人の画面では一瞬透明になって戻る時にキルクールが戻ることがあるので、
        // 戻った後にもう一度 0 にする
        killCooldown = 0.01f;
        foreach (var delay in new[] { 0.2f, VanishCancelAppearDelay + 0.15f })
        {
            _ = new LateTask(() =>
            {
                if (Player != null && hunting && generation == token && Player.IsAlive() && GameStates.IsInTask)
                    Player.SetKillCooldown(0.01f);
            }, delay, "TigerReadyToKill");
        }
        return false;
    }

    // 本人の画面で透明になっている間はキルボタンが押せないので、すぐに戻す
    public override float VanishCancelAppearDelay => 0.3f;
    public override string GetAbilityButtonText() => "捕食";

    private void EndPredation()
    {
        if (!hunting) return;
        hunting = false;
        remaining = 0f;
        generation++;
        if (!AmongUsClient.Instance.AmHost || Player == null) return;
        if (normalSpeed > 0f) Main.AllPlayerSpeed[Player.PlayerId] = normalSpeed;
        Player.MarkDirtySettings();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive())
        {
            EndPredation();
            return;
        }
        sawOtherKiller |= HasOtherKiller();
        if (!hunting) return;
        remaining -= Time.fixedDeltaTime;
        if (remaining > 0f) return;

        // 時間内にキルできなかったので自滅
        EndPredation();
        Logger.Info($"{Player.GetNameWithRole()} は捕食に失敗して自滅", nameof(Tiger));
        PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Suicide;
        Player.RpcMurderPlayer(Player);
    }

    public override void OnStartMeeting() => EndPredation();
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => EndPredation();

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!hunting || info.IsSuicide || !info.CanKill) return;
        // 捕食中のキル: 普通のキルはやめて、死体を残さずにその場で消す(会議でも死体は出ない)
        info.DoKill = false;
        var target = info.AttemptTarget;
        if (target == null || target.Data == null || target.Data.IsDead) return;
        Logger.Info($"{Player.GetNameWithRole()} が {target.GetNameWithRole()} を捕食", nameof(Tiger));

        var state = PlayerState.GetByPlayerId(target.PlayerId);
        state.DeathReason = CustomDeathReason.Eaten;
        Main.AllPlayerSpeed[target.PlayerId] = Main.RealOptionsData.GetFloat(FloatOptionNames.PlayerSpeedMod);
        target.NetTransform.Halt();
        target.MarkDirtySettings();

        // 死体なしで霊界へ送る(ジャニターの掃除と同じやり方)
        state.SetDead();
        target.RpcSetRoleNormal(RoleTypes.CrewmateGhost);
        target.RpcExileV3();

        // 普通のキルの後と同じ処理(恋人の後追い、タイガーのキルクールなど)を通す
        CustomRoleManager.CheckMurderInfos[Player.PlayerId] = new MurderInfo(Player, target, Player, target);
        CustomRoleManager.OnMurderPlayer(Player, target);
        Utils.NotifyRoles(ForceLoop: true);
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (info.IsSuicide) return;
        hasKilled = true;
        EndPredation();
        Player.SetKillCooldown(OptionKillCooldown.GetFloat());
    }

    public override string GetProgressText(bool comms = false)
        => hunting ? Utils.ColorString(RoleInfo.RoleColor, "(捕食中)") : "";

    private bool HasOtherKiller()
        => Main.AllAlivePlayerControls.Any(pc => pc.PlayerId != Player.PlayerId && pc.Data != null && !pc.Data.Disconnected && pc.CanUseKillButton());

    private bool CanWin => Player.IsAlive() && !Player.Data.Disconnected && (sawOtherKiller || hasKilled) && !HasOtherKiller();

    public static bool HasLivingTiger() => Main.AllAlivePlayerControls.Any(pc => pc.Is(CustomRoles.Tiger));

    /// <summary>ゲーム終了判定(人数)の最初に呼ぶ。勝利条件を満たすタイガーがいれば単独勝利</summary>
    public static bool TryChooseWinner()
    {
        foreach (var tiger in CustomRoleManager.AllActiveRoles.Values.OfType<Tiger>().OrderBy(t => t.Player.PlayerId))
        {
            if (!tiger.CanWin) continue;
            if (!CustomWinnerHolder.SetPriorityWinner(CustomWinner.Tiger, WinPriority, new[] { tiger.Player.PlayerId }, addWin: false)) continue;
            Logger.Info($"{tiger.Player.GetNameWithRole()} 以外にキル可能者がいなくなったので単独勝利", nameof(Tiger));
            return true;
        }
        return false;
    }
}
