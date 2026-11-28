using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using static TownOfHostY.Translator;

namespace TownOfHostY.Roles.Crewmate;

/// <summary>
/// 番犬 / Guard Dog (TOH_Zero・人狼ジャッジメントより)
/// ・会議中に自投票してから投票した人を「飼い主」にする
/// ・番犬が生きている間、飼い主はキルから守られる(何度でも)
/// ・番犬は飼い主だけを噛み殺せる(キルボタン)。飼い主以外にキルボタンを押しても何も起きない
/// ・ペットを撫でると「タスクモード」と「噛み殺しモード」を切り替える。最初と会議の後はタスクモード。
///   噛み殺しモードの間だけ、本人の画面でインポスターになってキルボタンが出る(ほかの人の画面ではクルーのまま)
/// ・番犬からは飼い主が誰か分かる(役職は設定で)。飼い主からは番犬が分からない(設定で分かるようにできる)
/// ・占い師・霊媒師の結果は「インポスター」と出る
/// ・設定のターン数の会議が終わるまでに飼い主を決めないと、番犬は死亡する
/// </summary>
public sealed class GuardDog : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(GuardDog),
            player => new GuardDog(player),
            CustomRoles.GuardDog,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 2300,
            SetupOptionItem,
            "番犬",
            "#c08a4a"
        );
    public GuardDog(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        owners = new();
        designationsLeft = OptionMultipleOwners.GetBool() ? OptionDesignateCount.GetInt() : 1;
        meetingsWithoutOwner = 0;
        biteMode = false;
        selecting = false;
        Instances.Add(this);
        if (Instances.Count == 1) CustomRoleManager.MarkOthers.Add(GetMarkOthers);
    }
    public override void OnDestroy()
    {
        Instances.Remove(this);
        if (Instances.Count == 0) CustomRoleManager.MarkOthers.Remove(GetMarkOthers);
    }

    private static OptionItem OptionMustDesignateTurns;
    private static OptionItem OptionMultipleOwners;
    private static OptionItem OptionDesignateCount;
    private static OptionItem OptionBiteCooldown;
    private static OptionItem OptionOwnerKnowsDog;
    private static OptionItem OptionKnowOwnerRole;
    enum OptionName
    {
        GuardDogMustDesignateTurns,
        GuardDogMultipleOwners,
        GuardDogDesignateCount,
        GuardDogBiteCooldown,
        GuardDogOwnerKnowsDog,
        GuardDogKnowOwnerRole,
    }

    private static readonly List<GuardDog> Instances = new();

    /// <summary>飼い主</summary>
    private HashSet<byte> owners;
    /// <summary>あと何人飼い主にできるか</summary>
    private int designationsLeft;
    /// <summary>飼い主がいないまま終わった会議の数</summary>
    private int meetingsWithoutOwner;
    /// <summary>噛み殺しモードか(false = タスクモード)</summary>
    private bool biteMode;
    /// <summary>自投票して、次の投票で飼い主を選ぶところか</summary>
    private bool selecting;
    /// <summary>この会議の分を数えたか(会議の後の処理は 1 回の会議で 2 回呼ばれるため)</summary>
    private bool countedThisMeeting;
    /// <summary>飼い主を決めなかったので、タスクターンになったら死亡する</summary>
    private bool dieWhenInTask;

    private const string Mark = "◇";

    private static void SetupOptionItem()
    {
        OptionMustDesignateTurns = IntegerOptionItem.Create(RoleInfo, 10, OptionName.GuardDogMustDesignateTurns, new(1, 5, 1), 2, false)
            .SetValueFormat(OptionFormat.Turns);
        OptionMultipleOwners = BooleanOptionItem.Create(RoleInfo, 11, OptionName.GuardDogMultipleOwners, false, false);
        OptionDesignateCount = IntegerOptionItem.Create(RoleInfo, 12, OptionName.GuardDogDesignateCount, new(1, 5, 1), 1, false, OptionMultipleOwners)
            .SetValueFormat(OptionFormat.Times);
        OptionBiteCooldown = FloatOptionItem.Create(RoleInfo, 13, OptionName.GuardDogBiteCooldown, new(0f, 45f, 1f), 0f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionOwnerKnowsDog = BooleanOptionItem.Create(RoleInfo, 14, OptionName.GuardDogOwnerKnowsDog, false, false);
        OptionKnowOwnerRole = BooleanOptionItem.Create(RoleInfo, 15, OptionName.GuardDogKnowOwnerRole, false, false);
    }

    private bool IsOwner(byte id) => owners.Contains(id);

    // ───── 噛み殺し(キルボタン) ─────
    public bool IsKiller => biteMode;
    public bool CanUseKillButton() => Player.IsAlive() && biteMode;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public float CalculateKillCooldown() => OptionBiteCooldown.GetFloat();
    public bool OverrideKillButtonText(out string text)
    {
        text = "噛み殺す";
        return true;
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsSuicide) return;
        var target = info.AttemptTarget;
        // 噛み殺せるのは飼い主だけ
        if (target == null || !IsOwner(target.PlayerId))
        {
            info.DoKill = false;
            Logger.Info($"{Player.GetNameWithRole()} : {target?.GetNameWithRole()} は飼い主ではないので噛めない", nameof(GuardDog));
            return;
        }
        Logger.Info($"{Player.GetNameWithRole()} が飼い主 {target.GetNameWithRole()} を噛み殺した", nameof(GuardDog));
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // 噛み殺しモードで本人の画面がインポスターになっても、視界はクルーのまま
        opt.SetVision(false);
    }

    // ───── ペット: モードの切り替え ─────
    public override bool UsePetAbility => Player.IsAlive();
    public override float PetCooldown => 1f;
    public override void OnPet() => SetBiteMode(!biteMode);

    private void SetBiteMode(bool bite)
    {
        if (!AmongUsClient.Instance.AmHost || biteMode == bite) return;
        biteMode = bite;
        // ホストのキルボタンは MOD が出すので、役職は変えない
        // (ホストの役職は全員に送られるので、変えると全員の画面でインポスターになってしまう)
        var clientId = Player.GetClientId();
        if (clientId != -1 && !Player.AmOwner)
        {
            if (bite)
            {
                // 本人の画面でだけインポスターにする。本物のインポスター側は本人の画面でクルーに見せる
                // (インポスター同士に見えると、名前が赤くなってインポスターが分かってしまう)
                foreach (var target in Main.AllAlivePlayerControls)
                {
                    if (target.PlayerId == Player.PlayerId) continue;
                    if (target.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true) continue;
                    var type = target.GetCustomRole().GetRoleTypes();
                    if (type is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.Viper)
                        target.RpcSetRoleDesync(RoleTypes.Crewmate, clientId);
                }
                Player.RpcSetRoleDesync(RoleTypes.Impostor, clientId);
            }
            else
            {
                Player.RpcSetRoleDesync(RoleTypes.Crewmate, clientId);
            }
        }
        Logger.Info($"{Player.GetNameWithRole()} : {(bite ? "噛み殺しモード" : "タスクモード")}", nameof(GuardDog));
        Player.MarkDirtySettings();
        if (bite)
        {
            _ = new LateTask(() =>
            {
                if (biteMode && Player != null && Player.IsAlive() && GameStates.IsInTask) Player.SetKillCooldown();
            }, 0.3f, "GuardDog.BiteCooldown");
        }
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    // 会議が始まったらタスクモードに戻す(会議の後もタスクモードから)
    public override void OnStartMeeting()
    {
        SetBiteMode(false);
        selecting = false;
        countedThisMeeting = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || designationsLeft <= 0) return;
        var left = designationsLeft;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsMeeting || Player == null || !Player.IsAlive()) return;
            var text = owners.Count == 0
                ? $"自投票すると、次に投票した人を飼い主にできます(あと{left}人)。\n{OptionMustDesignateTurns.GetInt()}回目の会議が終わるまでに飼い主を決めないと死亡します。(今 {meetingsWithoutOwner + 1}回目)"
                : $"自投票すると、次に投票した人を飼い主に追加できます(あと{left}人)。";
            Utils.SendMessageAutoSplit(text, true, Utils.ColorString(RoleInfo.RoleColor, "【番犬】"), Player.PlayerId);
        }, 5f, "GuardDog.Guide");
    }

    // ───── 会議: 自投票 → 投票した人を飼い主にする ─────
    public override bool CheckVoteAsVoter(PlayerControl votedFor)
    {
        if (!Player.IsAlive() || designationsLeft <= 0) return true;
        var title = Utils.ColorString(RoleInfo.RoleColor, "【番犬】");
        if (selecting)
        {
            if (votedFor == null)
            {
                selecting = false;
                Utils.SendMessageAutoSplit("飼い主の指定をやめました。", true, title, Player.PlayerId);
                return false;
            }
            if (votedFor.PlayerId == Player.PlayerId)
            {
                // もう一度自投票したら、普通の投票(自投票)にする
                selecting = false;
                return true;
            }
            if (IsOwner(votedFor.PlayerId))
            {
                Utils.SendMessageAutoSplit($"{votedFor.GetRealName(true)} はもう飼い主です。ほかの人に投票してください。", true, title, Player.PlayerId);
                return false;
            }
            owners.Add(votedFor.PlayerId);
            designationsLeft--;
            selecting = false;
            Logger.Info($"{Player.GetNameWithRole()} が {votedFor.GetNameWithRole()} を飼い主にした", nameof(GuardDog));
            Utils.SendMessageAutoSplit($"{votedFor.GetRealName(true)} を飼い主にしました。\nもう一度投票できます。", true, title, Player.PlayerId);
            Utils.NotifyRoles(isForMeeting: true, NoCache: true);
            return false;
        }
        if (votedFor != null && votedFor.PlayerId == Player.PlayerId)
        {
            selecting = true;
            Utils.SendMessageAutoSplit("飼い主にする人に投票してください。\n(スキップでやめる・もう一度自投票すると普通の投票になる)", true, title, Player.PlayerId);
            return false;
        }
        return true;
    }

    // 決めたターン数の会議が終わるまでに飼い主がいなければ死亡する
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || countedThisMeeting) return;
        countedThisMeeting = true;
        if (owners.Count > 0) return;
        meetingsWithoutOwner++;
        if (meetingsWithoutOwner < OptionMustDesignateTurns.GetInt()) return;
        Logger.Info($"{Player.GetNameWithRole()} : 飼い主を決めなかったので、タスクターンで死亡する", nameof(GuardDog));
        dieWhenInTask = true;
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!dieWhenInTask || !AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) { dieWhenInTask = false; return; }
        if (!GameStates.IsInTask || MeetingHud.Instance != null || ExileController.Instance != null || !Main.introDestroyed) return;
        dieWhenInTask = false;
        MyState.DeathReason = CustomDeathReason.Suicide;
        Player.RpcMurderPlayer(Player);
    }

    // ───── 飼い主を守る ─────
    /// <summary>キルの確認の時に呼ぶ。生きている番犬の飼い主へのキルを防いだら true</summary>
    public static bool GuardPlayerCheckMurder(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        if (killer == null || target == null || info.IsSuicide) return false;
        var dogs = Instances.Where(d => d.Player != null && d.Player.IsAlive() && d.IsOwner(target.PlayerId)).ToList();
        if (dogs.Count == 0) return false;
        // 番犬自身の噛み殺しは防がない
        if (dogs.Any(d => d.Player.PlayerId == killer.PlayerId)) return false;
        if (killer.GetCustomRole().IsDirectKillRole()) return false;
        Logger.Info($"番犬が飼い主 {target.GetNameWithRole()} を {killer.GetNameWithRole()} のキルから守った", nameof(GuardDog));
        killer.RpcProtectedMurderPlayer(target); // キルした側にだけ守られた表示
        info.CanKill = false;
        return true;
    }

    // ───── 表示 ─────
    /// <summary>番犬からは飼い主に印が見える</summary>
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer != Player || !IsOwner(seen.PlayerId)) return "";
        return Utils.ColorString(RoleInfo.RoleColor, Mark);
    }
    /// <summary>設定がオンなら、飼い主からは番犬に印が見える</summary>
    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer == null || seen == null || !OptionOwnerKnowsDog.GetBool()) return "";
        return Instances.Any(d => d.Player == seen && d.IsOwner(seer.PlayerId))
            ? Utils.ColorString(RoleInfo.RoleColor, Mark) : "";
    }
    /// <summary>番犬か、番犬が分かる飼い主か(ほかの人の名前を個別に送る相手にする)</summary>
    public static bool NeedsOthersNames(PlayerControl seer)
        => seer != null && (seer.Is(CustomRoles.GuardDog)
            || (OptionOwnerKnowsDog.GetBool() && Instances.Any(d => d.IsOwner(seer.PlayerId))));

    /// <summary>設定がオンなら、番犬からは飼い主の役職が分かる</summary>
    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (seen != null && OptionKnowOwnerRole.GetBool() && IsOwner(seen.PlayerId)) enabled = true;
    }

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(biteMode ? Palette.ImpostorRed : Palette.CrewmateBlue, biteMode ? "(噛)" : "(タ)");

    /// <summary>噛み殺しモードの番犬か(本人の画面でインポスターになっている)</summary>
    public static bool IsBiting(PlayerControl pc)
        => pc != null && pc.GetRoleClass() is GuardDog dog && dog.biteMode && pc.IsAlive();

    /// <summary>
    /// 本物のインポスター側の人をクルーに見せないといけない人か
    /// (自分の画面でだけインポスターになっている人: ジャッカル・白狐などと、噛み殺しモードの番犬)
    /// </summary>
    public static bool SeesImpostorsAsCrew(PlayerControl seer)
        => seer != null && seer.IsAlive() && !seer.AmOwner
            && (seer.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true || IsBiting(seer) || Santa.IsInPresentMode(seer));

    /// <summary>本物のインポスター側の役職(本人以外の画面でもインポスター側に見える)か</summary>
    public static bool IsRealImpostorSide(PlayerControl pc)
    {
        if (pc == null || pc.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true) return false;
        return pc.GetCustomRole().GetRoleTypes() is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.Viper;
    }

    private static bool rehidePending;
    /// <summary>
    /// 誰かのデータが全員に送られた時に呼ぶ(GameDataSerializePatch)。
    /// データには役職も入っていて、ホストの画面の役職(インポスターなど)に戻ってしまうので、少し後にクルーに見せ直す
    /// </summary>
    public static void OnDataSerialized(PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost || rehidePending || !IsRealImpostorSide(target)) return;
        if (!Main.AllAlivePlayerControls.Any(SeesImpostorsAsCrew)) return;
        rehidePending = true;
        _ = new LateTask(() =>
        {
            rehidePending = false;
            if (!GameStates.IsInGame || CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
            // 会議・追放画面の間は、暗転対策で身代わりを見せている番犬(本人の画面ではクルー)には触らない。
            // 自分の画面でだけインポスターの人(ジャッカルなど)には暗転対策を送っていないので、会議中も見せ直す
            var inMeeting = MeetingHud.Instance != null || ExileController.Instance != null || !GameStates.IsInTask;
            foreach (var seer in Main.AllAlivePlayerControls.Where(SeesImpostorsAsCrew).ToArray())
            {
                if (inMeeting && seer.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor != true) continue;
                var clientId = seer.GetClientId();
                if (clientId == -1) continue;
                foreach (var imp in Main.AllAlivePlayerControls.Where(IsRealImpostorSide).ToArray())
                    if (imp.PlayerId != seer.PlayerId) imp.RpcSetRoleDesync(RoleTypes.Crewmate, clientId);
            }
        }, 0.05f, "GuardDog.Rehide");
    }

    /// <summary>占い師・霊媒師の結果で「インポスター」と出すか</summary>
    public static bool ShowsAsImpostor(PlayerControl pc) => pc != null && pc.Is(CustomRoles.GuardDog);
    /// <summary>占い・霊媒の結果の文字(陣営を出す設定なら「インポスター陣営」、役職を出す設定なら「インポスター」)</summary>
    public static string FortuneText(bool confirmCamp)
        => (confirmCamp ? GetString("TeamImpostor") : Utils.GetRoleName(CustomRoles.Impostor)).Color(Palette.ImpostorRed);
}
