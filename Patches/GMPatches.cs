using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

using TownOfHostY.Modules;
using TownOfHostY.Roles.Core;

namespace TownOfHostY.Patches;

/// <summary>
/// GM の詳細設定の動き (TOH_Zero・TownOfHost-hamo を参考)
/// ・タスク一覧を隠す / 全体のタスク進捗を出す
/// ・会議が始まったらチャットを自動で開く
/// ・タスクターンが始まったら自動で憑依ボタンを押す
/// </summary>
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class GMHudPatch
{
    public static void Postfix(HudManager __instance)
    {
        if (!AmongUsClient.Instance.AmHost || !AmongUsClient.Instance.IsGameStarted || !GMSettings.IsLocalGM) return;
        if (GMSettings.HideTaskPanel.GetBool() && __instance.TaskPanel != null && __instance.TaskPanel.gameObject.activeSelf)
            __instance.TaskPanel.gameObject.SetActive(false);
    }
}

[HarmonyPatch(typeof(TaskPanelBehaviour), nameof(TaskPanelBehaviour.SetTaskText))]
public static class GMTaskPanelPatch
{
    public static void Postfix(TaskPanelBehaviour __instance)
    {
        if (!AmongUsClient.Instance.AmHost || GameStates.IsLobby || !GMSettings.IsLocalGM) return;
        if (!GMSettings.ShowOverallTaskProgress.GetBool()) return;
        var (completed, all) = Utils.GetTasksState();
        var comms = Utils.IsActive(SystemTypes.Comms);
        var text = comms ? "?" : completed.ToString();
        __instance.taskText.text = Utils.ColorString(Color.yellow, $"全体のタスク: {text}/{all}") + "\n" + __instance.taskText.text;
    }
}

/// <summary>会議が始まったら GM のチャットを自動で開く(開けるようになるまで少しやり直す)</summary>
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
public static class GMAutoChatPatch
{
    private const float TryInterval = 0.25f;
    private const float GiveUpSeconds = 8f;

    public static void Postfix()
    {
        if (!AmongUsClient.Instance.AmHost || !GMSettings.IsLocalGM || !GMSettings.AutoChat.GetBool()) return;
        var hud = DestroyableSingleton<HudManager>.Instance;
        if (hud == null) return;
        hud.StartCoroutine(CoOpenChat().WrapToIl2Cpp());
    }

    private static System.Collections.IEnumerator CoOpenChat()
    {
        var waited = 0f;
        while (waited < GiveUpSeconds)
        {
            if (MeetingHud.Instance == null) yield break;
            var chat = DestroyableSingleton<HudManager>.Instance?.Chat;
            if (chat != null)
            {
                if (chat.IsOpenOrOpening) yield break;
                var buttonReady = chat.chatButton == null || chat.chatButton.gameObject.activeInHierarchy;
                if (chat.gameObject.activeInHierarchy && buttonReady)
                {
                    chat.Toggle();
                    Logger.Info("会議が始まったのでチャットを開きました", "GMAutoChat");
                }
            }
            yield return new WaitForSeconds(TryInterval);
            waited += TryInterval;
        }
    }
}

/// <summary>
/// タスクターンが始まったら、GM の憑依ボタンを 1 回だけ自動で押す。
/// 誰に憑依するかは、開いた画面で自分で切り替えられる(設定でインポスターを優先して選ぶ)
/// </summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class GMAutoPossessPatch
{
    private const float RoundStartDelay = 5f;
    private const float AfterMeetingDelay = 3f;
    private const float AirshipDelay = 15f; // エアシップはスポーン選択があるので長めに待つ
    private const float VerifySeconds = 1.5f;
    private const int MaxAttempts = 5;

    private static bool wasInTask;
    private static bool firstRound = true;
    private static float windowStart;
    private static bool done;
    private static int attempts;
    private static float clickedAt = -1f;

    public static void ResetForNewGame()
    {
        wasInTask = false;
        firstRound = true;
        done = false;
    }

    private static bool InTask => GameStates.IsInTask && Main.introDestroyed && MeetingHud.Instance == null && !ExileController.Instance;

    private static bool IsHauntMenuOpen()
    {
        try { return Minigame.Instance != null && Minigame.Instance.TryCast<HauntMenuMinigame>() != null; }
        catch { return false; }
    }

    public static void Postfix(PlayerControl __instance)
    {
        if (__instance != PlayerControl.LocalPlayer || !AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInGame || !GMSettings.IsGM(__instance) || !GMSettings.AutoPossess.GetBool()) return;

        var inTask = InTask;
        if (inTask && !wasInTask)
        {
            // タスクターンが始まった
            windowStart = Time.realtimeSinceStartup;
            done = false;
            attempts = 0;
            clickedAt = -1f;
        }
        else if (!inTask && wasInTask) firstRound = false;
        wasInTask = inTask;
        if (!inTask || done) return;

        var delay = (MapNames)Main.NormalOptions.MapId == MapNames.Airship ? AirshipDelay
            : firstRound ? RoundStartDelay : AfterMeetingDelay;
        if (Time.realtimeSinceStartup - windowStart < delay) return;

        if (IsHauntMenuOpen()) { done = true; return; }
        if (clickedAt >= 0f && Time.realtimeSinceStartup - clickedAt < VerifySeconds) return;
        if (attempts >= MaxAttempts) { done = true; return; }

        var hud = HudManager.Instance;
        var button = hud?.AbilityButton;
        if (button == null || !button.gameObject.activeInHierarchy) return;
        if (!button.canInteract) button.isCoolingDown = false;
        // 憑依できる生存者がいないのに押すとバニラ側でエラーになる
        if (!Main.AllAlivePlayerControls.Any(p => p.PlayerId != __instance.PlayerId)) return;

        button.DoClick();
        attempts++;
        clickedAt = Time.realtimeSinceStartup;
        Logger.Info($"憑依ボタンを自動で押しました({attempts}回目)", "GMAutoPossess");
        if (GMSettings.AutoPossessPreferImpostor.GetBool())
            hud.StartCoroutine(CoPreferImpostor().WrapToIl2Cpp());
    }

    private static System.Collections.IEnumerator CoPreferImpostor()
    {
        for (int i = 0; i < 30; i++)
        {
            var menu = Object.FindObjectOfType<HauntMenuMinigame>(true);
            if (menu != null)
            {
                var impostor = Main.AllAlivePlayerControls.FirstOrDefault(p => p.Is(CustomRoleTypes.Impostor));
                if (impostor != null) menu.HauntTarget = impostor;
                yield break;
            }
            yield return null;
        }
    }
}
