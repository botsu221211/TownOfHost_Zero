using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// 白狐 / kitsune spirit (TOH_Zero。Nebula on the Ship の妖狐を参考にした)
/// ・お揚げを食べながら身を潜める第三陣営。満腹度が 0 になると餓死する
/// ・マップの決められた場所にお皿が出る。最初は全部のお皿にお揚げが乗っている(茶色いダミー)
/// ・お揚げの上でシェイプ(誰を選んでもよい)するとお揚げを食べる。食べたお皿は空(白いダミー)になり、
///   設定秒数たつとまたお揚げが乗る(会議の間は止まり、会議の後に続きから数える)
/// ・満腹度はキルボタンのクールダウンで表示する(キルはできない)
/// ・キル役のいる方向が、その役職の色の矢印で分かる(設定)
/// ・生存し、満腹度が「勝利に必要な満腹度」以上でゲームが終われば追加勝利
/// </summary>
public sealed class KitsuneSpirit : RoleBase, IKiller, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(KitsuneSpirit),
            player => new KitsuneSpirit(player),
            CustomRoles.KitsuneSpirit,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1500,
            SetupOptionItem,
            "白狐",
            "#E8E3D6",
            isDesyncImpostor: true
        );
    public KitsuneSpirit(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        satiety = Mathf.Min(OptionInitialSatiety.GetFloat(), MaxSatiety);
        displaySynced = false;
    }

    private static OptionItem OptionArrowToKiller;
    private static OptionItem OptionOageRespawn;
    private static OptionItem OptionOageSatiety;
    private static OptionItem OptionInitialSatiety;
    private static OptionItem OptionWinSatiety;
    private static OptionItem OptionDrainRate;
    enum OptionName
    {
        KitsuneArrowToKiller,
        KitsuneOageRespawn,
        KitsuneOageSatiety,
        KitsuneInitialSatiety,
        KitsuneWinSatiety,
        KitsuneSatietyDrainRate,
    }

    /// <summary>満腹度(%)。タスクターンの間 1 秒に「1秒に減る満腹度」ずつ減る</summary>
    private float satiety;
    /// <summary>キルボタンの表示を今の満腹度に合わせたか(タスクターンの始まりに合わせ直す)</summary>
    private bool displaySynced;
    private int lastShownSatiety = -1;
    private float lastTick = -1f;
    private float noOageMessageUntil;

    private static void SetupOptionItem()
    {
        OptionArrowToKiller = BooleanOptionItem.Create(RoleInfo, 10, OptionName.KitsuneArrowToKiller, true, false);
        // 11 は前の「お揚げの最大出現数」で使っていた番号なので空けておく(保存された値と混ざらないように)
        OptionOageRespawn = FloatOptionItem.Create(RoleInfo, 16, OptionName.KitsuneOageRespawn, new(2.5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        // 満腹度は % で数える(満腹が 100%)
        OptionOageSatiety = FloatOptionItem.Create(RoleInfo, 12, OptionName.KitsuneOageSatiety, new(5f, 100f, 5f), 20f, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionInitialSatiety = FloatOptionItem.Create(RoleInfo, 14, OptionName.KitsuneInitialSatiety, new(5f, 100f, 5f), 80f, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionWinSatiety = FloatOptionItem.Create(RoleInfo, 15, OptionName.KitsuneWinSatiety, new(0f, 100f, 5f), 30f, false)
            .SetValueFormat(OptionFormat.Percent);
        // 1 秒に減る満腹度(%)
        OptionDrainRate = FloatOptionItem.Create(RoleInfo, 17, OptionName.KitsuneSatietyDrainRate, new(0.1f, 5f, 0.1f), 0.7f, false)
            .SetValueFormat(OptionFormat.Percent);
    }

    /// <summary>満腹(100%)</summary>
    private const float MaxSatiety = 100f;

    public override void Add()
    {
        if (AmongUsClient.Instance.AmHost) KitsuneDishes.Prepare(OptionOageRespawn.GetFloat());
        // 矢印は設定がオンの時だけ(オフでも登録すると、矢印の更新で名前の書き換えが無駄に増える)
        if (OptionArrowToKiller.GetBool())
            foreach (var pc in Main.AllPlayerControls)
                if (pc.PlayerId != Player.PlayerId) TargetArrow.Add(Player.PlayerId, pc.PlayerId);
    }

    // ===== キルボタン = 満腹ゲージ(キルはできない) =====
    public bool CanKill => false;
    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    // キルボタンのクールダウンには「餓死までの残り秒数」(満腹度 ÷ 減る速さ)を出す。
    // ボタンの数字は 1 秒に 1 ずつ減るので、減る速さを変えてもずれないようにする
    private static float DrainRate => Mathf.Max(OptionDrainRate.GetFloat(), 0.01f);
    private float SecondsLeft => Mathf.Max(satiety / DrainRate, 0.1f);
    public float CalculateKillCooldown() => MaxSatiety / DrainRate;
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!info.IsSuicide) info.DoKill = false;
    }
    public bool OverrideKillButtonText(out string text)
    {
        text = "満腹度";
        return true;
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        AURoleOptions.ShapeshifterCooldown = 1f;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    // ===== お揚げを食べる =====
    // お揚げの上でシェイプ(誰を選んでもよい)。変身はしない
    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        TryEat();
        return false;
    }
    // ホストが白狐の時も、変身ボタン(HostShapeshiftAbility)から同じ OnCheckShapeshift が呼ばれる

    /// <summary>
    /// シェイプの返事。変身を断るだけだとモーションが出ないので、全員の画面に
    /// 「自分への変身(見た目は変わらない)」をモーション付きで送る。ほかの人にもシェイプの煙が見える
    /// </summary>
    public void RespondShapeshift()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // 会議の終わり(追放画面に移る時)にも白狐の画面から「変身解除」の合図が来る。
        // その時にモーション付きで返すと、切り替わりでモーションが途中で止まって次のターンに動けなくなるので、
        // モーションはタスクターン中だけにする
        var inTask = GameStates.IsInTask && Main.introDestroyed
            && MeetingHud.Instance == null && ExileController.Instance == null;
        if (!Player.IsAlive() || !inTask)
        {
            Player.RpcRejectShapeshift();
            return;
        }
        Player.RpcShapeshift(Player, true);
    }

    private void TryEat()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || !GameStates.IsInTask) return;
        if (!KitsuneDishes.TryEat(Player.GetTruePosition()))
        {
            Logger.Info($"{Player.GetNameWithRole()} : 近くにお揚げが無い", nameof(KitsuneSpirit));
            // 食べられなかったことが分かるよう、少しの間 名前の下に出す
            noOageMessageUntil = Time.realtimeSinceStartup + 2f;
            Utils.NotifyRoles(SpecifySeer: Player);
            return;
        }
        noOageMessageUntil = 0f;
        satiety = Mathf.Min(MaxSatiety, satiety + OptionOageSatiety.GetFloat());
        Logger.Info($"{Player.GetNameWithRole()} : お揚げを食べた (満腹度 {satiety:0.0})", nameof(KitsuneSpirit));
        Player.SetKillCooldown(SecondsLeft);
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    // ===== 満腹度が減る・餓死 =====
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => displaySynced = false;
    // 会議の前に消したお皿を出し直す(白狐が死んでいてもお皿は出す。ほかの白狐がいるかもしれないので)
    public override void AfterMeetingTasks() => KitsuneDishes.RespawnAfterMeeting();

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) { lastTick = -1f; return; }
        if (!GameStates.IsInTask || !Main.introDestroyed || MeetingHud.Instance != null || ExileController.Instance != null) { lastTick = -1f; return; }

        if (!displaySynced)
        {
            // タスクターンの始まり: キルボタンのクールダウンを満腹度に合わせる
            displaySynced = true;
            Player.SetKillCooldown(SecondsLeft);
        }

        // 処理の回数ではなく本当の時間で減らす(重い時に遅れないように)
        var now = Time.realtimeSinceStartup;
        if (lastTick >= 0f) satiety -= Mathf.Min(now - lastTick, 1f) * DrainRate;
        lastTick = now;
        if (satiety <= 0f)
        {
            satiety = 0f;
            Logger.Info($"{Player.GetNameWithRole()} : 満腹度が尽きて餓死", nameof(KitsuneSpirit));
            MyState.DeathReason = CustomDeathReason.Starved;
            Player.RpcMurderPlayer(Player);
            return;
        }

        var shown = Mathf.CeilToInt(satiety);
        if (noOageMessageUntil > 0f && Time.realtimeSinceStartup >= noOageMessageUntil)
        {
            noOageMessageUntil = 0f;
            lastShownSatiety = -1; // 「お揚げがない」を消すために名前を書き直す
        }
        if (shown != lastShownSatiety)
        {
            lastShownSatiety = shown;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
    }

    // ===== 勝利 =====
    public bool CheckWin(ref CustomRoles winnerRole)
        => Player.IsAlive() && satiety >= OptionWinSatiety.GetFloat();

    // ===== 表示 =====
    public override string GetProgressText(bool comms = false)
    {
        var enough = satiety >= OptionWinSatiety.GetFloat();
        return Utils.ColorString(enough ? RoleInfo.RoleColor : Color.gray, $"(満腹 {Mathf.CeilToInt(satiety)}%)");
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer != Player || seen != Player || !Player.IsAlive()) return "";
        if (noOageMessageUntil > 0f && Time.realtimeSinceStartup < noOageMessageUntil)
            return Utils.ColorString(Color.gray, "近くにお揚げがない");
        return "";
    }

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting || seer != Player || seen != Player || !OptionArrowToKiller.GetBool() || !Player.IsAlive()) return "";
        var killers = Main.AllAlivePlayerControls
            .Where(pc => pc.PlayerId != Player.PlayerId && !pc.Is(CustomRoles.KitsuneSpirit) && pc.CanUseKillButton())
            .Select(pc => pc.PlayerId)
            .ToArray();
        if (killers.Length == 0) return "";
        // 矢印はキル役ごとに、その役職の色で出す
        var sb = new System.Text.StringBuilder();
        foreach (var id in killers)
        {
            var arrow = TargetArrow.GetArrows(Player, id);
            if (string.IsNullOrEmpty(arrow)) continue;
            var killer = Utils.GetPlayerById(id);
            sb.Append(Utils.ColorString(Utils.GetRoleColor(killer.GetCustomRole()), arrow));
        }
        return sb.ToString();
    }
}

/// <summary>
/// 白狐のお皿とお揚げ。マップの決められた場所(ランダムスポーンの地点)にお皿を置き、最初は全部にお揚げを乗せる。
/// 食べられたお皿は空(白)になり、設定秒数たつとまたお揚げ(茶色)が乗る。ホストだけが持つ
/// </summary>
public static class KitsuneDishes
{
    private const float EatDistance = 1.5f;
    /// <summary>お皿の数(ランダムスポーンの地点から、試合ごとにランダムに選ぶ。その試合中は同じ場所)</summary>
    private const int DishCount = 7;

    private sealed class Dish
    {
        public Vector2 Position;
        public CustomNetObject Object;
        public bool HasOage = true;
        /// <summary>お揚げがまた乗るまでの残り秒数(タスク中だけ減る。会議の間は止まる)</summary>
        public float RespawnLeft;
    }

    private static readonly List<Dish> dishes = new();
    private static float respawnSeconds = 30f;
    private static float lastTick = -1f;

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        dishes.Clear();
        lastTick = -1f;
        CustomNetObject.HiddenForMeeting -= OnHiddenForMeeting;
        CustomNetObject.HiddenForMeeting += OnHiddenForMeeting;
    }

    private static byte ColorOf(Dish dish) => dish.HasOage ? CustomNetObject.ColorBrown : CustomNetObject.ColorWhite;

    // 会議の前にダミーが消されたら、お皿の場所・お揚げの有無・復活までの残り秒数はそのままで、会議の後に出し直す
    private static void OnHiddenForMeeting()
    {
        foreach (var dish in dishes) dish.Object = null;
    }

    /// <summary>会議の後に呼ぶ。消えているお皿を出し直す(実際に出るのは会議明けの 10 秒後から)</summary>
    public static void RespawnAfterMeeting()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // 生きている白狐がいなければ、お皿はもう出さない(会議の前に消えたまま)
        if (!Main.AllAlivePlayerControls.Any(pc => pc.Is(CustomRoles.KitsuneSpirit))) return;
        foreach (var dish in dishes)
        {
            if (dish.Object != null && !dish.Object.IsGone) continue;
            dish.Object = CustomNetObject.Create("", dish.Position, ColorOf(dish));
        }
    }

    /// <summary>お皿を置く(白狐がいる時だけ・1 回だけ)。最初は全部のお皿にお揚げが乗っている</summary>
    public static void Prepare(float respawn)
    {
        if (dishes.Count > 0 || ShipStatus.Instance == null) return;
        respawnSeconds = Mathf.Max(0.5f, respawn);
        lastTick = -1f;
        var spots = PickSpots().OrderBy(_ => IRandom.Instance.Next(1000000)).Take(DishCount);
        foreach (var pos in spots)
            dishes.Add(new Dish { Position = pos, HasOage = true });

        foreach (var dish in dishes)
        {
            dish.Object = CustomNetObject.Create("", dish.Position, ColorOf(dish));
            if (dish.Object == null) Logger.Warn($"お皿のダミーを作れませんでした ({dish.Position})", nameof(KitsuneDishes));
        }
        Logger.Info($"お皿を{dishes.Count}枚置いた (全部にお揚げ・{respawnSeconds}秒で復活)", nameof(KitsuneDishes));
    }

    /// <summary>
    /// お皿を置く候補: ランダムスポーンの地点(Images/*_Random_Map.png の星の所)。この中からランダムに選ぶ
    /// </summary>
    private static List<Vector2> PickSpots()
    {
        Dictionary<string, Vector2> positions = (MapNames)Main.NormalOptions.MapId switch
        {
            MapNames.Skeld => new RandomSpawn.SkeldSpawnMap().positions,
            MapNames.MiraHQ => new RandomSpawn.MiraHQSpawnMap().positions,
            MapNames.Polus => new RandomSpawn.PolusSpawnMap().positions,
            MapNames.Dleks => new RandomSpawn.SkeldSpawnMap().positions,
            MapNames.Airship => new RandomSpawn.AirshipSpawnMap().positions,
            MapNames.Fungle => new RandomSpawn.FungleSpawnMap().positions,
            _ => null,
        };
        if (positions == null || positions.Count == 0)
        {
            Logger.Warn("このマップのお皿の場所がありません", nameof(KitsuneDishes));
            return new();
        }
        var spots = positions.Values.ToList();
        // ドレクスはスケルドを左右反転したマップ
        if ((MapNames)Main.NormalOptions.MapId == MapNames.Dleks)
            spots = spots.Select(p => new Vector2(-p.x, p.y)).ToList();
        return spots;
    }

    /// <summary>pos の近くにお揚げがあれば食べる。食べたお皿は空になり、設定秒数たつとまたお揚げが乗る</summary>
    public static bool TryEat(Vector2 pos)
    {
        var dish = dishes.Where(d => d.HasOage && Vector2.Distance(d.Position, pos) <= EatDistance)
            .OrderBy(d => Vector2.Distance(d.Position, pos))
            .FirstOrDefault();
        if (dish == null) return false;

        dish.HasOage = false;
        dish.RespawnLeft = respawnSeconds;
        dish.Object?.SetLook(ColorOf(dish));
        return true;
    }

    /// <summary>
    /// お揚げの復活を数える(ホストで毎フレーム)。タスク中だけ減らすので、会議を挟んでも残り秒数はそのまま続く
    /// </summary>
    private static void Tick()
    {
        if (dishes.Count == 0 || !AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
        var counting = GameStates.IsInGame && GameStates.IsInTask && Main.introDestroyed
            && MeetingHud.Instance == null && ExileController.Instance == null;
        var now = Time.realtimeSinceStartup;
        if (!counting)
        {
            lastTick = -1f;
            return;
        }
        var delta = lastTick < 0f ? 0f : Mathf.Min(now - lastTick, 1f);
        lastTick = now;
        if (delta <= 0f) return;

        foreach (var dish in dishes)
        {
            if (dish.HasOage) continue;
            dish.RespawnLeft -= delta;
            if (dish.RespawnLeft > 0f) continue;
            dish.HasOage = true;
            dish.RespawnLeft = 0f;
            dish.Object?.SetLook(ColorOf(dish));
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    private static class TickPatch
    {
        public static void Postfix()
        {
            try { Tick(); }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(KitsuneDishes)); }
        }
    }
}
