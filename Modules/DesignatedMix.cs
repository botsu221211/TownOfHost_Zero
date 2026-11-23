using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using TownOfHostY.Roles.Core;

namespace TownOfHostY.Modules;

/// <summary>
/// 指定闇鍋 (TOH_Zero)
/// ルーム設定のプリセット画面でオンにすると、全役職を 0% にリセットし、
/// 参加者全員(ホスト・ゲスト)が「/add 役職名」(/追加)で好きな役職を 100% / 最大1 にできる。
/// ・1人が入れられる数は MOD の設定(1〜5、初期値2)まで
/// ・すでに入っている役職は無効
/// ・入れた役職は試合が終わっても引き継ぐ
/// ・部屋を抜けた人が入れた役職は自動でオフにする
/// </summary>
public static class DesignatedMix
{
    public static OptionItem LimitPerPlayer;

    /// <summary>指定闇鍋がオンか(部屋にいる間は試合をまたいで続く)</summary>
    public static bool Active { get; private set; }

    /// <summary>人(フレンドコード) → その人が入れた役職</summary>
    private static readonly Dictionary<string, List<CustomRoles>> picks = new();
    /// <summary>人(フレンドコード) → 表示用の名前</summary>
    private static readonly Dictionary<string, string> pickerNames = new();

    public static void SetupCustomOption(int id)
    {
        LimitPerPlayer = IntegerOptionItem.Create(id, "DesignatedMixLimit", new(1, 5, 1), 2, TabGroup.ModMainSettings, false)
            .SetColor(new Color32(255, 179, 71, 255))
            .SetValueFormat(OptionFormat.Pieces)
            .SetGameMode(CustomGameMode.Standard);
    }

    private static int Limit => LimitPerPlayer?.GetInt() ?? 2;

    /// <summary>オンにする。全役職・属性を 0% にして、全員に使い方を知らせる</summary>
    public static void TurnOn()
    {
        Active = true;
        picks.Clear();
        pickerNames.Clear();
        ZeroSettingUI.ResetAllRoles();
        Logger.Info("指定闇鍋をオンにしました", nameof(DesignatedMix));
        Utils.SendMessageAutoSplit(GuideText(), true, Title);
    }

    /// <summary>オフにする。今入っている役職はそのまま残す</summary>
    public static void TurnOff()
    {
        Active = false;
        picks.Clear();
        pickerNames.Clear();
        Logger.Info("指定闇鍋をオフにしました", nameof(DesignatedMix));
        Utils.SendMessageAutoSplit("指定闇鍋を終了しました。", true, Title);
    }

    private static string Title => "<color=#ffb347>【指定闇鍋】</color>";
    private static string GuideText()
        => $"役職を全部リセットしました。\n<color=yellow>/add 役職名</color>(または /追加 役職名)で、1人 {Limit} 個まで好きな役職を入れられます(出現率100%・最大1)。\n(例) /add シェリフ\n※すでに入っている役職は入れられません\n※/cmd add 役職名 と打つと、打った文が他の人に見えません";

    /// <summary>入室した人に、指定闇鍋の最中なら使い方を知らせる</summary>
    public static void SendGuideTo(PlayerControl player)
    {
        if (!Active || player == null) return;
        Utils.SendMessageAutoSplit(GuideText() + Remaining(player), true, Title, player.PlayerId);
    }

    private static string KeyOf(PlayerControl player)
    {
        var code = player?.Data?.FriendCode;
        return string.IsNullOrEmpty(code) ? $"name:{player?.Data?.PlayerName}" : code;
    }
    private static int UsedCount(string key) => picks.TryGetValue(key, out var list) ? list.Count : 0;
    private static string Remaining(PlayerControl player) => $"\n(あなたの残り: {Limit - UsedCount(KeyOf(player))} 個)";

    /// <summary>/add 役職名 (/追加)</summary>
    public static void TryPick(PlayerControl player, string roleName)
    {
        if (!AmongUsClient.Instance.AmHost || player == null) return;
        void Reply(string text) => Utils.SendMessageAutoSplit(text, true, Title, player.PlayerId);

        if (!Active)
        {
            Reply("今は指定闇鍋ではありません。");
            return;
        }
        if (GameStates.IsInGame)
        {
            Reply("役職を入れられるのはロビーにいる間だけです。");
            return;
        }
        if (string.IsNullOrWhiteSpace(roleName))
        {
            Reply(GuideText() + Remaining(player));
            return;
        }

        var key = KeyOf(player);
        if (UsedCount(key) >= Limit)
        {
            Reply($"1人 {Limit} 個までです。これ以上は入れられません。");
            return;
        }

        var role = ChatCommands.FindRoleByName(roleName.Trim());
        if (role == null || !Options.CustomRoleSpawnChances.TryGetValue(role.Value, out var spawn)
            || spawn.Tab == TabGroup.ModMainSettings || spawn.IsHiddenOn(Options.CurrentGameMode))
        {
            Reply($"「{roleName.Trim()}」という役職が見つかりません。");
            return;
        }
        if (spawn.GetInt() > 0)
        {
            Reply($"{Utils.GetRoleName(role.Value)} はすでに入っています。");
            return;
        }

        ZeroSettingUI.SetRole(role.Value, true);
        if (!picks.TryGetValue(key, out var list)) picks[key] = list = new();
        list.Add(role.Value);
        pickerNames[key] = player.Data?.PlayerName ?? "";
        Logger.Info($"{player.GetNameWithRole()} が {role.Value} を入れた ({list.Count}/{Limit})", nameof(DesignatedMix));
        var coloredRole = Utils.ColorString(Utils.GetRoleColor(role.Value), Utils.GetRoleName(role.Value));
        // 全員に、何の役職が入ったかを知らせる(誰が入れたかは出さない)
        Utils.SendMessageAutoSplit($"{coloredRole} が追加されました。", true, Title);
        Reply($"{coloredRole} を入れました。" + Remaining(player));
    }

    /// <summary>部屋を抜けた人が入れた役職をオフにする</summary>
    public static void OnPlayerLeft(string friendCode, string playerName)
    {
        if (!Active || !AmongUsClient.Instance.AmHost) return;
        var key = string.IsNullOrEmpty(friendCode) ? $"name:{playerName}" : friendCode;
        if (!picks.TryGetValue(key, out var list) || list.Count == 0) return;

        foreach (var role in list) ZeroSettingUI.SetRole(role, false);
        picks.Remove(key);
        pickerNames.Remove(key);
        var names = string.Join("・", list.Select(Utils.GetRoleName));
        Logger.Info($"{playerName} が抜けたので {names} をオフにした", nameof(DesignatedMix));
        Utils.SendMessageAutoSplit($"{playerName} さんが抜けたので、入れていた役職({names})をオフにしました。", true, Title);
    }
}
