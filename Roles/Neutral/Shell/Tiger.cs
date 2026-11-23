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
/// ・捕食中のキルは死体をマップの外に残す(通報されない)。キルに成功すると捕食は終わる
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

        // 足を速くする
        if (Main.AllPlayerSpeed.TryGetValue(Player.PlayerId, out var speed)) normalSpeed = speed;
        Main.AllPlayerSpeed[Player.PlayerId] = normalSpeed * OptionSpeed.GetFloat();
        Player.MarkDirtySettings();

        // すぐにキルできるようにする(ファントムの処理の後にもう一度)
        killCooldown = 0.01f;
        _ = new LateTask(() =>
        {
            if (Player != null && hunting && generation == token && Player.IsAlive() && GameStates.IsInTask)
                Player.SetKillCooldown(0.01f);
        }, 0.2f, "TigerReadyToKill");
        return false;
    }

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
        // 捕食中のキル: 普通のキルはやめて、対象をマップの外に移してから倒す(死体はマップの外に残る)
        info.DoKill = false;
        var target = info.AttemptTarget;
        var origin = target.GetTruePosition();
        Logger.Info($"{Player.GetNameWithRole()} が {target.GetNameWithRole()} を捕食", nameof(Tiger));

        target.RpcSnapTo(new Vector2(9999f, 9999f));
        CustomRoleManager.CheckMurderInfos[target.PlayerId] = new MurderInfo(Player, target, target, target);
        target.RpcMurderPlayer(target);
        if (!target.Data.IsDead)
        {
            // キルが失敗した時は、対象をマップの外に置き去りにしない
            CustomRoleManager.CheckMurderInfos.Remove(target.PlayerId);
            target.RpcSnapTo(origin);
            return;
        }
        // 死体はそのまま、キルの演出の後で幽霊だけ元の場所へ戻す
        _ = new LateTask(() =>
        {
            if (GameStates.IsInTask && target != null && target.Data != null && target.Data.IsDead && !target.Data.Disconnected)
                target.RpcSnapTo(origin);
        }, 2f, "TigerRestoreGhost");
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
