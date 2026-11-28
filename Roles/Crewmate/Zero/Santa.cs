using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Crewmate;

/// <summary>
/// サンタ / Santa (TOH_Zero・TownOfHost-Pko から移植。もとは SuperNewRoles)
/// ・ペットを撫でると「タスクモード」と「プレゼントモード」を切り替える
///   プレゼントモードの間だけ、本人の画面でインポスターになってキルボタンが出る(ほかの人の画面ではクルーのまま)
/// ・プレゼントモードでキルボタンを押すと、相手の役職が設定の確率でシェリフ・ライターなどに変わる
/// ・相手がクルー陣営でなければ、サンタが自殺する(ラバーズ・マッドメイトは設定で渡せる)
/// ・タスクを全部終わらせると、ずっとプレゼントモードになる(ペットがなくても使える)
/// Zero での変更: Pko の天秤・ウルトラスターは Zero にないので、代わりにスピードブースターを入れた /
///   「能力を発揮するタスク数」を使えるように(Pko では使われていなかった) / ホストへはシェリフを渡さない /
///   シェリフだった人にほかの役を渡した時は、本人の画面もクルーに戻す
/// </summary>
public sealed class Santa : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Santa),
            player => new Santa(player),
            CustomRoles.Santa,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 2400,
            SetupOptionItem,
            "サンタ",
            "#f29c9f"
        );
    public Santa(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        giftsLeft = OptionGiftLimit.GetInt();
        presentMode = false;
        tasksCompleted = false;
    }

    private static OptionItem OptionCooldown;
    private static OptionItem OptionGiftLimit;
    private static OptionItem OptionNeedTasks;
    private static OptionItem OptionCanGiftLovers;
    private static OptionItem OptionCanGiftMadmate;
    private static OptionItem OptionRateSheriff;
    private static OptionItem OptionRateLighter;
    private static OptionItem OptionRateNiceGuesser;
    private static OptionItem OptionRateExpress;
    private static OptionItem OptionRateSpeedBooster;
    enum OptionName
    {
        SantaKillCooldown,
        SantaGiftLimit,
        SantaCanTaskCount,
        SantaCanGiftLovers,
        SantaCanGiftMadmate,
        SantaGiftRateSheriff,
        SantaGiftRateLighter,
        SantaGiftRateNiceGuesser,
        SantaGiftRateExpress,
        SantaGiftRateSpeedBooster,
    }

    private int giftsLeft;
    /// <summary>プレゼントモードか(false = タスクモード)</summary>
    private bool presentMode;
    /// <summary>タスクを全部終わらせた(ずっとプレゼントモード)</summary>
    private bool tasksCompleted;
    /// <summary>会議の前にプレゼントモードだった(会議の後に戻す)</summary>
    private bool restoreAfterMeeting;

    private static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.SantaKillCooldown, new(0.5f, 60f, 0.5f), 25f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionGiftLimit = IntegerOptionItem.Create(RoleInfo, 11, OptionName.SantaGiftLimit, new(1, 100, 1), 15, false)
            .SetValueFormat(OptionFormat.Times);
        OptionNeedTasks = IntegerOptionItem.Create(RoleInfo, 12, OptionName.SantaCanTaskCount, new(0, 99, 1), 0, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionCanGiftLovers = BooleanOptionItem.Create(RoleInfo, 13, OptionName.SantaCanGiftLovers, false, false);
        OptionCanGiftMadmate = BooleanOptionItem.Create(RoleInfo, 14, OptionName.SantaCanGiftMadmate, false, false);
        OptionRateSheriff = IntegerOptionItem.Create(RoleInfo, 15, OptionName.SantaGiftRateSheriff, new(0, 100, 5), 20, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionRateLighter = IntegerOptionItem.Create(RoleInfo, 16, OptionName.SantaGiftRateLighter, new(0, 100, 5), 20, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionRateNiceGuesser = IntegerOptionItem.Create(RoleInfo, 17, OptionName.SantaGiftRateNiceGuesser, new(0, 100, 5), 20, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionRateExpress = IntegerOptionItem.Create(RoleInfo, 18, OptionName.SantaGiftRateExpress, new(0, 100, 5), 20, false)
            .SetValueFormat(OptionFormat.Percent);
        OptionRateSpeedBooster = IntegerOptionItem.Create(RoleInfo, 19, OptionName.SantaGiftRateSpeedBooster, new(0, 100, 5), 20, false)
            .SetValueFormat(OptionFormat.Percent);
        Options.OverrideTasksData.Create(RoleInfo, 20);
    }

    private static readonly string Title = Utils.ColorString(RoleInfo.RoleColor, "【サンタ】");

    /// <summary>設定のタスク数を終わらせたか</summary>
    private bool TasksReady => MyTaskState.HasCompletedEnoughCountOfTasks(OptionNeedTasks.GetInt());

    // ───── プレゼント(キルボタン) ─────
    public bool IsKiller => presentMode;
    public bool CanUseKillButton() => Player.IsAlive() && presentMode && giftsLeft > 0;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public float CalculateKillCooldown() => OptionCooldown.GetFloat();
    public bool OverrideKillButtonText(out string text)
    {
        text = "プレゼント";
        return true;
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // プレゼントモードで本人の画面がインポスターになっても、視界はクルーのまま
        opt.SetVision(false);
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsSuicide) return;
        info.DoKill = false; // プレゼントなので殺さない
        var target = info.AttemptTarget;
        if (!AmongUsClient.Instance.AmHost || target == null || !presentMode || giftsLeft <= 0) return;
        giftsLeft--;

        if (!CanReceive(target))
        {
            Logger.Info($"{Player.GetNameWithRole()} : {target.GetNameWithRole()} はクルー陣営ではないので自殺", nameof(Santa));
            MyState.DeathReason = CustomDeathReason.Suicide;
            Player.RpcMurderPlayer(Player);
            return;
        }

        var gift = ChooseGift(target);
        Logger.Info($"{Player.GetNameWithRole()} : {target.GetNameWithRole()} に {gift} をプレゼント (残り{giftsLeft})", nameof(Santa));
        GiveRole(target, gift);
        var giftName = Utils.ColorString(Utils.GetRoleColor(gift), Utils.GetRoleName(gift));
        Utils.SendMessageAutoSplit($"サンタさんから{giftName}のプレゼントを受け取りました。ふぉっふぉっふぉ...", true, Title, target.PlayerId);
        Utils.SendMessageAutoSplit($"{target.GetRealName(true)} に{giftName}をプレゼントしました。", true, Title, Player.PlayerId);

        if (giftsLeft <= 0) SetPresentMode(false);
        else
        {
            Player.SetKillCooldown();
            Utils.NotifyRoles(SpecifySeer: Player);
        }
    }

    /// <summary>プレゼントを受け取れる(クルー陣営)か。受け取れない相手に渡そうとするとサンタが自殺</summary>
    private static bool CanReceive(PlayerControl target)
    {
        var type = target.GetCustomRole().GetCustomRoleTypes();
        if (type == CustomRoleTypes.Crewmate) return true;
        if (target.Is(CustomRoles.Lovers) && OptionCanGiftLovers.GetBool()) return true;
        return type == CustomRoleTypes.Madmate && OptionCanGiftMadmate.GetBool();
    }

    private static CustomRoles ChooseGift(PlayerControl target)
    {
        var table = new (CustomRoles role, int rate)[]
        {
            (CustomRoles.Sheriff, OptionRateSheriff.GetInt()),
            (CustomRoles.Lighter, OptionRateLighter.GetInt()),
            (CustomRoles.NiceGuesser, OptionRateNiceGuesser.GetInt()),
            (CustomRoles.Express, OptionRateExpress.GetInt()),
            (CustomRoles.SpeedBooster, OptionRateSpeedBooster.GetInt()),
        }
        // 今と同じ役職は渡さない。ホストには自分の画面でだけインポスターの役(シェリフ)を渡さない
        // (ホストの役職は全員に送られるので、全員の画面でインポスターになってしまう)
        .Where(t => t.role != target.GetCustomRole())
        .Where(t => !(target.AmOwner && t.role.GetRoleInfo()?.IsDesyncImpostor == true))
        .ToArray();
        if (table.Length == 0) return CustomRoles.Lighter;
        var total = table.Sum(t => t.rate);
        if (total <= 0) return table[IRandom.Instance.Next(table.Length)].role; // 全部 0% なら同じ確率(Pko と同じ)
        var r = IRandom.Instance.Next(total);
        foreach (var (role, rate) in table)
        {
            if (r < rate) return role;
            r -= rate;
        }
        return table[^1].role;
    }

    /// <summary>役職を変える。バニラの役職(キルボタンなど)も合わせる</summary>
    private static void GiveRole(PlayerControl target, CustomRoles role)
    {
        var before = target.GetCustomRole();
        // もとが本人の画面でだけインポスター(シェリフ・噛み殺しモードの番犬・プレゼントモードのサンタ)か
        var wasDesync = before.GetRoleInfo()?.IsDesyncImpostor == true || GuardDog.IsBiting(target) || IsInPresentMode(target);
        // エクスプレスでなくなる時は速さを戻す
        if (before == CustomRoles.Express && role != CustomRoles.Express)
            Main.AllPlayerSpeed[target.PlayerId] = Main.NormalOptions.PlayerSpeedMod;
        target.RpcSetCustomRole(role);
        var info = role.GetRoleInfo();
        if (info?.IsDesyncImpostor == true)
        {
            // 本人の画面でだけインポスター(シェリフなど)。本物のインポスター側は本人の画面でクルーに見せる
            var clientId = target.GetClientId();
            if (clientId != -1) target.RpcSetRoleDesync(RoleTypes.Impostor, clientId);
            StandardIntroHelper.HideImpostorsFromDesyncPlayers();
        }
        else
        {
            var type = role.GetRoleTypes();
            if (target.Data?.Role?.Role != type) target.RpcSetRoleNormal(type, true);
            // もとがシェリフなど(本人の画面でだけインポスター)なら、本人の画面も戻す
            var clientId = target.GetClientId();
            if (wasDesync && clientId != -1) target.RpcSetRoleDesync(type, clientId);
        }
        target.ResetKillCooldown();
        target.MarkDirtySettings();
        _ = new LateTask(() =>
        {
            if (target != null && target.IsAlive() && GameStates.IsInTask) target.SetKillCooldown();
        }, 0.3f, "Santa.GiftKillCooldown");
        Utils.NotifyRoles(ForceLoop: true);
    }

    // ───── ペット: モードの切り替え ─────
    public override bool UsePetAbility => Player.IsAlive() && giftsLeft > 0;
    public override float PetCooldown => 1f;
    public override void OnPet()
    {
        if (tasksCompleted) return; // タスクを全部終わらせたら、ずっとプレゼントモード
        if (!presentMode && !TasksReady)
        {
            Utils.SendMessageAutoSplit($"タスクをあと{OptionNeedTasks.GetInt() - MyTaskState.CompletedTasksCount}個終わらせると、プレゼントモードにできます。", true, Title, Player.PlayerId);
            return;
        }
        SetPresentMode(!presentMode);
    }

    // タスクを全部終わらせると、ずっとプレゼントモードになる(ペットがなくても使える)
    public override bool OnCompleteTask()
    {
        if (!AmongUsClient.Instance.AmHost || tasksCompleted || !Player.IsAlive() || !IsTaskFinished) return true;
        tasksCompleted = true;
        _ = new LateTask(() =>
        {
            if (Player != null && Player.IsAlive() && GameStates.IsInTask) SetPresentMode(true);
        }, 0.5f, "Santa.AutoPresentMode");
        return true;
    }

    private void SetPresentMode(bool on)
    {
        if (!AmongUsClient.Instance.AmHost || presentMode == on) return;
        if (on && (giftsLeft <= 0 || !Player.IsAlive())) return;
        presentMode = on;
        // ホストのキルボタンは MOD が出すので、役職は変えない
        // (ホストの役職は全員に送られるので、変えると全員の画面でインポスターになってしまう)
        var clientId = Player.GetClientId();
        if (clientId != -1 && !Player.AmOwner)
        {
            if (on)
            {
                // 本人の画面でだけインポスターにする。本物のインポスター側は本人の画面でクルーに見せる
                foreach (var target in Main.AllAlivePlayerControls)
                {
                    if (target.PlayerId == Player.PlayerId) continue;
                    if (target.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true) continue;
                    if (target.GetCustomRole().GetRoleTypes() is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.Viper)
                        target.RpcSetRoleDesync(RoleTypes.Crewmate, clientId);
                }
                Player.RpcSetRoleDesync(RoleTypes.Impostor, clientId);
            }
            else
            {
                Player.RpcSetRoleDesync(RoleTypes.Crewmate, clientId);
            }
        }
        Logger.Info($"{Player.GetNameWithRole()} : {(on ? "プレゼントモード" : "タスクモード")}", nameof(Santa));
        Player.MarkDirtySettings();
        if (on)
            _ = new LateTask(() =>
            {
                if (presentMode && Player != null && Player.IsAlive() && GameStates.IsInTask) Player.SetKillCooldown();
            }, 0.3f, "Santa.Cooldown");
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    // 会議の間はタスクモードにしておき(暗転対策とぶつからないように)、会議の後に戻す
    public override void OnStartMeeting()
    {
        restoreAfterMeeting = presentMode || tasksCompleted;
        SetPresentMode(false);
    }
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !restoreAfterMeeting) return;
        // 会議の後の処理は 2 回呼ばれる。暗転対策の役職の戻しが終わってから戻す
        _ = new LateTask(() =>
        {
            if (!restoreAfterMeeting || Player == null || !Player.IsAlive()) return;
            if (!GameStates.IsInTask || MeetingHud.Instance != null || ExileController.Instance != null) return;
            restoreAfterMeeting = false;
            SetPresentMode(true);
        }, 1.5f, "Santa.RestoreMode");
    }

    /// <summary>プレゼントモードのサンタか(本人の画面でインポスターになっている)</summary>
    public static bool IsInPresentMode(PlayerControl pc)
        => pc != null && pc.GetRoleClass() is Santa santa && santa.presentMode && pc.IsAlive();

    public override string GetProgressText(bool comms = false)
    {
        var used = OptionGiftLimit.GetInt() - giftsLeft;
        var mode = presentMode ? Utils.ColorString(RoleInfo.RoleColor, "(プ)") : Utils.ColorString(Palette.CrewmateBlue, "(タ)");
        return mode + Utils.ColorString(giftsLeft > 0 ? RoleInfo.RoleColor : Color.gray, $"({used}/{OptionGiftLimit.GetInt()}){(tasksCompleted ? " ∞" : "")}");
    }
}
