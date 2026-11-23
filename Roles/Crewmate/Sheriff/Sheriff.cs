using System.Collections.Generic;
using System.Linq;
using Hazel;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using TownOfHostY.Roles.Neutral;
using static TownOfHostY.Translator;

namespace TownOfHostY.Roles.Crewmate;
/// <summary>
/// シェリフ。設定「タスクモードで始める」がオンの時は TownOfHost-Pko と同じ仕様:
/// ・タスクモードとシェリフ(キル)モードを、ペットを撫でて切り替える
/// ・タスクモードの間はタスクができ、キルボタンはない。キルクールはタスクモードの間だけ減る
/// ・キルしたら、または会議が始まったらタスクモードに戻る
/// </summary>
public sealed class Sheriff : RoleBase, IKiller, ISchrodingerCatOwner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Sheriff),
            player => new Sheriff(player),
            CustomRoles.Sheriff,
            () => RequiresTasks ? RoleTypes.Crewmate : RoleTypes.Impostor,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewSheriff + 0,
            SetupOptionItem,
            "シェリフ",
            "#f8cd46",
            isDesyncImpostor: true,
            introSound: () => GetIntroSound(RoleTypes.Crewmate)
        );
    public Sheriff(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => RequiresTasks ? HasTask.True : HasTask.False
    )
    {
        ShotLimit = ShotLimitOpt.GetInt();
        CurrentKillCooldown = KillCooldown.GetFloat();
        Taskmode = RequiresTasks;
        killReadyAt = 0f;
        ghostFixed = false;
    }

    private static OptionItem StartInTaskMode;
    /// <summary>タスクモードとシェリフモードを切り替える仕様か</summary>
    private static bool RequiresTasks => StartInTaskMode?.GetBool() ?? false;
    /// <summary>今タスクモードか</summary>
    public bool Taskmode;
    /// <summary>キルクールが明ける時刻(タスクモードの間だけ減らすので、止まっている間は残り秒数を持つ)</summary>
    private float killReadyAt;
    private float pausedLeft = -1f;
    private bool ghostFixed;
    private static float Now => Time.realtimeSinceStartup;

    private static OptionItem KillCooldown;
    private static OptionItem ShotLimitOpt;
    public static OptionItem IsInfoPoor;
    public static OptionItem IsClumsy;
    private static OptionItem CanKillAllAlive;
    public static OptionItem CanKillNeutrals;
    enum OptionName
    {
        SheriffShotLimit,
        SheriffIsInfoPoor,
        SheriffIsClumsy,
        SheriffCanKillAllAlive,
        SheriffCanKillNeutrals,
        SheriffCanKill,
        SheriffStartInTaskMode,
    }
    public static Dictionary<CustomRoles, OptionItem> KillTargetOptions = new();
    public static Dictionary<SchrodingerCat.TeamType, OptionItem> SchrodingerCatKillTargetOptions = new();
    public int ShotLimit = 0;
    public float CurrentKillCooldown = 30;
    public static readonly string[] KillOption =
    {
        "SheriffCanKillAll", "SheriffCanKillSeparately"
    };

    public SchrodingerCat.TeamType SchrodingerCatChangeTo => SchrodingerCat.TeamType.Crew;

    private static void SetupOptionItem()
    {
        KillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        ShotLimitOpt = IntegerOptionItem.Create(RoleInfo, 12, OptionName.SheriffShotLimit, new(1, 15, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        IsInfoPoor = BooleanOptionItem.Create(RoleInfo, 16, OptionName.SheriffIsInfoPoor, false, false);
        IsClumsy = BooleanOptionItem.Create(RoleInfo, 17, OptionName.SheriffIsClumsy, false, false);
        CanKillAllAlive = BooleanOptionItem.Create(RoleInfo, 15, OptionName.SheriffCanKillAllAlive, true, false);
        StartInTaskMode = BooleanOptionItem.Create(RoleInfo, 18, OptionName.SheriffStartInTaskMode, true, false);
        Options.OverrideTasksData.Create(RoleInfo, 19, StartInTaskMode);
        SetUpKillTargetOption(CustomRoles.Madmate, 13);
        CanKillNeutrals = StringOptionItem.Create(RoleInfo, 14, OptionName.SheriffCanKillNeutrals, KillOption, 0, false);
        SetUpNeutralOptions(30);
    }
    public static void SetUpNeutralOptions(int idOffset)
    {
        foreach (var neutral in CustomRolesHelper.AllStandardRoles.Where(x => x.IsNeutral()).ToArray())
        {
            if (neutral is CustomRoles.SchrodingerCat) continue;
            SetUpKillTargetOption(neutral, idOffset, true, CanKillNeutrals);
            idOffset++;
        }
        foreach (var catType in EnumHelper.GetAllValues<SchrodingerCat.TeamType>())
        {
            if ((byte)catType < 50)
            {
                continue;
            }
            SetUpSchrodingerCatKillTargetOption(catType, idOffset, true, CanKillNeutrals);
            idOffset++;
        }
    }
    public static void SetUpKillTargetOption(CustomRoles role, int idOffset, bool defaultValue = true, OptionItem parent = null)
    {
        var id = RoleInfo.ConfigId + idOffset;
        if (parent == null) parent = RoleInfo.RoleOption;
        var roleName = Utils.GetRoleName(role);
        Dictionary<string, string> replacementDic = new() { { "%role%", Utils.ColorString(Utils.GetRoleColor(role), roleName) } };
        KillTargetOptions[role] = BooleanOptionItem.Create(id, OptionName.SheriffCanKill + "%role%", defaultValue, RoleInfo.Tab, false).SetParent(parent);
        KillTargetOptions[role].ReplacementDictionary = replacementDic;
    }
    public static void SetUpSchrodingerCatKillTargetOption(SchrodingerCat.TeamType catType, int idOffset, bool defaultValue = true, OptionItem parent = null)
    {
        var id = RoleInfo.ConfigId + idOffset;
        parent ??= RoleInfo.RoleOption;
        // (%team%陣営)
        var inTeam = GetString("In%team%", new Dictionary<string, string>() { ["%team%"] = GetRoleString(catType.ToString()) });
        // シュレディンガーの猫(%team%陣営)
        var catInTeam = Utils.ColorString(SchrodingerCat.GetCatColor(catType), Utils.GetRoleName(CustomRoles.SchrodingerCat) + inTeam);
        Dictionary<string, string> replacementDic = new() { ["%role%"] = catInTeam };
        SchrodingerCatKillTargetOptions[catType] = BooleanOptionItem.Create(id, OptionName.SheriffCanKill + "%role%", defaultValue, RoleInfo.Tab, false).SetParent(parent);
        SchrodingerCatKillTargetOptions[catType].ReplacementDictionary = replacementDic;
    }
    public override void Add()
    {
        var playerId = Player.PlayerId;
        CurrentKillCooldown = KillCooldown.GetFloat();
        ShotLimit = ShotLimitOpt.GetInt();
        Taskmode = RequiresTasks;
        ResetKillTimer();
        Logger.Info($"{Utils.GetPlayerById(playerId)?.GetNameWithRole()} : 残り{ShotLimit}発", "Sheriff");
    }
    private void SendRPC()
    {
        using var sender = CreateSender(CustomRPC.SetSheriffShotLimit);
        sender.Writer.Write(ShotLimit);
        sender.Writer.Write(Taskmode);
    }
    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.SetSheriffShotLimit) return;

        ShotLimit = reader.ReadInt32();
        Taskmode = reader.ReadBoolean();
    }
    public float CalculateKillCooldown() => CanUseKillButton() ? CurrentKillCooldown : 0f;
    public bool CanUseKillButton()
        => CanUseSheriffMode()
        && !Taskmode;
    private bool CanUseSheriffMode()
        => Player.IsAlive()
        && (CanKillAllAlive.GetBool() || GameStates.AlreadyDied)
        && ShotLimit > 0;

    // ===== タスクモード / シェリフモード (TownOfHost-Pko と同じ仕様) =====
    /// <summary>キルクールの残り秒数(タスクモードの間だけ減る)</summary>
    private float KillCooldownLeft => pausedLeft >= 0f ? pausedLeft : Mathf.Max(0f, killReadyAt - Now);
    private void ResetKillTimer()
    {
        killReadyAt = Now + CurrentKillCooldown;
        pausedLeft = -1f;
    }

    public override bool UsePetAbility => RequiresTasks;
    public override float PetCooldown => CanUseSheriffMode() ? 0f : PetAbility.Unusable;
    public override void OnPet()
    {
        if (!RequiresTasks || !CanUseSheriffMode()) return;
        SwitchMode(!Taskmode);
    }

    /// <summary>モードを切り替え、本人の画面の役職(クルー/インポスター)とキルボタンを合わせる</summary>
    private void SwitchMode(bool toTaskmode)
    {
        if (!RequiresTasks) toTaskmode = false;
        Taskmode = toTaskmode;
        Logger.Info($"{Player.GetNameWithRole()} : {(Taskmode ? "タスク" : "シェリフ")}モード", "Sheriff");

        var clientId = Player.GetClientId();
        if (AmongUsClient.Instance.AmHost && Player.IsAlive() && clientId != -1 && Player != PlayerControl.LocalPlayer)
        {
            // 本人の画面でだけ役職を切り替える。シェリフモードの間はインポスターを仲間に見せないよう科学者にしておく
            Player.RpcSetRoleDesync(Taskmode ? RoleTypes.Crewmate : RoleTypes.Impostor, clientId);
            if (!Taskmode)
            {
                foreach (var pc in Main.AllAlivePlayerControls)
                {
                    if (pc == Player || pc.GetClientId() == -1) continue;
                    var roleType = pc.GetCustomRole().GetRoleTypes();
                    if (roleType is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.Viper
                        || pc.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true)
                        pc.RpcSetRoleDesync(RoleTypes.Scientist, clientId);
                }
            }
        }
        if (!Taskmode)
        {
            // 役職を変えるとキルクールが戻るので、少し後で残りのキルクールを合わせる
            var left = Mathf.Max(KillCooldownLeft, 0.1f);
            _ = new LateTask(() =>
            {
                if (Player != null && !Taskmode && Player.IsAlive()) Player.SetKillCooldown(Mathf.Max(KillCooldownLeft, 0.1f));
            }, 0.3f, "Sheriff.ModeKillCooldown");
        }
        Player.MarkDirtySettings();
        SendRPC();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        // 会議が始まったらタスクモードに戻す(会議の後は本人の画面もクルーに戻る)
        if (RequiresTasks && Player.IsAlive() && !Taskmode) SwitchMode(true);
    }
    public override void AfterMeetingTasks()
    {
        if (RequiresTasks) ResetKillTimer();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !RequiresTasks) return;
        if (!Player.IsAlive())
        {
            // 死んだらタスクができる幽霊にする(シェリフモードで死ぬとインポスターの幽霊になるため)
            if (!ghostFixed && Player != PlayerControl.LocalPlayer && Player.GetClientId() != -1)
            {
                ghostFixed = true;
                _ = new LateTask(() =>
                {
                    if (Player != null && GameStates.IsInGame) Player.RpcSetRoleDesync(RoleTypes.CrewmateGhost, Player.GetClientId());
                }, 1.5f, "Sheriff.GhostRole");
            }
            return;
        }
        // キルクールはタスクモードの間だけ減る。会議中・シェリフモードの間は止める
        var counting = Taskmode && GameStates.IsInTask && MeetingHud.Instance == null && ExileController.Instance == null;
        if (counting && pausedLeft >= 0f)
        {
            killReadyAt = Now + pausedLeft;
            pausedLeft = -1f;
        }
        else if (!counting && pausedLeft < 0f)
        {
            pausedLeft = Mathf.Max(0f, killReadyAt - Now);
        }
        var shown = Mathf.CeilToInt(KillCooldownLeft);
        if (shown != lastShownCooldown)
        {
            lastShownCooldown = shown;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
    }
    private int lastShownCooldown = -1;
    public bool CanUseImpostorVentButton() => false;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
    }
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide || !info.CanKill) return;
        (var killer, var target) = info.AttemptTuple;

        if (ShotLimit <= 0)
        {
            info.DoKill = false;
            return;
        }
        ShotLimit--;
        Logger.Info($"{killer.GetNameWithRole()} : 残り{ShotLimit}発", "Sheriff");
        SendRPC();
        if (!CanBeKilledBy(target))
        {
            PlayerState.GetByPlayerId(killer.PlayerId).DeathReason = CustomDeathReason.Misfire;
            killer.RpcMurderPlayer(killer);

            info.DoKill = false;
            return;
        }
        killer.ResetKillCooldown();
        // 撃ったらタスクモードに戻り、キルクールを数え直す
        if (RequiresTasks)
        {
            ResetKillTimer();
            _ = new LateTask(() =>
            {
                if (Player != null && Player.IsAlive() && GameStates.IsInTask) SwitchMode(true);
            }, 0.5f, "Sheriff.BackToTaskMode");
        }
    }
    public override string GetProgressText(bool comms = false)
    {
        var text = Utils.ColorString(CanUseSheriffMode() ? Color.yellow : Color.gray, $"〈{ShotLimit}〉");
        if (RequiresTasks && Player.IsAlive() && !GameStates.IsMeeting)
            text += Utils.ColorString(Color.yellow, Taskmode
                ? $" [タスク]<color=#ffffff>({Mathf.CeilToInt(KillCooldownLeft)})</color>"
                : " [シェリフ]");
        return text;
    }
    public static bool CanBeKilledBy(PlayerControl player)
    {
        var cRole = player.GetCustomRole();

        if (player.GetRoleClass() is SchrodingerCat schrodingerCat)
        {
            if (schrodingerCat.Team == SchrodingerCat.TeamType.None)
            {
                Logger.Warn($"シェリフ({player.GetRealName()})にキルされたシュレディンガーの猫のロールが変化していません", nameof(Sheriff));
                return false;
            }
            return schrodingerCat.Team switch
            {
                SchrodingerCat.TeamType.Mad => KillTargetOptions.TryGetValue(CustomRoles.Madmate, out var option) && option.GetBool(),
                SchrodingerCat.TeamType.Crew => false,
                _ => CanKillNeutrals.GetValue() == 0 || (SchrodingerCatKillTargetOptions.TryGetValue(schrodingerCat.Team, out var option) && option.GetBool()),
            };
        }

        return cRole.GetCustomRoleTypes() switch
        {
            CustomRoleTypes.Impostor => true,
            CustomRoleTypes.Madmate => KillTargetOptions.TryGetValue(CustomRoles.Madmate, out var option) && option.GetBool(),
            CustomRoleTypes.Neutral => CanKillNeutrals.GetValue() == 0 || !KillTargetOptions.TryGetValue(cRole, out var option) || option.GetBool(),
            _ => false,
        };
    }
    public void OnSchrodingerCatKill(SchrodingerCat schrodingerCat)
    {
        if (SchrodingerCat.ConsumeBullet)
        {
            ShotLimit--;
        }
    }
}