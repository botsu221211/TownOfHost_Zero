using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using AmongUs.GameOptions;
using TownOfHostY.Attributes;
using TownOfHostY.Modules;
using TownOfHostY.Roles.Impostor;
using TownOfHostY.Roles.Neutral;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Unit;

namespace TownOfHostY;

public static class AntiBlackout
{
    ///<summary>
    ///追放処理を上書きするかどうか
    ///</summary>
    public static bool OverrideExiledPlayer
        => Options.NoGameEnd.GetBool()
        || Jackal.RoleInfo.IsEnable
        || Pirate.RoleInfo.IsEnable
        || Options.IsCCMode;

    public static bool IsCached { get; private set; } = false;
    private static Dictionary<byte, (bool isDead, bool Disconnected)> isDeadCache = new();
    private readonly static LogHandler logger = Logger.Handler("AntiBlackout");

    private static RecognizeType recognizeType = RecognizeType.Impostor;
    public static int ExiledPlayerId = -1;

    public static void SetIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        SetRoleChange();

        logger.Info($"SetIsDead is called from {callerMethodName}");
        if (IsCached)
        {
            logger.Info("再度SetIsDeadを実行する前に、RestoreIsDeadを実行してください。");
            return;
        }
        isDeadCache.Clear();
        // 暗転を防ぐのに必要な数の幽霊だけを、一時的に生きている扱いにする。
        // (全員を生きている扱いにすると、追放の演出が先に終わった人の画面に幽霊の体が見え、
        //  幽霊本人の当たり判定も戻ってしまう)
        int need = RequiredRevivals();
        LastRevivedCount = 0;
        foreach (var info in GameData.Instance.AllPlayers)
        {
            if (info == null || !info.IsDead || info.Disconnected) continue;
            if (LastRevivedCount >= need) break;
            isDeadCache[info.PlayerId] = (info.IsDead, info.Disconnected);
            info.IsDead = false;
            LastRevivedCount++;
        }
        logger.Info($"暗転対策で一時的に生きている扱いにした幽霊: {LastRevivedCount}人 (必要数 {need}, 追放者ID {lastExiledPlayerId}, 身代わり {LastDummyCount}人, 生存クルーの見え方 {AliveCrewInView()}人)");
        IsCached = true;
        if (doSend) SendGameData();
    }
    private enum RecognizeType
    {
        Impostor,
        Jackal,
        Pirate,
        Catalyst,
        /// <summary>
        /// 上のどれもいないのに試合が続く時(タイガーなど、判定がクルーのキル人外が生きている時)。
        /// 身代わりのインポスターが 0 人だと、客は追放の後に「インポスター全滅で試合終了」と判断して暗転するので、
        /// 生きているキル人外を身代わりにする
        /// </summary>
        NeutralKiller,
    }
    private static bool IsRecognizeType(PlayerControl pc, RecognizeType type)
    {
        if (pc == null) return false;
        var customRole = pc.GetCustomRole();
        var countType = PlayerState.GetByPlayerId(pc.PlayerId).CountType;
        //var countType = customRole.GetRoleInfo().CountType;
        Logger.Info($"RecognizeType {pc?.name}, {countType}, {customRole}({customRole.GetRoleInfo().CountType})", "AntiBlackout");
        return type switch
        {
            RecognizeType.Impostor => countType == CountTypes.Impostor,
            RecognizeType.Jackal => countType == CountTypes.Jackal,
            RecognizeType.Pirate => countType == CountTypes.Pirate,
            RecognizeType.Catalyst => countType == CountTypes.Catalyst,
            RecognizeType.NeutralKiller => customRole.IsNeutral() && customRole != CustomRoles.KitsuneSpirit && pc.CanUseKillButton(),
            _ => false,
        };
    }

    /// <summary>直前の SetRoleChange で、各クライアントに見せた身代わりのインポスターの数</summary>
    public static int LastDummyCount { get; private set; }
    private static int lastExiledPlayerId = -1;
    /// <summary>
    /// 追放を同数投票として伏せた時の追放者。追放者本人の画面では自分が生きているので、
    /// 自分がインポスターだと「生存インポスター ≥ 生存クルー」になって暗転しうる。
    /// 本人の画面でだけ自分をクルーに見せ、会議明けの役職の配り直しでも戻さない
    /// </summary>
    public static byte HiddenExiledPlayerId { get; private set; } = byte.MaxValue;
    public static void HideExiledRoleForSelf(PlayerControl exiled)
    {
        HiddenExiledPlayerId = byte.MaxValue;
        if (exiled == null || exiled.AmOwner) return;
        int clientId = exiled.GetClientId();
        if (clientId == -1) return;
        HiddenExiledPlayerId = exiled.PlayerId;
        lastHiddenExiledPlayerId = exiled.PlayerId;
        exiled.RpcSetRoleDesync(RoleTypes.Crewmate, clientId, canOverrideRole: true);
        logger.Info($"{exiled.Data?.PlayerName}: 追放を伏せたので、本人の画面でだけクルーに見せます");
    }

    private static byte lastHiddenExiledPlayerId = byte.MaxValue;
    /// <summary>
    /// 追放者が幽霊になった後に呼ぶ。本人の画面でクルーに見せていた役職を、本当の幽霊の役職に戻す
    /// (インポスターなら幽霊でもサボタージュできるように)。勝敗の判定はホストの役職データで行うので、元々影響しない
    /// </summary>
    public static void RestoreHiddenExiledRole(PlayerControl exiled)
    {
        if (exiled == null || exiled.PlayerId != lastHiddenExiledPlayerId) return;
        lastHiddenExiledPlayerId = byte.MaxValue;
        int clientId = exiled.GetClientId();
        if (clientId == -1) return;
        var ghost = exiled.GetCustomRole().IsImpostor() ? RoleTypes.ImpostorGhost : RoleTypes.CrewmateGhost;
        exiled.RpcSetRoleDesync(ghost, clientId, canOverrideRole: true);
        logger.Info($"{exiled.Data?.PlayerName}: 本人の画面の役職を {ghost} に戻しました");
    }

    /// <summary>直前の SetIsDead で、一時的に生きている扱いにした幽霊の数</summary>
    public static int LastRevivedCount { get; private set; }

    /// <summary>追放後(追放者を除く)にクライアントから見える、生存しているクルーの数(幽霊は数えない)</summary>
    private static int AliveCrewInView()
    {
        int alive = Main.AllAlivePlayerControls.Count(pc => pc.PlayerId != lastExiledPlayerId);
        return alive - LastDummyCount;
    }
    /// <summary>
    /// クライアントは追放の演出の締めで「生存インポスター ≥ 生存クルー」なら試合が終わったと判断して暗転する。
    /// 身代わりのインポスターの数がクルーより少なくなるよう、足りない分だけ幽霊を生きているクルーに見せる
    /// </summary>
    private static int RequiredRevivals()
    {
        if (LastDummyCount <= 0) return 0;
        int crew = AliveCrewInView();
        return LastDummyCount >= crew ? LastDummyCount - crew + 1 : 0;
    }

    /// <summary>
    /// 追放者を本当の名前で見せても、クライアントが追放の演出の締めで「試合が終わった」と判断しないか。
    /// 会議の後は全員を生きている扱いにしているので、クライアントから見た生存者は(切断者以外の)全員から追放者を除いた数。
    /// そのうち身代わりのインポスターが 1 人以上いて、クルーより少なければ試合は続くと判断される。
    /// </summary>
    public static bool IsSafeToShowExiled()
    {
        if (Options.IsCCMode) return false;
        return LastDummyCount > 0 && LastDummyCount < AliveCrewInView() + LastRevivedCount;
    }

    public static void SetRoleChange(PlayerControl killer = null)
    {
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;

        RecognizeType remaining = RecognizeType.Impostor;
        List<PlayerControl> list = new();
        foreach (RecognizeType type in Enum.GetValues(typeof(RecognizeType)))
        {
            remaining = type;
            list = Main.AllAlivePlayerControls.Where(pc => pc.PlayerId != ExiledPlayerId && IsRecognizeType(pc, type)).ToList();
            Logger.Info($"CheckRoleCount type: {remaining}, count: {list.Count}, exiled: {ExiledPlayerId}", "AntiBlackout");
            if (list.Count > 0) break;
        }

        recognizeType = remaining;
        LastDummyCount = list.Count;

        Logger.Info($"SetDummyImpostor type: {recognizeType}, count:{list.Count}", "AntiBlackout");
        var deadPlayers = Main.AllDeadPlayerControls.Where(x => !x.Data.Disconnected).ToList();

        foreach (var pc in Main.AllPlayerControls.Where(x => !x.Data.Disconnected))
        {
            if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
            if (pc.IsAlive() && pc.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true) continue;

            var clientId = pc.GetClientId();
            if (clientId == -1) continue;
            var sender = CustomRpcSender.Create("AntiBlackout.SetRoleChange", Hazel.SendOption.Reliable);
            sender.StartMessage(clientId);
            var count = 0;

            foreach (var dummy in list)
            {
                if (killer != null && dummy.PlayerId == pc.PlayerId) continue;
                // 身代わりは客の画面でインポスター側の役職に見えないと意味がない
                var dummyRole = dummy.GetCustomRole().GetRoleTypes();
                if (dummyRole is not (RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.Viper))
                    dummyRole = RoleTypes.Impostor;
                sender.StartRpc(dummy.NetId, RpcCalls.SetRole)
                    .Write((ushort)dummyRole)
                    .Write(true)
                    .EndRpc();
                count++;
            }
            foreach (var dead in deadPlayers)
            {
                sender.StartRpc(dead.NetId, RpcCalls.SetRole)
                    .Write((ushort)RoleTypes.CrewmateGhost)
                    .Write(true)
                    .EndRpc();
                count++;
            }

            if (count > 0)
            {
                sender.EndMessage();
                sender.SendMessage();
            }
            else
            {
                sender.stream.Recycle();
            }
            Logger.Info($"SetDummyImpostor player: {pc?.name} ({count} rpc)", "AntiBlackout");
        }
        // 追放者の ID は直後の人数計算(RequiredRevivals / IsSafeToShowExiled)でも使うので控えておく
        lastExiledPlayerId = ExiledPlayerId;
        ExiledPlayerId = -1;
    }
    public static void RestoreIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        logger.Info($"RestoreIsDead is called from {callerMethodName}");
        var ghosts = new List<NetworkedPlayerInfo>();
        foreach (var info in GameData.Instance.AllPlayers)
        {
            if (info == null) continue;
            if (isDeadCache.TryGetValue(info.PlayerId, out var val))
            {
                info.IsDead = val.isDead;
                info.Disconnected = val.Disconnected;
                if (val.isDead && !val.Disconnected) ghosts.Add(info);
            }
        }
        isDeadCache.Clear();
        IsCached = false;
        HiddenExiledPlayerId = byte.MaxValue;
        ResendGhostDeaths(ghosts);
        if (doSend) SendGameData();
    }

    /// <summary>
    /// 会議の間だけ生きている扱いにしていた幽霊を、各クライアントの画面でも本当に死んだ状態に戻す。
    /// IsDead を戻すだけだとクライアントは死亡処理(Die)をやり直さないため、
    /// ・生きている人の画面に幽霊が見えたままになる
    /// ・幽霊本人の当たり判定が戻ったままで壁を抜けられない
    /// ことがある。Exiled(追放)のRPCは死体を残さずに死亡処理を行うので、それを送る。
    /// </summary>
    private static void ResendGhostDeaths(List<NetworkedPlayerInfo> ghosts)
    {
        foreach (var info in ghosts)
        {
            var pc = info.Object;
            if (pc == null) continue;
            try
            {
                if (pc.AmOwner)
                {
                    // ホスト自身が幽霊の時は、自分の画面の当たり判定だけ切る
                    if (pc.Collider != null) pc.Collider.enabled = false;
                    continue;
                }
                var writer = AmongUsClient.Instance.StartRpcImmediately(pc.NetId, (byte)RpcCalls.Exiled, Hazel.SendOption.Reliable, -1);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                logger.Info($"{info.PlayerName}: 幽霊の死亡処理を送り直しました");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "AntiBlackout.ResendGhostDeaths");
            }
        }
    }

    public static void SendGameData([CallerMemberName] string callerMethodName = "")
    {
        logger.Info($"SendGameData is called from {callerMethodName}");
        foreach (var innerNetObject in GameData.Instance.AllPlayers)
        {
            innerNetObject.SetDirtyBit(uint.MaxValue);
        }
    }
    public static void OnDisconnect(NetworkedPlayerInfo player)
    {
        // 実行条件: クライアントがホストである, IsDeadが上書きされている, playerが切断済み
        if (!AmongUsClient.Instance.AmHost || !IsCached || !player.Disconnected) return;
        isDeadCache[player.PlayerId] = (true, true);
        player.IsDead = player.Disconnected = false;
        SendGameData();
    }

    ///<summary>
    ///一時的にIsDeadを本来のものに戻した状態でコードを実行します
    ///<param name="action">実行内容</param>
    ///</summary>
    public static void TempRestore(Action action)
    {
        logger.Info("==Temp Restore==");
        //IsDeadが上書きされた状態でTempRestoreが実行されたかどうか
        bool before_IsCached = IsCached;
        try
        {
            if (before_IsCached) RestoreIsDead(doSend: false);
            action();
        }
        catch (Exception ex)
        {
            logger.Warn("AntiBlackout.TempRestore内で例外が発生しました");
            logger.Exception(ex);
        }
        finally
        {
            if (before_IsCached) SetIsDead(doSend: false);
            logger.Info("==/Temp Restore==");
        }
    }

    [GameModuleInitializer]
    public static void Reset()
    {
        logger.Info("==Reset==");
        if (isDeadCache == null) isDeadCache = new();
        isDeadCache.Clear();
        IsCached = false;
        recognizeType = RecognizeType.Impostor;
    }
}