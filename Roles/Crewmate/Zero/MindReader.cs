using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;

using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Roles.Crewmate;

/// <summary>
/// マインドリーダー / Mind Reader (TOH_Zero)
/// ・タスクを設定数終わらせるまでは自覚がない(覚醒役職。自分の画面では「クルーメイト(?)」に見える)
/// ・覚醒すると、次の会議の始めに「マインドリーダーが覚醒した」と全員に通知される(設定でオフにできる)
/// ・覚醒した後は、ほかの人の秘匿メッセージ(/c)の中身が自分にだけ届く
/// </summary>
public sealed class MindReader : RoleBase, IAwakeningRole
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MindReader),
            player => new MindReader(player),
            CustomRoles.MindReader,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 2200,
            SetupOptionItem,
            "マインドリーダー",
            "#b48cff"
        );
    public MindReader(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        awakened = false;
        announcePending = false;
        // 覚醒までのタスク数が 0 なら最初から覚醒している(通知もしない)
        if (OptionAwakenTasks.GetInt() <= 0) awakened = true;
    }

    private static OptionItem OptionAwakenTasks;
    private static OptionItem OptionNotify;
    enum OptionName
    {
        MindReaderAwakenTasks,
        MindReaderNotify,
    }

    private bool awakened;
    /// <summary>覚醒したことを次の会議の始めに知らせるか</summary>
    private bool announcePending;

    private static void SetupOptionItem()
    {
        OptionAwakenTasks = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MindReaderAwakenTasks, new(0, 20, 1), 8, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionNotify = BooleanOptionItem.Create(RoleInfo, 11, OptionName.MindReaderNotify, true, false);
    }

    public bool IsAwakened => awakened;

    public override bool OnCompleteTask()
    {
        if (awakened || !Player.IsAlive()) return true;
        // 自分のタスクが設定数より少ない時は、全部終わったら覚醒
        var need = Mathf.Min(OptionAwakenTasks.GetInt(), MyTaskState.AllTasksCount);
        if (MyTaskState.CompletedTasksCount >= need)
        {
            awakened = true;
            announcePending = true;
            Logger.Info($"{Player.GetNameWithRole()} : タスクを {MyTaskState.CompletedTasksCount} 個終えて覚醒", nameof(MindReader));
            Utils.NotifyRoles(SpecifySeer: Player);
        }
        return true;
    }

    public override void OnStartMeeting()
    {
        if (!AmongUsClient.Instance.AmHost || !announcePending) return;
        announcePending = false;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsMeeting) return;
            if (OptionNotify.GetBool())
                Utils.SendMessageAutoSplit(Translator.GetString("MindReaderAwakened"), true);
            // 本人には役職の説明を送る
            if (Player != null) Utils.ShowMyRoleInfo(Player, Player.PlayerId);
        }, 4f, "MindReader.Announce");
    }

    /// <summary>
    /// 秘匿メッセージが送られた時に呼ぶ。覚醒した生きているマインドリーダー(送り主・宛先以外)に中身を届ける
    /// </summary>
    public static void OnPrivateMessage(PlayerControl sender, PlayerControl target, string senderName, string targetName, string body)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        foreach (var mr in CustomRoleManager.AllActiveRoles.Values.OfType<MindReader>())
        {
            var pc = mr.Player;
            if (pc == null || !mr.awakened || !pc.IsAlive()) continue;
            if (pc.PlayerId == sender.PlayerId || pc.PlayerId == target.PlayerId) continue;
            var title = Utils.ColorString(RoleInfo.RoleColor, string.Format(Translator.GetString("MindReaderPeekTitle"), senderName, targetName));
            if (pc.AmOwner)
                HudManager.Instance?.Chat?.AddChat(pc, $"<align=left>{title}\n{body}</align>");
            else
                Utils.SendMessageAutoSplit(body, true, title, sendTo: pc.PlayerId);
        }
    }
}
