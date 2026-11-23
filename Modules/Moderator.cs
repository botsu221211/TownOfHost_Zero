using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostY.Modules;

/// <summary>
/// モデレーター (TownOfHost-hamo の Moderator を TOH_Zero 向けに移植・簡略化)
/// ・ホストが「/mod 対象」で、部屋の人をモデレーターに追加・解除する(もう一度打つと解除)
/// ・登録は TOHZ_DATA/moderator.txt に保存され、次に部屋を立てても引き継ぐ(フレンドコード / PUID で判定)
/// ・モデレーターはホストの代わりに、キック・BAN・廃村・会議の操作・開始などのコマンドを使える
/// ・ロビーでは名前の前に「モデレーター - 」が付く(試合が始まる時に外す)
/// </summary>
public static class Moderator
{
    private const string FilePath = "./TOHZ_DATA/moderator.txt";
    private const string NamePrefix = "<#ADE0EE>モデレーター</color> - ";
    private const string Title = "<color=#ADE0EE>【モデレーター】</color>";

    private sealed class Entry
    {
        public string FriendCode = "";
        public string Puid = "";
        public string Name = "";
    }
    private static readonly List<Entry> entries = new();
    private static bool loaded;
    /// <summary>試合開始のために名前から「モデレーター - 」を外している間は付け直さない</summary>
    private static bool namesStripped;
    private static float nextRefreshTime;

    // ───────────────────────── 登録の読み書き ─────────────────────────
    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        entries.Clear();
        try
        {
            Directory.CreateDirectory("./TOHZ_DATA");
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, "# フレンドコード,PUID,名前\n");
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var parts = line.Split(',', 3);
                var e = new Entry
                {
                    FriendCode = NormalizeFc(parts.ElementAtOrDefault(0)),
                    Puid = (parts.ElementAtOrDefault(1) ?? "").Trim(),
                    Name = (parts.ElementAtOrDefault(2) ?? "").Trim(),
                };
                if (e.FriendCode != "" || e.Puid != "") entries.Add(e);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"moderator.txt の読み込みに失敗: {ex.Message}", nameof(Moderator));
        }
    }
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory("./TOHZ_DATA");
            var lines = new List<string> { "# フレンドコード,PUID,名前" };
            lines.AddRange(entries.Select(e => $"{e.FriendCode},{e.Puid},{e.Name}"));
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception ex)
        {
            Logger.Warn($"moderator.txt の保存に失敗: {ex.Message}", nameof(Moderator));
        }
    }
    private static string NormalizeFc(string fc)
        => string.IsNullOrWhiteSpace(fc) ? "" : fc.Trim().Replace(':', '#').ToUpperInvariant();

    private static Entry FindEntry(PlayerControl pc)
    {
        EnsureLoaded();
        var client = pc?.GetClient();
        if (client == null) return null;
        var fc = NormalizeFc(client.FriendCode);
        var puid = client.ProductUserId ?? "";
        return entries.FirstOrDefault(e =>
            (fc != "" && e.FriendCode == fc) || (puid != "" && e.Puid != "" && string.Equals(e.Puid, puid, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsModerator(PlayerControl pc) => pc != null && !IsHost(pc) && FindEntry(pc) != null;
    /// <summary>ホストかモデレーターか(霊界からの個人秘匿など)</summary>
    public static bool IsHostOrModerator(PlayerControl pc) => IsHost(pc) || IsModerator(pc);
    private static bool IsHost(PlayerControl pc)
        => pc != null && (pc.PlayerId == PlayerControl.LocalPlayer?.PlayerId || pc.GetClientId() == AmongUsClient.Instance.HostId);

    // ───────────────────────── コマンド ─────────────────────────
    /// <summary>モデレーター用のコマンド(これ以外は今まで通り)</summary>
    private static bool IsModeratorCommand(string cmd)
        => cmd is "/kick" or "/ban" or "/say" or "/end" or "/廃村" or "/mf" or "/fm" or "/start" or "/cs" or "/bo";

    /// <summary>
    /// ホストのチャット・ゲストのチャットの両方から呼ぶ。モデレーター関係のコマンドなら処理して true を返す。
    /// ホスト自身の /say /end /mf /bo は今まで通りホスト用の処理を使うので、ここでは扱わない
    /// </summary>
    public static bool TryHandleCommand(PlayerControl sender, string[] args)
    {
        if (!AmongUsClient.Instance.AmHost || sender == null || args == null || args.Length == 0) return false;
        var cmd = args[0].ToLowerInvariant();
        var fromHost = IsHost(sender);

        if (cmd == "/mod")
        {
            if (!fromHost)
            {
                Reply(sender, "/mod はホストだけが使えます。");
                return true;
            }
            HandleMod(sender, args);
            return true;
        }
        if (!IsModeratorCommand(cmd)) return false;
        if (fromHost && cmd is "/say" or "/end" or "/廃村" or "/mf" or "/bo") return false;
        if (!fromHost && !IsModerator(sender))
        {
            Reply(sender, "このコマンドはモデレーターだけが使えます。");
            return true;
        }

        Logger.Info($"{sender.GetNameWithRole()} がモデレーターコマンド: {string.Join(" ", args)}", nameof(Moderator));
        switch (cmd)
        {
            case "/kick": HandleKick(sender, args, false); break;
            case "/ban": HandleKick(sender, args, true); break;
            case "/say": HandleSay(sender, args); break;
            case "/end":
            case "/廃村":
                if (!AutoAbort.EndAsDraw($"モデレーター {PlainName(sender)} の /end"))
                    Reply(sender, "廃村は試合中(役職の割り当て後)だけ使えます。");
                break;
            case "/mf":
                if (GameStates.IsMeeting && MeetingHud.Instance != null) MeetingHud.Instance.RpcClose();
                else Reply(sender, "/mf は会議中だけ使えます。");
                break;
            case "/fm": HandleForceMeeting(sender); break;
            case "/start": HandleStart(sender, args); break;
            case "/cs":
                if (GameStates.IsCountDown) GameStartManager.Instance.ResetStartState();
                else Reply(sender, "/cs は開始のカウントダウン中だけ使えます。");
                break;
            case "/bo":
                if (GameStates.InGame) Main.AllPlayerControls.Do(pc => pc.KillFlash());
                break;
        }
        return true;
    }

    private static void Reply(PlayerControl to, string text)
    {
        if (to == null) return;
        Utils.SendMessageAutoSplit(text, true, Title, to.PlayerId);
    }

    private static string PlainName(PlayerControl pc)
        => StripPrefix(pc?.Data?.PlayerName ?? pc?.name ?? "").RemoveHtmlTags().Trim();

    /// <summary>/mod: 引数なしで一覧、対象を指定するとモデレーターに追加(すでにモデレーターなら解除)</summary>
    private static void HandleMod(PlayerControl host, string[] args)
    {
        EnsureLoaded();
        var key = string.Join(" ", args.Skip(1)).Trim();
        if (key == "")
        {
            var list = entries.Count == 0
                ? "(まだいません)"
                : string.Join("\n", entries.Select(e => $"・{(e.Name == "" ? "(名前不明)" : e.Name)}  {e.FriendCode}"));
            Reply(host, $"モデレーター一覧:\n{list}\n\n/mod <ID・色・名前・フレンドコード> で追加・解除できます。");
            return;
        }

        var target = FindTarget(key);
        if (target != null)
        {
            if (IsHost(target))
            {
                Reply(host, "ホストはモデレーターにできません。");
                return;
            }
            var existing = FindEntry(target);
            if (existing != null)
            {
                entries.Remove(existing);
                Save();
                Reply(host, $"{PlainName(target)} をモデレーターから外しました。");
                Utils.SendMessageAutoSplit("あなたはモデレーターから外されました。", true, Title, target.PlayerId);
            }
            else
            {
                var client = target.GetClient();
                var fc = NormalizeFc(client?.FriendCode);
                var puid = client?.ProductUserId ?? "";
                if (fc == "" && puid == "")
                {
                    Reply(host, "フレンドコードが取れないため追加できません。");
                    return;
                }
                entries.Add(new Entry { FriendCode = fc, Puid = puid, Name = PlainName(target).Replace(',', ' ') });
                Save();
                Reply(host, $"{PlainName(target)} をモデレーターにしました。");
                Utils.SendMessageAutoSplit("あなたはモデレーターになりました。\n/h でモデレーター用のコマンドを確認できます。", true, Title, target.PlayerId);
            }
            RefreshNames(force: true);
            return;
        }

        // 部屋にいない人: 登録済みのフレンドコードか名前で解除
        var fcKey = NormalizeFc(key);
        var removed = entries.RemoveAll(e => e.FriendCode == fcKey || string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Save();
            Reply(host, $"{key} をモデレーターから外しました。");
        }
        else Reply(host, $"「{key}」に当てはまる人が見つかりません。");
    }

    /// <summary>ID・色・フレンドコード・名前の順で、部屋の人を探す</summary>
    private static PlayerControl FindTarget(string key)
    {
        key = key.Trim();
        if (byte.TryParse(key, out var id))
        {
            var byId = Utils.GetPlayerById(id);
            if (byId != null) return byId;
        }
        var fc = NormalizeFc(key);
        var byFc = Main.AllPlayerControls.FirstOrDefault(pc => NormalizeFc(pc.GetClient()?.FriendCode) == fc && fc != "");
        if (byFc != null) return byFc;
        var byColor = ChatCommands.FindPlayerByColorName(key);
        if (byColor != null) return byColor;
        return Main.AllPlayerControls.FirstOrDefault(pc => string.Equals(PlainName(pc), key, StringComparison.OrdinalIgnoreCase));
    }

    private static void HandleKick(PlayerControl sender, string[] args, bool ban)
    {
        var label = ban ? "/ban" : "/kick";
        if (args.Length < 2)
        {
            Reply(sender, $"使い方: {label} <ID・色・名前>");
            return;
        }
        var target = FindTarget(string.Join(" ", args.Skip(1)));
        if (target == null)
        {
            Reply(sender, "その人が見つかりません。");
            return;
        }
        if (target.PlayerId == sender.PlayerId)
        {
            Reply(sender, "自分には使えません。");
            return;
        }
        if (IsHost(target) || IsModerator(target))
        {
            Reply(sender, "ホストとモデレーターはキック・BANできません。");
            return;
        }
        var clientId = target.GetClientId();
        if (clientId == -1)
        {
            Reply(sender, "その人の接続が見つかりません。");
            return;
        }
        var name = PlainName(target);
        AmongUsClient.Instance.KickPlayer(clientId, ban);
        Utils.SendMessageAutoSplit($"{name} が{(ban ? "BAN" : "キック")}されました。", true, Title);
    }

    private static void HandleSay(PlayerControl sender, string[] args)
    {
        if (args.Length < 2)
        {
            Reply(sender, "使い方: /say <内容>");
            return;
        }
        var body = string.Join(" ", args.Skip(1)).Replace("<", "＜").Replace(">", "＞");
        Utils.SendMessageAutoSplit(body, true, $"<color=#ADE0EE>{PlainName(sender)}(モデレーター)からの伝言</color>");
    }

    private static void HandleForceMeeting(PlayerControl sender)
    {
        if (!GameStates.IsInTask || !Main.IsSetRoleFinished || ExileController.Instance != null || MeetingHud.Instance != null)
        {
            Reply(sender, "/fm はタスク中だけ使えます。");
            return;
        }
        // 本編は死んだ人の通報を受け付けないので、送った人が死んでいれば生きている誰かを通報者にする
        var reporter = sender.IsAlive() ? sender : Main.AllAlivePlayerControls.FirstOrDefault();
        if (reporter == null) return;
        reporter.ReportDeadBody(reporter.Data);
    }

    private static void HandleStart(PlayerControl sender, string[] args)
    {
        if (!GameStates.IsLobby || GameStartManager.Instance == null)
        {
            Reply(sender, "/start はロビーでだけ使えます。");
            return;
        }
        var sec = 5;
        if (args.Length >= 2 && !int.TryParse(args[1], out sec))
        {
            Reply(sender, "使い方: /start <秒数(0〜600)>");
            return;
        }
        sec = Mathf.Clamp(sec, 0, 600);
        var gsm = GameStartManager.Instance;
        gsm.countDownTimer = Mathf.Max(sec, 0.1f);
        gsm.startState = GameStartManager.StartingStates.Countdown;
    }

    /// <summary>/h に足すモデレーター用コマンドの説明</summary>
    public static string HelpText()
        => "\n\n<color=#ADE0EE>【モデレーター用】</color>"
         + "\n/kick <ID・色・名前> - キックする"
         + "\n/ban <ID・色・名前> - BANする"
         + "\n/say <内容> - 全員に伝言を出す"
         + "\n/end - 廃村にする(試合中)"
         + "\n/mf - 会議を終わらせる(会議中)"
         + "\n/fm - 強制的に会議を開く(タスク中)"
         + "\n/start <秒> - 開始のカウントダウンを始める(ロビー)"
         + "\n/cs - 開始のカウントダウンを止める"
         + "\n/bo - 全員の画面を光らせる(試合中)"
         + "\n/c <色> <内容> - 霊界からでも個人秘匿を送れる";

    // ───────────────────────── ロビーの名前表示 ─────────────────────────
    private static bool HasPrefix(string name) => !string.IsNullOrEmpty(name) && name.StartsWith(NamePrefix, StringComparison.Ordinal);
    public static string StripPrefix(string name) => HasPrefix(name) ? name.Substring(NamePrefix.Length) : (name ?? "");

    /// <summary>ロビーにいる間、モデレーターの名前に「モデレーター - 」を付け、外れた人からは外す</summary>
    private static void RefreshNames(bool force = false)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsLobby || namesStripped) return;
        if (!force && Time.time < nextRefreshTime) return;
        nextRefreshTime = Time.time + 0.5f;

        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc == null || pc.Data == null || IsHost(pc)) continue;
            var current = pc.Data.PlayerName ?? "";
            var baseName = StripPrefix(current);
            if (string.IsNullOrWhiteSpace(baseName)) continue;
            var wanted = IsModerator(pc) ? NamePrefix + baseName : baseName;
            if (current == wanted) continue;
            pc.RpcSetName(wanted);
        }
    }

    /// <summary>試合が始まる時に「モデレーター - 」を外す(役職の表示や結果に混ざらないように)</summary>
    public static void StripNamesForGame()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        namesStripped = true;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc == null || pc.Data == null) continue;
            var current = pc.Data.PlayerName ?? "";
            if (!HasPrefix(current)) continue;
            var baseName = StripPrefix(current);
            pc.RpcSetName(baseName);
            if (Main.AllPlayerNames.ContainsKey(pc.PlayerId)) Main.AllPlayerNames[pc.PlayerId] = baseName;
        }
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
    private static class LobbyUpdatePatch
    {
        public static void Postfix() => RefreshNames();
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    private static class LobbyStartPatch
    {
        public static void Postfix()
        {
            namesStripped = false;
            RefreshNames(force: true);
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.StartGame))]
    private static class StartGamePatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix() => StripNamesForGame();
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.ResetStartState))]
    private static class ResetStartPatch
    {
        public static void Postfix()
        {
            if (!GameStates.IsLobby) return;
            namesStripped = false;
        }
    }
}
