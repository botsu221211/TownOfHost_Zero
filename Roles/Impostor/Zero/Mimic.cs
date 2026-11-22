using System;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using static TownOfHostY.Translator;

namespace TownOfHostY.Roles.Impostor;

/// <summary>
/// ミミック (TOH_Zero)
/// シェイプ中は自殺クールが1秒ごとに1上がり(上限あり)、シェイプしていないと1秒ごとに1下がる。
/// 0になると自殺する。自殺クールは会議をまたいで持ち越す。
/// </summary>
public sealed class Mimic : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Mimic),
            player => new Mimic(player),
            CustomRoles.Mimic,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 2300,
            SetupOptionItem,
            "ミミック"
        );
    public Mimic(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        killCooldown = OptionKillCooldown.GetFloat();
        maxSuicideCooldown = OptionMaxSuicideCooldown.GetFloat();
        shapeshiftCooldown = OptionShapeshiftCooldown.GetFloat();
        shapeshiftDuration = OptionShapeshiftDuration.GetFloat();

        suicideCooldown = OptionInitialSuicideCooldown.GetFloat();
        elapsed = 0f;
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionInitialSuicideCooldown;
    private static OptionItem OptionMaxSuicideCooldown;
    private static OptionItem OptionShapeshiftCooldown;
    private static OptionItem OptionShapeshiftDuration;
    enum OptionName
    {
        MimicInitialSuicideCooldown,
        MimicMaxSuicideCooldown,
        MimicShapeshiftCooldown,
        MimicShapeshiftDuration,
    }

    private static float killCooldown;
    private static float maxSuicideCooldown;
    private static float shapeshiftCooldown;
    private static float shapeshiftDuration;

    /// <summary>残りの自殺クール(秒)。会議をまたいで持ち越す</summary>
    private float suicideCooldown;
    private float elapsed;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 25f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionInitialSuicideCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.MimicInitialSuicideCooldown, new(0f, 999f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMaxSuicideCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.MimicMaxSuicideCooldown, new(0f, 999f, 1f), 50f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionShapeshiftCooldown = FloatOptionItem.Create(RoleInfo, 13, OptionName.MimicShapeshiftCooldown, new(0f, 255f, 1f), 0f, false)
            .SetValueFormat(OptionFormat.Seconds);
        // 0 のときは「∞」(時間制限なし)
        OptionShapeshiftDuration = InfinityFloatOptionItem.Create(RoleInfo, 14, OptionName.MimicShapeshiftDuration, new(0f, 180f, 1f), 0f)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public float CalculateKillCooldown() => killCooldown;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = shapeshiftCooldown;
        // バニラのシェイプ時間は 0 が無制限
        AURoleOptions.ShapeshifterDuration = shapeshiftDuration;
    }

    private bool IsShapeshifting
        => Main.CheckShapeshift.TryGetValue(Player.PlayerId, out var shapeshifting) && shapeshifting;

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Player == null || !Player.IsAlive()) return;
        // 会議中・追放画面中は止める(値はそのまま持ち越す)
        if (!GameStates.IsInTask || ExileController.Instance) return;

        elapsed += Time.fixedDeltaTime;
        if (elapsed < 1f) return;
        elapsed -= 1f;

        int before = DisplayValue;
        if (IsShapeshifting)
            suicideCooldown = Mathf.Min(suicideCooldown + 1f, maxSuicideCooldown);
        else
            suicideCooldown -= 1f;

        if (suicideCooldown <= 0f)
        {
            suicideCooldown = 0f;
            Logger.Info($"{Player.GetNameWithRole()} : 自殺クールが0になったため自殺", nameof(Mimic));
            MyState.DeathReason = CustomDeathReason.Suicide; // 死因：自殺
            Player.RpcMurderPlayer(Player);
            return;
        }

        // 表示が変わった時だけ本人の名前表示を更新する
        if (DisplayValue != before)
            Utils.NotifyRoles(SpecifySeer: Player);
    }

    private int DisplayValue => Mathf.CeilToInt(suicideCooldown);

    /// <summary>名前の横に残りの自殺クールを表示(本人のみ)</summary>
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer != Player || seen != Player || !Player.IsAlive()) return "";

        // 残りが少ないほど赤くする
        var color = DisplayValue <= 10 ? "#ff4d4d" : DisplayValue <= 20 ? "#ffd84d" : "#9fe8ff";
        return $"<color={color}>({DisplayValue})</color>";
    }
    public override string GetAbilityButtonText() => GetString("MimicShapeshiftButtonText");

    /// <summary>0 を「∞」と表示する秒数オプション</summary>
    private sealed class InfinityFloatOptionItem : FloatOptionItem
    {
        private InfinityFloatOptionItem(int id, string name, float defaultValue, TabGroup tab, FloatValueRule rule)
        : base(id, name, defaultValue, tab, false, rule) { }

        public static InfinityFloatOptionItem Create(SimpleRoleInfo roleInfo, int idOffset, Enum name, FloatValueRule rule, float defaultValue)
        {
            var opt = new InfinityFloatOptionItem(roleInfo.ConfigId + idOffset, name.ToString(), defaultValue, roleInfo.Tab, rule);
            opt.SetParent(roleInfo.RoleOption);
            return opt;
        }
        public override string GetString()
            => Mathf.Abs(GetFloat()) < 0.0001f ? "∞" : base.GetString();
    }
}
