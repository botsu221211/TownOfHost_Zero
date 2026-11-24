using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// 白狐 / kitsune spirit (TOH_Zero。Nebula on the Ship のスペクターが元)
/// ・お揚げを食べながら身を潜める第三陣営。満腹度が 0 になると餓死する
/// ・マップの決められた場所にお皿(白いダミー)が出る。そのうちランダムな数枚にお揚げ(茶色いダミー)が乗る
/// ・お揚げの上でシェイプ(誰を選んでもよい)するとお揚げを食べる。食べたお揚げは別のお皿へ移る
/// ・満腹度はキルボタンのクールダウンで表示する(キルはできない)
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
        satiety = Mathf.Min(OptionInitialSatiety.GetFloat(), OptionMaxSatiety.GetFloat());
        displaySynced = false;
    }

    private static OptionItem OptionArrowToKiller;
    private static OptionItem OptionMaxOage;
    private static OptionItem OptionOageSatiety;
    private static OptionItem OptionMaxSatiety;
    private static OptionItem OptionInitialSatiety;
    private static OptionItem OptionWinSatiety;
    enum OptionName
    {
        KitsuneArrowToKiller,
        KitsuneMaxOage,
        KitsuneOageSatiety,
        KitsuneMaxSatiety,
        KitsuneInitialSatiety,
        KitsuneWinSatiety,
    }

    /// <summary>満腹度(秒)。タスクターンの間 1 秒に 1 ずつ減る</summary>
    private float satiety;
    /// <summary>キルボタンの表示を今の満腹度に合わせたか(タスクターンの始まりに合わせ直す)</summary>
    private bool displaySynced;
    private int lastShownSatiety = -1;
    private float lastTick = -1f;
    private float noOageMessageUntil;

    private static void SetupOptionItem()
    {
        OptionArrowToKiller = BooleanOptionItem.Create(RoleInfo, 10, OptionName.KitsuneArrowToKiller, false, false);
        OptionMaxOage = IntegerOptionItem.Create(RoleInfo, 11, OptionName.KitsuneMaxOage, new(1, 10, 1), 3, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionOageSatiety = FloatOptionItem.Create(RoleInfo, 12, OptionName.KitsuneOageSatiety, new(0.5f, 180f, 0.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMaxSatiety = FloatOptionItem.Create(RoleInfo, 13, OptionName.KitsuneMaxSatiety, new(0.5f, 180f, 0.5f), 100f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionInitialSatiety = FloatOptionItem.Create(RoleInfo, 14, OptionName.KitsuneInitialSatiety, new(0.5f, 180f, 0.5f), 80f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionWinSatiety = FloatOptionItem.Create(RoleInfo, 15, OptionName.KitsuneWinSatiety, new(0.5f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    private static float MaxSatiety => OptionMaxSatiety.GetFloat();

    public override void Add()
    {
        if (AmongUsClient.Instance.AmHost) KitsuneDishes.Prepare(OptionMaxOage.GetInt());
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
    // キルクールの上限を最大満腹度にしておき、満腹度は SetKillCooldown で合わせる
    public float CalculateKillCooldown() => MaxSatiety;
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
        Player.SetKillCooldown(Mathf.Max(satiety, 0.1f));
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
            Player.SetKillCooldown(Mathf.Max(satiety, 0.1f));
        }

        // 処理の回数ではなく本当の時間で減らす(重い時に遅れないように)
        var now = Time.realtimeSinceStartup;
        if (lastTick >= 0f) satiety -= Mathf.Min(now - lastTick, 1f);
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
        return Utils.ColorString(enough ? RoleInfo.RoleColor : Color.gray, $"(満腹 {Mathf.CeilToInt(satiety)}/{Mathf.CeilToInt(MaxSatiety)})");
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
        return Utils.ColorString(Color.red, TargetArrow.GetArrows(Player, killers));
    }
}

/// <summary>
/// 白狐のお皿とお揚げ。マップの決められた場所(各部屋の端末の位置)にお皿(白いダミー)を置き、
/// そのうちランダムな数枚にお揚げ(茶色いダミー)を乗せる。ホストだけが持つ
/// </summary>
public static class KitsuneDishes
{
    private const float EatDistance = 1.5f;

    private sealed class Dish
    {
        public Vector2 Position;
        public CustomNetObject Object;
        public bool HasOage;
    }

    private static readonly List<Dish> dishes = new();

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        dishes.Clear();
        CustomNetObject.HiddenForMeeting -= OnHiddenForMeeting;
        CustomNetObject.HiddenForMeeting += OnHiddenForMeeting;
    }

    // 会議の前にダミーが消されたら、お皿の場所とお揚げはそのままで、会議の後に出し直す
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
            dish.Object = CustomNetObject.Create("", dish.Position, dish.HasOage ? CustomNetObject.ColorBrown : CustomNetObject.ColorWhite);
        }
    }

    /// <summary>お皿を置く(白狐がいる時だけ・1 回だけ)</summary>
    public static void Prepare(int maxOage)
    {
        if (dishes.Count > 0 || ShipStatus.Instance == null) return;
        var spots = PickSpots(maxOage);
        foreach (var pos in spots)
            dishes.Add(new Dish { Position = pos });

        foreach (var dish in dishes.OrderBy(_ => IRandom.Instance.Next(1000000)).Take(Mathf.Min(maxOage, dishes.Count)))
            dish.HasOage = true;

        foreach (var dish in dishes)
        {
            dish.Object = CustomNetObject.Create("", dish.Position, dish.HasOage ? CustomNetObject.ColorBrown : CustomNetObject.ColorWhite);
            if (dish.Object == null) Logger.Warn($"お皿のダミーを作れませんでした ({dish.Position})", nameof(KitsuneDishes));
        }
        Logger.Info($"お皿を{dishes.Count}枚置いた (お揚げ {dishes.Count(d => d.HasOage)}個)", nameof(KitsuneDishes));
    }

    /// <summary>
    /// 決められた場所: ランダムスポーンの地点(Images/*_Random_Map.png の星の所)。全部の地点にお皿を置く
    /// </summary>
    private static List<Vector2> PickSpots(int count)
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

    /// <summary>pos の近くにお揚げがあれば食べる。食べたお揚げは別のランダムなお皿へ移る</summary>
    public static bool TryEat(Vector2 pos)
    {
        var dish = dishes.Where(d => d.HasOage && Vector2.Distance(d.Position, pos) <= EatDistance)
            .OrderBy(d => Vector2.Distance(d.Position, pos))
            .FirstOrDefault();
        if (dish == null) return false;

        dish.HasOage = false;
        dish.Object?.SetLook(CustomNetObject.ColorWhite);
        var empty = dishes.Where(d => !d.HasOage && d != dish).ToList();
        if (empty.Count > 0)
        {
            var next = empty[IRandom.Instance.Next(empty.Count)];
            next.HasOage = true;
            next.Object?.SetLook(CustomNetObject.ColorBrown);
        }
        else
        {
            dish.HasOage = true; // ほかに空のお皿が無ければ同じお皿に戻す
            dish.Object?.SetLook(CustomNetObject.ColorBrown);
        }
        return true;
    }
}
