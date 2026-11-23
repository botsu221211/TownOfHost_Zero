using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Roles.Neutral;

/// <summary>
/// チャッター (TownOfHost-Pko より移植)
/// ・会議で設定時間チャットしないと死亡する(会議が始まって 10 秒は猶予)
/// ・/cmd から始まる文(コマンド)はチャットに数えない
/// ・試合が終わった時に生き残っていれば単独勝利(勝利優先度で比べる)
/// </summary>
public sealed class Chatter : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Chatter),
            player => new Chatter(player),
            CustomRoles.Chatter,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 1400,
            SetupOptionItem,
            "チャッター",
            "#FF66B2"
        );
    public Chatter(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        chatTimeLimit = OptionChatTimeLimit.GetFloat();
        timeSinceLastChat = 0f;
        meetingActiveTimer = 0f;
    }

    private static OptionItem OptionWinPriority;
    private static OptionItem OptionChatTimeLimit;
    enum OptionName
    {
        ChatterWinPriority,
        ChatterTimeLimit,
    }
    private const float GraceSeconds = 10f;

    private static float chatTimeLimit;
    private float timeSinceLastChat;
    private float meetingActiveTimer;

    public static int WinPriority => OptionWinPriority?.GetInt() ?? 15;

    private static void SetupOptionItem()
    {
        OptionWinPriority = IntegerOptionItem.Create(RoleInfo, 9, OptionName.ChatterWinPriority, new(1, 50, 1), 15, false);
        OptionChatTimeLimit = FloatOptionItem.Create(RoleInfo, 10, OptionName.ChatterTimeLimit, new(5f, 120f, 2.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void OnStartMeeting()
    {
        timeSinceLastChat = 0f;
        meetingActiveTimer = 0f;
    }

    private void ResetTimer() => timeSinceLastChat = 0f;

    private void UpdateMeetingTimer()
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsMeeting) return;
        if (Player == null || Player.Data == null || Player.Data.Disconnected || !Player.IsAlive()) return;
        // 投票結果の表示が始まったら数えない
        if (MeetingHud.Instance == null || (int)MeetingHud.Instance.state >= 4) return; // 4 = Results(投票結果の表示)、5 = Proceeding

        meetingActiveTimer += Time.deltaTime;
        if (meetingActiveTimer <= GraceSeconds) return;

        timeSinceLastChat += Time.deltaTime;
        if (timeSinceLastChat < chatTimeLimit) return;
        timeSinceLastChat = float.NegativeInfinity; // 二度処理しない

        var chatterId = Player.PlayerId;
        Logger.Info($"{Player.GetNameWithRole()} は {chatTimeLimit} 秒チャットしなかったので死亡", nameof(Chatter));
        Utils.SendMessageAutoSplit($"{Utils.ColorString(Main.PlayerColors.TryGetValue(chatterId, out var c) ? c : Color.white, Player.Data.PlayerName)} が急に意識を失った。\nどうしたんだろうな。",
            true, "<color=#FF66B2>【チャッター】</color>");

        _ = new LateTask(() =>
        {
            if (!GameStates.IsMeeting || !AmongUsClient.Instance.AmHost) return;
            var chatter = Utils.GetPlayerById(chatterId);
            if (chatter == null || chatter.Data == null || chatter.Data.Disconnected || !chatter.IsAlive()) return;
            var state = PlayerState.GetByPlayerId(chatterId);
            if (state == null || state.IsDead) return;

            state.DeathReason = CustomDeathReason.Suicide;
            CustomRoleManager.CheckMurderInfos[chatterId] = new MurderInfo(chatter, chatter, chatter, chatter);
            chatter.RpcMurderPlayer(chatter, true);
            // 自分への票・自分の票を取り消す
            foreach (var va in MeetingHud.Instance.playerStates)
            {
                if (va.VotedForId != chatterId && va.PlayerId != chatterId) continue;
                MeetingHud.Instance.RpcClearVote(va.PlayerId);
            }
        }, 0.3f, "ChatterKill");
    }

    /// <summary>
    /// ゲーム終了時に呼ぶ。生き残っているチャッターがいれば、勝利優先度を比べて単独勝利する
    /// </summary>
    public static bool CheckWinOnGameEnd(CustomWinner currentWinner)
    {
        if (currentWinner is CustomWinner.Chatter or CustomWinner.Draw or CustomWinner.None) return false;
        var ids = Main.AllAlivePlayerControls.Where(pc => pc.Is(CustomRoles.Chatter)).Select(pc => pc.PlayerId).ToList();
        if (ids.Count == 0) return false;
        if (!CustomWinnerHolder.SetPriorityWinner(CustomWinner.Chatter, WinPriority, ids)) return false;
        Logger.Info($"生き残ったチャッターが勝利 (優先度 {WinPriority})", nameof(Chatter));
        return true;
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    private static class AddChatPatch
    {
        public static void Postfix(PlayerControl sourcePlayer, string chatText)
        {
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsMeeting) return;
            if (sourcePlayer == null || !sourcePlayer.IsAlive()) return;
            if (sourcePlayer.GetRoleClass() is not Chatter chatter) return;
            // /cmd から始まる文はコマンドなので数えない
            if (chatText != null && chatText.TrimStart().StartsWith("/cmd")) return;
            chatter.ResetTimer();
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
    private static class MeetingUpdatePatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsMeeting) return;
            foreach (var chatter in CustomRoleManager.AllActiveRoles.Values.OfType<Chatter>().ToArray())
                chatter.UpdateMeetingTimer();
        }
    }
}
