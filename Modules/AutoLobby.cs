using System.Linq;
using HarmonyLib;
using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Modules;

/// <summary>
/// 自動スタート・試合後に自動で部屋に戻る (TOH_Zero・TownOfHost-hamo を参考)
/// どちらもホストだけの機能。設定は MOD の設定の「自動機能」にある(全プリセット共通)
/// </summary>
public static class AutoLobby
{
    public static OptionItem AutoStart;
    public static OptionItem AutoStartTime;
    public static OptionItem AutoStartMinPlayers;
    public static OptionItem AutoStartWhenFull;
    public static OptionItem AutoStartFullDelay;
    public static OptionItem AutoStartOnlyGM;
    public static OptionItem AutoReturn;
    public static OptionItem AutoReturnOnlyGM;

    private static readonly Color32 HeadColor = new(0, 193, 255, 255);

    public static void SetupCustomOption(int id)
    {
        TextOptionItem.Create(id, "Head.AutoLobby", TabGroup.ModMainSettings).SetColor(HeadColor).SetGameMode(CustomGameMode.All);
        AutoStart = BooleanOptionItem.Create(id + 10, "AutoStart", false, TabGroup.ModMainSettings, true)
            .SetColor(HeadColor).SetGameMode(CustomGameMode.All);
        AutoStartTime = IntegerOptionItem.Create(id + 11, "AutoStartTime", new(30, 570, 30), 180, TabGroup.ModMainSettings, true)
            .SetParent(AutoStart).SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        AutoStartMinPlayers = IntegerOptionItem.Create(id + 12, "AutoStartMinPlayers", new(4, 15, 1), 4, TabGroup.ModMainSettings, true)
            .SetParent(AutoStart).SetValueFormat(OptionFormat.Players).SetGameMode(CustomGameMode.All);
        AutoStartWhenFull = BooleanOptionItem.Create(id + 13, "AutoStartWhenFull", false, TabGroup.ModMainSettings, true)
            .SetParent(AutoStart).SetGameMode(CustomGameMode.All);
        AutoStartFullDelay = IntegerOptionItem.Create(id + 14, "AutoStartFullDelay", new(5, 120, 5), 20, TabGroup.ModMainSettings, true)
            .SetParent(AutoStartWhenFull).SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        AutoStartOnlyGM = BooleanOptionItem.Create(id + 15, "AutoStartOnlyGM", false, TabGroup.ModMainSettings, true)
            .SetParent(AutoStart).SetGameMode(CustomGameMode.All);

        AutoReturn = BooleanOptionItem.Create(id + 20, "AutoReturnLobby", false, TabGroup.ModMainSettings, true)
            .SetColor(HeadColor).SetGameMode(CustomGameMode.All);
        AutoReturnOnlyGM = BooleanOptionItem.Create(id + 21, "AutoReturnOnlyGM", false, TabGroup.ModMainSettings, true)
            .SetParent(AutoReturn).SetGameMode(CustomGameMode.All);
    }

    // ===== 自動スタート =====
    private const float JoinSettleSeconds = 5f; // 人が出入りしてから、この秒数は始めない
    private static int lastPlayerCount;
    private static float lastCountChangedAt;
    private static float fullSince = -1f;

    private static bool AutoStartEnabled
        => AutoStart?.GetBool() == true && (!AutoStartOnlyGM.GetBool() || Options.EnableGM.GetBool());

    /// <summary>ロビーに入った(部屋を立てた・試合から戻った)時に呼ぶ</summary>
    public static void OnLobbyStart()
    {
        lastPlayerCount = 0;
        lastCountChangedAt = Time.time;
        fullSince = -1f;
    }

    /// <summary>自動スタートまでの残り秒数(自動スタートしない時は null)</summary>
    public static float? SecondsUntilAutoStart(float lobbyTimer)
    {
        if (!AutoStartEnabled) return null;
        var left = AutoStartTime.GetInt() - (600f - lobbyTimer);
        if (fullSince >= 0f) left = Mathf.Min(left, AutoStartFullDelay.GetInt() - (Time.time - fullSince));
        return Mathf.Max(0f, left);
    }

    /// <summary>GameStartManager.Update の後に毎フレーム呼ぶ</summary>
    public static void Update(GameStartManager gsm, float lobbyTimer)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsLobby || gsm == null) return;
        if (!AutoStartEnabled) { fullSince = -1f; return; }

        var count = PlayerControl.AllPlayerControls.Count;
        if (count != lastPlayerCount)
        {
            lastPlayerCount = count;
            lastCountChangedAt = Time.time;
        }
        // 満員になったら早める
        var full = AutoStartWhenFull.GetBool() && count >= GameOptionsManager.Instance.CurrentGameOptions.MaxPlayers;
        if (!full) fullSince = -1f;
        else if (fullSince < 0f) fullSince = Time.time;

        var left = SecondsUntilAutoStart(lobbyTimer);
        if (left == null || left > 0f) return;
        if (!IsReadyToStart(gsm, count)) return; // 準備ができるまで待つ(できたらすぐ始まる)

        Logger.Info($"自動スタート ({count}人)", nameof(AutoLobby));
        fullSince = -1f;
        gsm.BeginGame();
    }

    private static bool IsReadyToStart(GameStartManager gsm, int count)
    {
        if (count < AutoStartMinPlayers.GetInt()) return false;
        if (Time.time - lastCountChangedAt < JoinSettleSeconds) return false;
        if (gsm.startState != GameStartManager.StartingStates.NotStarting) return false;
        // バージョン違いの人がいる時などはスタートボタンが消えているので始めない
        if (gsm.StartButton != null && !gsm.StartButton.gameObject.activeSelf) return false;
        return PlayerControl.AllPlayerControls.ToArray().All(pc =>
            pc != null && pc.Data != null && !pc.Data.Disconnected
            && pc.Data.DefaultOutfit != null && pc.Data.DefaultOutfit.ColorId >= 0);
    }

    // ===== 試合後に自動で部屋に戻る =====
    private static bool returnScheduled;
    private const int MaxReturnAttempts = 3;

    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.ShowButtons))]
    private static class AutoReturnPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost || AutoReturn?.GetBool() != true) return;
            if (AutoReturnOnlyGM.GetBool() && !Options.EnableGM.GetBool()) return;
            if (returnScheduled) return;
            returnScheduled = true;
            // 今のアモアスは結果画面に「続行」ボタンがあり、それを押さないと「戻る」が効かないので先に押す
            _ = new LateTask(() => PressContinue(), 0.5f, "AutoReturnLobby.Continue");
            TryReturn(1);
        }
    }

    /// <summary>「続行」が出ていれば押す。押した時は true</summary>
    private static bool PressContinue()
    {
        if (!AmongUsClient.Instance.AmHost || GameStates.IsLobby) return false;
        var nav = DestroyableSingleton<EndGameNavigation>.Instance;
        if (nav == null || nav.ContinueButton == null) return false;
        try
        {
            if (!nav.ContinueButton.gameObject.activeInHierarchy) return false; // もう押されている
            var button = nav.ContinueButton.GetComponent<PassiveButton>();
            if (button == null) return false;
            button.OnClick.Invoke();
            Logger.Info("結果画面の「続行」を自動で押しました", nameof(AutoLobby));
            return true;
        }
        catch (System.Exception ex)
        {
            Logger.Warn($"「続行」を押せませんでした: {ex.Message}", nameof(AutoLobby));
            return false;
        }
    }

    // まれに失敗することがあるので、少し間を空けて数回やり直す
    private static void TryReturn(int attempt)
    {
        _ = new LateTask(() =>
        {
            var nav = DestroyableSingleton<EndGameNavigation>.Instance;
            if (!AmongUsClient.Instance.AmHost || GameStates.IsLobby || nav == null)
            {
                returnScheduled = false;
                return;
            }
            try
            {
                // まだ「続行」が残っていたら先に押し、画面が切り替わってから「戻る」を押す
                if (PressContinue())
                {
                    TryReturn(attempt);
                    return;
                }
                nav.NextGame();
                returnScheduled = false;
                Logger.Info("試合が終わったので部屋に戻りました", nameof(AutoLobby));
            }
            catch (System.Exception ex)
            {
                if (attempt < MaxReturnAttempts)
                {
                    Logger.Warn($"部屋に戻れませんでした({attempt}回目)。やり直します: {ex.Message}", nameof(AutoLobby));
                    TryReturn(attempt + 1);
                }
                else
                {
                    Logger.Error($"部屋に戻れませんでした: {ex}", nameof(AutoLobby));
                    returnScheduled = false;
                }
            }
        }, attempt == 1 ? 1.5f : 1f, "AutoReturnLobby");
    }
}
