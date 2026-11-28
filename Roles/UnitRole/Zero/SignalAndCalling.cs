using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Impostor;
using TownOfHostY.Roles.Madmate;

namespace TownOfHostY.Roles.Unit;

/// <summary>
/// シグナル＆コーリング (TOH_Zero) のコンビ設定
/// ・インポスター「シグナル」とマッドメイト「コーリング」が必ず一緒に出る
/// ・コーリングが会議中に自投票すると交換要請。次のターンからシグナルとコーリングが入れ替わる
/// ・シグナルが死んだ時のコーリング: 後追い / シグナルになる / 何もしない
/// </summary>
public sealed class SignalAndCalling : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SignalAndCalling),
            player => new SignalAndCalling(player),
            CustomRoles.SignalAndCalling,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Unit,
            (int)Options.offsetId.UnitMix + 100,
            SetupOptionItem,
            "シグナル&コーリング",
            "#ffffff",
            tab: TabGroup.UnitRoles,
            assignInfo: new RoleAssignInfo(CustomRoles.SignalAndCalling, CustomRoleTypes.Impostor)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = new CustomRoles[2] { CustomRoles.Signal, CustomRoles.Calling }
            }
        );
    public SignalAndCalling(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }

    public static OptionItem OptionCanSeeEachOther;
    public static OptionItem OptionOnSignalDead;
    public static OptionItem OptionCanVent;
    public static OptionItem OptionVentCooldown;
    public static OptionItem OptionVentMaxTime;
    public static OptionItem OptionUseMadmateSettings;
    enum OptionName
    {
        SignalCallingCanSeeEachOther,
        SignalCallingOnSignalDead,
        SignalCallingCanVent,
        SignalCallingVentCooldown,
        SignalCallingVentMaxTime,
        SignalCallingUseMadmateSettings,
    }
    public enum DeadAction
    {
        Follow,
        BecomeSignal,
        Nothing,
    }
    private static readonly string[] DeadActionTexts =
        { "SignalCallingDeadFollow", "SignalCallingDeadBecomeSignal", "SignalCallingDeadNothing" };

    private static void SetupOptionItem()
    {
        OptionCanSeeEachOther = BooleanOptionItem.Create(RoleInfo, 10, OptionName.SignalCallingCanSeeEachOther, true, false);
        OptionOnSignalDead = StringOptionItem.Create(RoleInfo, 11, OptionName.SignalCallingOnSignalDead, DeadActionTexts, (int)DeadAction.Nothing, false);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 12, OptionName.SignalCallingCanVent, false, false);
        OptionVentCooldown = FloatOptionItem.Create(RoleInfo, 13, OptionName.SignalCallingVentCooldown, new(0f, 180f, 0.5f), 20f, false, OptionCanVent)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVentMaxTime = FloatOptionItem.Create(RoleInfo, 14, OptionName.SignalCallingVentMaxTime, new(0f, 180f, 0.5f), 10f, false, OptionCanVent)
            .SetValueFormat(OptionFormat.Seconds);
        OptionUseMadmateSettings = BooleanOptionItem.Create(RoleInfo, 15, OptionName.SignalCallingUseMadmateSettings, true, false);
    }

    public static bool CanSeeEachOther => OptionCanSeeEachOther?.GetBool() ?? true;
    public static DeadAction OnSignalDead => (DeadAction)(OptionOnSignalDead?.GetValue() ?? (int)DeadAction.Nothing);
    public static bool CanVent => OptionCanVent?.GetBool() ?? false;
    public static bool UseMadmateSettings => OptionUseMadmateSettings?.GetBool() ?? true;

    // ───── 2人の今の担当 ─────
    public static byte SignalId = byte.MaxValue;
    public static byte CallingId = byte.MaxValue;
    /// <summary>コーリングが今の会議で交換要請を出したか</summary>
    public static bool SwapRequested;
    /// <summary>会議中にシグナルが死んだので、会議の後にコーリングをシグナルにする</summary>
    private static bool promotePending;
    /// <summary>入れ替えの途中(役職の作り直しで値を消さないため)</summary>
    public static bool Changing;

    public static PlayerControl SignalPlayer => Utils.GetPlayerById(SignalId);
    public static PlayerControl CallingPlayer => Utils.GetPlayerById(CallingId);
    public static readonly string Title = Utils.ColorString(Palette.ImpostorRed, "【シグナル&コーリング】");

    /// <summary>試合の始まりで呼ぶ(シグナルの Add から)</summary>
    public static void ResetForNewGame()
    {
        SwapRequested = false;
        promotePending = false;
        scheduled = false;
    }

    /// <summary>相方か</summary>
    public static bool IsPartner(PlayerControl seer, PlayerControl seen)
        => seer != null && seen != null && seer.PlayerId != seen.PlayerId
            && (seer.PlayerId == SignalId || seer.PlayerId == CallingId)
            && (seen.PlayerId == SignalId || seen.PlayerId == CallingId);

    // ───── 死亡 ─────
    /// <summary>誰かが死んだ時に呼ぶ(キルは CustomRoleManager、追放は MeetingHudPatch)</summary>
    public static void OnPlayerDead(byte deadId, bool isMeeting)
    {
        if (!AmongUsClient.Instance.AmHost || deadId != SignalId) return;
        var calling = CallingPlayer;
        if (calling == null || !calling.IsAlive() || !calling.Is(CustomRoles.Calling)) return;
        switch (OnSignalDead)
        {
            case DeadAction.Follow:
                Logger.Info($"{calling.GetNameWithRole()} : シグナルが死んだので後追い", nameof(SignalAndCalling));
                if (isMeeting)
                {
                    MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.FollowingSuicide, calling.PlayerId);
                }
                else
                {
                    PlayerState.GetByPlayerId(calling.PlayerId).DeathReason = CustomDeathReason.FollowingSuicide;
                    calling.RpcMurderPlayer(calling);
                }
                break;
            case DeadAction.BecomeSignal:
                Logger.Info($"{calling.GetNameWithRole()} : シグナルが死んだのでシグナルになる({(isMeeting ? "会議の後" : "すぐ")})", nameof(SignalAndCalling));
                if (isMeeting || !GameStates.IsInTask) promotePending = true;
                else _ = new LateTask(Promote, 0.3f, "SignalAndCalling.Promote");
                break;
        }
    }

    // ───── 会議の後: 交換 / シグナルになる ─────
    /// <summary>入れ替えを予約しているか</summary>
    private static bool scheduled;
    /// <summary>この時刻より後に入れ替える</summary>
    private static float runAt;

    /// <summary>
    /// タスクターンが始まる時に呼ぶ(Utils.AfterMeetingTasks)。会議の終わりと追放画面の後の 2 回呼ばれるので、
    /// 最後に呼ばれてから 1.2 秒後(暗転対策の役職の戻しが終わってから)に 1 回だけ入れ替える
    /// </summary>
    public static void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!promotePending && !SwapRequested) return;
        runAt = Time.realtimeSinceStartup + 1.2f;
        if (scheduled) return;
        scheduled = true;
        ScheduleCheck(1.2f);
    }

    private static void ScheduleCheck(float delay)
    {
        _ = new LateTask(() =>
        {
            if (!GameStates.IsInGame || CustomWinnerHolder.WinnerTeam != CustomWinner.Default
                || MeetingHud.Instance != null) // もう次の会議
            {
                scheduled = false;
                return;
            }
            // 追放画面の途中・最後に呼ばれてから 1.2 秒たっていない時は待つ
            if (ExileController.Instance != null || !GameStates.IsInTask || Time.realtimeSinceStartup < runAt)
            {
                ScheduleCheck(0.3f);
                return;
            }
            scheduled = false;
            if (promotePending)
            {
                promotePending = false;
                SwapRequested = false;
                Promote();
                return;
            }
            if (!SwapRequested) return;
            SwapRequested = false;
            Swap();
        }, delay, "SignalAndCalling.AfterMeeting");
    }

    /// <summary>コーリングをシグナルにする(シグナルが死んだ時)</summary>
    private static void Promote()
    {
        var calling = CallingPlayer;
        if (calling == null || !calling.IsAlive() || !calling.Is(CustomRoles.Calling)) return;
        if (!GameStates.IsInGame || CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
        // キルの直後に会議になった時は、会議の後に回す(会議中に役職を変えると暗転対策とぶつかる)
        if (MeetingHud.Instance != null || ExileController.Instance != null)
        {
            promotePending = true;
            return;
        }
        Changing = true;
        calling.RpcSetCustomRole(CustomRoles.Signal);
        Changing = false;
        calling.RpcSetRoleNormal(RoleTypes.Impostor, true);
        AfterRoleChange(calling);
        Utils.SendMessageAutoSplit("シグナルが死んだので、あなたがシグナル(インポスター)になりました。", true, Title, calling.PlayerId);
        Logger.Info($"{calling.GetNameWithRole()} がシグナルになった", nameof(SignalAndCalling));
    }

    /// <summary>シグナルとコーリングを入れ替える</summary>
    private static void Swap()
    {
        var signal = SignalPlayer;
        var calling = CallingPlayer;
        if (signal == null || calling == null || !signal.IsAlive() || !calling.IsAlive()) return;
        if (!signal.Is(CustomRoles.Signal) || !calling.Is(CustomRoles.Calling)) return;

        Changing = true;
        signal.RpcSetCustomRole(CustomRoles.Calling);
        calling.RpcSetCustomRole(CustomRoles.Signal);
        Changing = false;
        // バニラの役職も入れ替える(全員の画面で、新しいシグナルがインポスター・新しいコーリングがエンジニア/クルー)
        calling.RpcSetRoleNormal(RoleTypes.Impostor, true);
        signal.RpcSetRoleNormal(Calling.BaseRole, true);
        AfterRoleChange(calling, signal);

        Utils.SendMessageAutoSplit("交換要請により、あなたはシグナル(インポスター)になりました。", true, Title, calling.PlayerId);
        Utils.SendMessageAutoSplit("交換要請により、あなたはコーリング(マッドメイト)になりました。", true, Title, signal.PlayerId);
        Logger.Info($"交換: 新シグナル {calling.GetNameWithRole()} / 新コーリング {signal.GetNameWithRole()}", nameof(SignalAndCalling));
    }

    private static void AfterRoleChange(params PlayerControl[] players)
    {
        // 自分の画面でだけインポスターの人(ジャッカルなど)には、新しいシグナルをクルーに見せる
        StandardIntroHelper.HideImpostorsFromDesyncPlayers();
        foreach (var pc in players)
        {
            pc.ResetKillCooldown();
            pc.MarkDirtySettings();
        }
        _ = new LateTask(() =>
        {
            foreach (var pc in players)
                if (pc != null && pc.IsAlive() && pc.Is(CustomRoles.Signal)) pc.SetKillCooldown();
        }, 0.3f, "SignalAndCalling.KillCooldown");
        Utils.NotifyRoles(ForceLoop: true);
    }
}
