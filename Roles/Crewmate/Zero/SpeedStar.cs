using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Crewmate;

/// <summary>
/// スピードスター (TOH_Zero)
/// 設定秒数ごとに、自分の足の速さが最小〜最大倍率の間でランダムに変わる。
/// </summary>
public sealed class SpeedStar : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SpeedStar),
            player => new SpeedStar(player),
            CustomRoles.SpeedStar,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 2100,
            SetupOptionItem,
            "スピードスター",
            "#ffd400"
        );
    public SpeedStar(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        interval = OptionInterval.GetFloat();
        minRate = OptionMinRate.GetFloat();
        maxRate = OptionMaxRate.GetFloat();
        if (minRate > maxRate) (minRate, maxRate) = (maxRate, minRate);
        appliedDelta = 0f;
        timer = 0f;
    }

    private static OptionItem OptionInterval;
    private static OptionItem OptionMinRate;
    private static OptionItem OptionMaxRate;
    enum OptionName
    {
        SpeedStarInterval,
        SpeedStarMinRate,
        SpeedStarMaxRate,
    }
    private static float interval;
    private static float minRate;
    private static float maxRate;

    /// <summary>基準の速さに足している量(速さは代入せず増減で変える決まりのため)</summary>
    private float appliedDelta;
    private float timer;

    private static void SetupOptionItem()
    {
        OptionInterval = FloatOptionItem.Create(RoleInfo, 10, OptionName.SpeedStarInterval, new(1f, 30f, 0.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMinRate = FloatOptionItem.Create(RoleInfo, 11, OptionName.SpeedStarMinRate, new(0f, 3f, 0.25f), 0.5f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionMaxRate = FloatOptionItem.Create(RoleInfo, 12, OptionName.SpeedStarMaxRate, new(0.25f, 3f, 0.25f), 3f, false)
            .SetValueFormat(OptionFormat.Multiplier);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        // 会議中・追放の画面の間は数えない
        if (!GameStates.IsInTask || ExileController.Instance) return;

        timer += Time.fixedDeltaTime;
        if (timer < interval) return;
        timer = 0f;
        ChangeSpeed();
    }

    private void ChangeSpeed()
    {
        // 0.05 刻みのランダムな倍率
        float rate = Mathf.Round(Random.Range(minRate, maxRate) * 20f) / 20f;
        float baseSpeed = Main.NormalOptions.PlayerSpeedMod;
        float newDelta = baseSpeed * rate - baseSpeed;

        if (!Main.AllPlayerSpeed.TryGetValue(Player.PlayerId, out var current)) return;
        // 自分が足した分を除いた速さ(他の効果による増減は残す)
        float original = current - appliedDelta;
        // 速さが 0 以下だと不具合の元になるので最低値を保つ
        float target = Mathf.Max(original + newDelta, Main.MinSpeed);
        Main.AllPlayerSpeed[Player.PlayerId] = target;
        appliedDelta = target - original;

        Logger.Info($"{Player.GetNameWithRole()}: 速さ x{rate:0.##}", nameof(SpeedStar));
        Player.MarkDirtySettings();
    }
}
