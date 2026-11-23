using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using static TownOfHostY.Roles.Unit.CatalystAndConductor;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// カタリスト (TownOfHost-hamo より移植)
/// ・必ずコンダクターと同時にアサインされる。キルボタンは通常使えない(設定で使える)
/// ・共鳴(ファントムボタン): コンダクターのキル範囲にいる一番近い人をキルし、その場所へワープする。
///   コンダクターが自分の視界に入っていない時だけ使える
/// ・相方が死ぬと自分も死ぬ(設定でオポチュニストになる)
/// ・勝利条件: ほかのキル役がおらず、クルーの人数が自陣営の人数以下になること
/// </summary>
public sealed class Catalyst : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Catalyst),
            player => new Catalyst(player),
            CustomRoles.Catalyst,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.UnitNeu + 100, // 使用しない(設定はユニットにある)
            null,
            "カタリスト",
            "#E60049",
            isDesyncImpostor: true
        );
    public Catalyst(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        disabledUntil = -1f;
    }

    public static Catalyst Instance => CustomRoleManager.AllActiveRoles.Values.OfType<Catalyst>().FirstOrDefault();

    private float disabledUntil;
    public bool IsDisabled => Time.time < disabledUntil;

    public float CalculateKillCooldown() => OptionCatalystCanKill.GetBool() ? OptionCatalystKillCooldown.GetFloat() : OptionResonanceCooldown.GetFloat();
    public bool CanUseKillButton() => Player.IsAlive() && OptionCatalystCanKill.GetBool();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = OptionResonanceCooldown.GetFloat();
    }

    /// <summary>コンダクターの不協和音で、しばらく共鳴を使えなくする</summary>
    public void Disable(float seconds)
    {
        disabledUntil = Time.time + seconds;
        Utils.SendMessageAutoSplit($"コンダクターの不協和音で、{seconds} 秒間 共鳴が使えなくなった。", true, Title, Player.PlayerId);
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    private static string Title => "<color=#E60049>【カタリスト】</color>";

    private bool CanSee(PlayerControl other)
    {
        var a = Player.GetTruePosition();
        var b = other.GetTruePosition();
        var dist = Vector2.Distance(a, b);
        float radius;
        try { radius = ShipStatus.Instance.CalculateLightRadius(Player.Data); }
        catch { radius = 3f; }
        if (dist > radius) return false;
        return !PhysicsHelpers.AnyNonTriggersBetween(a, (b - a).normalized, dist, Constants.ShadowMask);
    }

    // ===== 共鳴(ファントムボタン) =====
    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        killCooldown = -1f;
        canResetAbilityCooldown = false;
        if (!Player.IsAlive()) return false;
        var conductor = Conductor.Instance?.Player;
        if (conductor == null || !conductor.IsAlive()) return false;
        void Reply(string text) => Utils.SendMessageAutoSplit(text, true, Title, Player.PlayerId);

        if (IsDisabled) { Reply("今は不協和音で共鳴が使えない。"); return false; }
        if (CanSee(conductor)) { Reply("コンダクターが見えている間は共鳴できない。"); return false; }

        var range = NormalGameOptionsV10.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)];
        var center = conductor.GetTruePosition();
        var target = Main.AllAlivePlayerControls
            .Where(p => p.PlayerId != Player.PlayerId && p.PlayerId != conductor.PlayerId && !p.inVent)
            .Select(p => (p, d: Vector2.Distance(center, p.GetTruePosition())))
            .Where(x => x.d <= range)
            .OrderBy(x => x.d)
            .Select(x => x.p)
            .FirstOrDefault();
        if (target == null) { Reply("コンダクターのキル範囲に誰もいない。"); return false; }

        var pos = target.GetTruePosition();
        Logger.Info($"カタリスト: 共鳴で {target.GetNameWithRole()} をキル", nameof(Catalyst));
        CustomRoleManager.OnCheckMurder(Player, target, target, target, true);
        // キルワープ(キルが防がれた時はワープしない)
        _ = new LateTask(() =>
        {
            if (Player.IsAlive() && !target.IsAlive()) Player.RpcSnapTo(pos);
        }, 0.2f, "Catalyst.Warp");
        canResetAbilityCooldown = true;
        return false;
    }

    public override string GetAbilityButtonText() => "共鳴";

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId || !IsDisabled) return "";
        return Utils.ColorString(Color.gray, "不協和音で共鳴が使えない");
    }

    public override void OnFixedUpdate(PlayerControl player)
        => CatalystPair.CheckPartnerDead(Player, Conductor.Instance?.Player, OptionCatalystToOpportunist.GetBool());

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
        => CatalystPair.ShowPartner(seen, ref enabled);
}

/// <summary>
/// コンダクター (TownOfHost-hamo より移植)
/// ・必ずカタリストと同時にアサインされる。キルボタンは通常使えない(設定で使える)
/// ・不協和音(ファントムボタン): しばらくカタリストの共鳴を使えなくする
/// ・相方が死ぬと自分も死ぬ(設定でオポチュニストになる)
/// </summary>
public sealed class Conductor : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Conductor),
            player => new Conductor(player),
            CustomRoles.Conductor,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.UnitNeu + 100, // 使用しない(設定はユニットにある)
            null,
            "コンダクター",
            "#00E5FF",
            isDesyncImpostor: true
        );
    public Conductor(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    { }

    public static Conductor Instance => CustomRoleManager.AllActiveRoles.Values.OfType<Conductor>().FirstOrDefault();

    public float CalculateKillCooldown() => OptionConductorCanKill.GetBool() ? OptionConductorKillCooldown.GetFloat() : OptionDissonanceCooldown.GetFloat();
    public bool CanUseKillButton() => Player.IsAlive() && OptionConductorCanKill.GetBool();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = OptionDissonanceCooldown.GetFloat();
    }

    // ===== 不協和音(ファントムボタン) =====
    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        killCooldown = -1f;
        canResetAbilityCooldown = false;
        if (!Player.IsAlive()) return false;
        var catalyst = Catalyst.Instance;
        if (catalyst?.Player == null || !catalyst.Player.IsAlive()) return false;
        var seconds = OptionDissonanceDuration.GetFloat();
        catalyst.Disable(seconds);
        Utils.SendMessageAutoSplit($"不協和音で、カタリストの共鳴を {seconds} 秒間 止めた。", true, "<color=#00E5FF>【コンダクター】</color>", Player.PlayerId);
        canResetAbilityCooldown = true;
        return false;
    }

    public override string GetAbilityButtonText() => "不協和音";

    public override void OnFixedUpdate(PlayerControl player)
        => CatalystPair.CheckPartnerDead(Player, Catalyst.Instance?.Player, OptionConductorToOpportunist.GetBool());

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
        => CatalystPair.ShowPartner(seen, ref enabled);
}

public static class CatalystPair
{
    public static bool IsMember(PlayerControl p) => p != null && (p.Is(CustomRoles.Catalyst) || p.Is(CustomRoles.Conductor));

    /// <summary>相方の役職はお互いに見える</summary>
    public static void ShowPartner(PlayerControl seen, ref bool enabled)
    {
        if (IsMember(seen)) enabled = true;
    }

    public static void CheckPartnerDead(PlayerControl me, PlayerControl partner, bool toOpportunist)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask || me == null || !me.IsAlive()) return;
        if (partner != null && partner.IsAlive() && partner.Data?.Disconnected != true) return;
        if (toOpportunist)
        {
            Logger.Info($"{me.GetNameWithRole()}: 相方が死亡したのでオポチュニストになる", nameof(CatalystPair));
            me.RpcSetCustomRole(CustomRoles.Opportunist);
            Utils.NotifyRoles();
            return;
        }
        Logger.Info($"{me.GetNameWithRole()}: 相方が死亡したので後追い", nameof(CatalystPair));
        var state = PlayerState.GetByPlayerId(me.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.FollowingSuicide;
        me.RpcMurderPlayer(me);
    }

    /// <summary>ゲーム終了判定(人数)の最初に呼ぶ。ほかのキル役がおらず、ほかの人数が自陣営以下ならカタリスト陣営の勝利</summary>
    public static bool CheckInstantWin(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        var alive = Main.AllAlivePlayerControls.ToList();
        var team = alive.Where(IsMember).ToList();
        if (team.Count == 0) return false;
        var others = alive.Where(p => !IsMember(p)).ToList();
        if (others.Count > team.Count) return false;
        if (others.Any(p => p.GetCountTypes() is not CountTypes.Crew || p.CanUseKillButton())) return false;
        var ids = Main.AllPlayerControls.Where(IsMember).Select(p => p.PlayerId).ToList();
        if (!CustomWinnerHolder.SetPriorityWinner(CustomWinner.Catalyst, 0, ids)) return false;
        Logger.Info("カタリスト陣営の勝利", nameof(CatalystPair));
        return true;
    }
}
