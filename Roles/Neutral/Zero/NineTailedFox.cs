using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// 九尾の狐 / Nine-Tailed Fox (TOH_Zero)
/// ・キルボタンで相手に妖術をかける(同時にかけられるのは 1 人だけ。かけ直すと前の相手の妖術は消える)
/// ・ファントムボタンで、妖術をかけた相手を遠くから焼き殺す
/// ・妖術で焼き殺した場所には、設定ターンの間「焼き跡」が残る
/// ・盤面が自分と、キルできない人の 2 人(または自分だけ)になったら単独勝利
/// ・判定は「九尾の狐」。生きている間は、インポスターが全滅しても人数による勝敗はつかない
/// </summary>
public sealed class NineTailedFox : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(NineTailedFox),
            player => new NineTailedFox(player),
            CustomRoles.NineTailedFox,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1600,
            SetupOptionItem,
            "九尾の狐",
            "#FFB347",
            isDesyncImpostor: true,
            countType: CountTypes.NineTailedFox,
            assignInfo: new RoleAssignInfo(CustomRoles.NineTailedFox, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1)
            }
        );
    public NineTailedFox(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        cursedId = byte.MaxValue;
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionAbilityCooldown;
    private static OptionItem OptionCanVent;
    private static OptionItem OptionBurnTurns;
    enum OptionName
    {
        NineTailedFoxAbilityCooldown,
        NineTailedFoxCanVent,
        NineTailedFoxBurnTurns,
    }

    /// <summary>妖術をかけている相手(いなければ byte.MaxValue)</summary>
    private byte cursedId;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAbilityCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.NineTailedFoxAbilityCooldown, new(0f, 255f, 1f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 12, OptionName.NineTailedFoxCanVent, true, false);
        OptionBurnTurns = IntegerOptionItem.Create(RoleInfo, 13, OptionName.NineTailedFoxBurnTurns, new(1, 5, 1), 2, false)
            .SetValueFormat(OptionFormat.Turns);
    }

    // ===== キルボタン = 妖術をかける =====
    /// <summary>焼き殺している最中だけ true(ガード・役職のキル防止などを効かせる)</summary>
    private bool burning;
    /// <summary>
    /// 妖術をかけるだけの時はキルではないので、相手のガードを消費したり、相手の「キルされそうになった時」の能力を
    /// 動かしたりしない。焼き殺す時だけキル役として扱う
    /// </summary>
    public bool IsKiller => burning;
    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => OptionCanVent.GetBool();
    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();
    public bool OverrideKillButtonText(out string text)
    {
        text = "妖術";
        return true;
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsSuicide) return;
        // キルはせずに妖術をかける(前の相手の妖術は消える)
        info.DoKill = false;
        var target = info.AttemptTarget;
        if (target == null || !target.IsAlive()) return;
        cursedId = target.PlayerId;
        Logger.Info($"{Player.GetNameWithRole()} が {target.GetNameWithRole()} に妖術をかけた", nameof(NineTailedFox));
        Player.SetKillCooldown();
        // キルが取り消された知らせの後にもう一度キルクールを合わせる(取り消しで 0 に戻ることがある)
        _ = new LateTask(() =>
        {
            if (Player != null && Player.IsAlive() && GameStates.IsInTask) Player.SetKillCooldown();
        }, 0.5f, "NineTailedFox.CurseCooldown");
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = OptionAbilityCooldown.GetFloat();
    }

    // ===== ファントムボタン = 妖術をかけた相手を焼き殺す =====
    public override string GetAbilityButtonText() => "焼殺";

    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown)
    {
        // 透明にはならない。キルクールは変えない
        killCooldown = -1f;
        canResetAbilityCooldown = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || !GameStates.IsInTask) return false;

        var target = cursedId == byte.MaxValue ? null : Utils.GetPlayerById(cursedId);
        if (target == null || !target.IsAlive() || target.Data == null || target.Data.Disconnected)
        {
            cursedId = byte.MaxValue;
            Utils.NotifyRoles(SpecifySeer: Player);
            return false;
        }

        // ベントの中・昇降機・はしごの途中の相手は今は焼けない(妖術もクールダウンもそのまま)
        // (ホストの画面の inVent は追い出した後も残ることがあるので、ベントの管理を見る)
        if (VentBooter.IsInVent(target) || target.inMovingPlat || target.onLadder
            || target.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
        {
            Logger.Info($"{Player.GetNameWithRole()} : {target.GetNameWithRole()} は今は焼けない状態", nameof(NineTailedFox));
            return false;
        }

        var position = target.GetTruePosition();
        var state = PlayerState.GetByPlayerId(target.PlayerId);
        var oldReason = state.DeathReason;
        state.DeathReason = CustomDeathReason.Torched;
        // 遠くからのキルなので、相手の自殺の形で倒す(ガード・役職のキル防止などは普通のキルと同じく効く)
        burning = true;
        try { CustomRoleManager.OnCheckMurder(Player, target, target, target, killerMyselfInvoke: true); }
        finally { burning = false; }
        if (target.IsAlive())
        {
            // 防がれた時は死因を戻す
            state.DeathReason = oldReason;
            Logger.Info($"{Player.GetNameWithRole()} : {target.GetNameWithRole()} を焼き殺せなかった", nameof(NineTailedFox));
        }
        else
        {
            Logger.Info($"{Player.GetNameWithRole()} が {target.GetNameWithRole()} を妖術で焼き殺した", nameof(NineTailedFox));
            BurnMarks.Add(position, OptionBurnTurns.GetInt());
        }
        cursedId = byte.MaxValue;
        canResetAbilityCooldown = true;
        Utils.NotifyRoles(SpecifySeer: Player);
        return false;
    }

    // ===== 表示 =====
    /// <summary>妖術をかけた相手には、自分にだけ見える印を付ける</summary>
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer != Player || seen.PlayerId != cursedId || !seen.IsAlive()) return "";
        return Utils.ColorString(RoleInfo.RoleColor, "◎");
    }

    // ===== 勝利 =====
    /// <summary>
    /// 盤面が九尾の狐と、キルできない人の 2 人(または九尾の狐だけ)になったか。
    /// ほかのキル陣営(インポスター・ジャッカル・海賊)が残っている時は勝てない
    /// </summary>
    public static bool CanWinNow()
    {
        var alive = Main.AllAlivePlayerControls.Where(pc => !pc.Is(CountTypes.OutOfGame)).ToList();
        var foxes = alive.Where(pc => pc.Is(CustomRoles.NineTailedFox)).ToList();
        if (foxes.Count == 0) return false;
        var others = alive.Where(pc => !pc.Is(CustomRoles.NineTailedFox)).ToList();
        if (others.Count == 0) return true;
        if (others.Count > 1) return false;
        var other = others[0];
        // 白狐のキルボタンは満腹ゲージなのでキル役ではない
        return !other.CanUseKillButton() || other.Is(CustomRoles.KitsuneSpirit);
    }
}

/// <summary>
/// 九尾の狐が焼き殺した場所に残す「焼き跡」。ダミー(CustomNetObject)で出す。ホストだけが持つ。
/// 会議の前にダミーは消えるので、会議の後に残りターン数を 1 減らして、残っていれば出し直す
/// </summary>
public static class BurnMarks
{
    private const string Sprite = "<color=#ff5a1f>焼け跡</color>";

    private sealed class Mark
    {
        public Vector2 Position;
        public int TurnsLeft;
        public CustomNetObject Object;
    }

    private static readonly List<Mark> marks = new();
    /// <summary>会議の後の処理は 1 回の会議で 2 回呼ばれるので、ターンを減らすのは会議ごとに 1 回だけにする</summary>
    private static bool turnPending;

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        marks.Clear();
        turnPending = false;
        CustomNetObject.HiddenForMeeting -= OnHiddenForMeeting;
        CustomNetObject.HiddenForMeeting += OnHiddenForMeeting;
    }

    private static void OnHiddenForMeeting()
    {
        foreach (var mark in marks) mark.Object = null;
        turnPending = true;
    }

    public static void Add(Vector2 position, int turns)
    {
        if (!AmongUsClient.Instance.AmHost || turns <= 0) return;
        var mark = new Mark { Position = position, TurnsLeft = turns };
        mark.Object = CustomNetObject.Create(Sprite, position, CustomNetObject.ColorBlack);
        marks.Add(mark);
    }

    /// <summary>会議の後に 1 回呼ぶ。ターンを 1 減らし、残っている焼き跡を出し直す</summary>
    public static void AfterMeeting()
    {
        if (!AmongUsClient.Instance.AmHost || marks.Count == 0) return;
        var countDown = turnPending;
        turnPending = false;
        foreach (var mark in marks.ToArray())
        {
            if (countDown) mark.TurnsLeft--;
            if (mark.TurnsLeft <= 0)
            {
                mark.Object?.Despawn();
                marks.Remove(mark);
                continue;
            }
            if (mark.Object == null || mark.Object.IsGone)
                mark.Object = CustomNetObject.Create(Sprite, mark.Position, CustomNetObject.ColorBlack);
        }
    }
}
