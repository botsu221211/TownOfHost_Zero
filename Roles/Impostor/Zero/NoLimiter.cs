using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Impostor;

/// <summary>
/// ノーリミッター (TOH_Zero・ネタ役)
/// キルクールが短い代わりに、キルクールが明けた瞬間にキル範囲内に人がいると自動でキルする。
/// キルクールが明けた時にキルできる相手がいなければ自分が死ぬ。
/// </summary>
public sealed class NoLimiter : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(NoLimiter),
            player => new NoLimiter(player),
            CustomRoles.NoLimiter,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 2700,
            SetupOptionItem,
            "ノーリミッター"
        );
    public NoLimiter(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        killCooldown = OptionKillCooldown.GetFloat();
        deadline = null;
    }

    private static OptionItem OptionKillCooldown;
    private static float killCooldown;

    /// <summary>
    /// キルクールが明ける時刻(ホストの Time.realtimeSinceStartup)。null はまだ数え始めていない。
    /// FixedUpdate の回数で数えると、重い時に回数が減って本当の時間より遅れる(数秒ずれる)ので、時刻で決める
    /// </summary>
    private float? deadline;
    /// <summary>SetKillCooldown が本人の画面に届くまでの遅れ(0.2秒後に送られる分 + 通信)</summary>
    private const float SyncDelay = 0.3f;
    private static float Now => Time.realtimeSinceStartup;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public float CalculateKillCooldown() => killCooldown;

    // キルしたら(自分で押しても自動でも)キルクールを数え直す
    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        // キルした瞬間に本人のキルボタンも数え直しになるので、同じ時刻から数える
        if (info.AttemptKiller == Player) deadline = Now + killCooldown;
    }

    // 会議が始まったら止めて、会議の後にまた数え始める
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => deadline = null;

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Player == null || !Player.IsAlive()) return;
        if (!GameStates.IsInTask || ExileController.Instance || !Main.introDestroyed) return;

        if (deadline == null)
        {
            // 数え始め。キルボタンの表示もこちらの時間に合わせる(表示が届くのが少し遅れる分も足す)
            Restart(SyncDelay);
            return;
        }

        if (Now < deadline) return;
        deadline = Now + killCooldown;

        var target = FindTarget();
        if (target == null)
        {
            Logger.Info($"{Player.GetNameWithRole()} : キルクールが明けた時にキルできる相手がいなかったので死亡", nameof(NoLimiter));
            MyState.DeathReason = CustomDeathReason.Suicide;
            Player.RpcMurderPlayer(Player);
            return;
        }

        Logger.Info($"{Player.GetNameWithRole()} : キルクールが明けたので {target.GetNameWithRole()} を自動キル", nameof(NoLimiter));
        CustomRoleManager.OnCheckMurder(Player, target, true);
        // キルが防がれた時もキルクールは数え直す(キルボタンの表示も合わせる)
        if (target.IsAlive()) Restart(SyncDelay);
    }

    /// <summary>キルクールを最初から数え直し、本人のキルボタンの表示も合わせる</summary>
    private void Restart(float delay)
    {
        deadline = Now + killCooldown + delay;
        Player.SetKillCooldown(killCooldown);
    }

    /// <summary>キル範囲内にいる一番近いキルできる人</summary>
    private PlayerControl FindTarget()
    {
        if (Player.inVent) return null;
        var range = NormalGameOptionsV10.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)];
        var me = Player.GetTruePosition();
        return Main.AllAlivePlayerControls
            .Where(p => p.PlayerId != Player.PlayerId && !p.inVent && !p.Is(CustomRoleTypes.Impostor))
            .Select(p => (p, d: Vector2.Distance(me, p.GetTruePosition())))
            .Where(x => x.d <= range && !PhysicsHelpers.AnyNonTriggersBetween(me, (x.p.GetTruePosition() - me).normalized, x.d, Constants.ShipAndObjectsMask))
            .OrderBy(x => x.d)
            .Select(x => x.p)
            .FirstOrDefault();
    }
}
