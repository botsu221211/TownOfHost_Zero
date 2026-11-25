using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hazel;
using InnerNet;
using UnityEngine;
using TownOfHostY.Roles.Core;

namespace TownOfHostY;

/// <summary>
/// MOD を入れていない人の画面にも見える「ダミー」(偽の PlayerControl)。ホストだけが作る。
/// 白狐のお皿など、マップに置く物に使う。体の色で見分ける(名前の文字で絵も描ける)。
///
/// TownOfHost_L の CustomNetObject (GPL-3.0) を TOH_Zero 向けに縮めて移植したもの。
/// 元は MoreGamemodes (Rabek009) の AllObjects と Endless Host Roles (Gurge44) の CustomNetObject。
///
/// 出し方(公式サーバーで客の画面に映る手順):
/// 1. PlayerPrefab を作って spawn(持ち主 -2)。公式サーバーでは子の 3 つの netId を空の spawn で埋める
/// 2. 0.1 秒後、客ごとに「ダミーの PlayerId をその客の ID に差し替え → MurderPlayer(FailedError) → 戻す」を送る
/// 3. 0.15 秒後、ホストの外見を一瞬だけ「絵の名前・体の色・装飾なし」にして送り、ダミーをホストに変身させて、外見を戻す
/// 4. 0.3 秒後に SnapTo をもう一度送る
///
/// 公式サーバーのキックを避ける決まり:
/// ・イントロが終わってから 13 秒、会議(追放の演出を含む)が明けてから 10 秒は出さない
/// ・出すのは 1 体ずつ。次は前の可視化から max(0.2 秒, 客の数 / 12) 空ける
/// ・見た目の包みは 1000 バイト以下に収める
/// ・客のキルボタンがダミーを指すことがあるので、ダミーへのキルは受け付けない
/// </summary>
public sealed class CustomNetObject
{
    private const string Tag = "CustomNetObject";
    public const string SpriteNamePrefix = "<size=14><br></size>";
    // 本物のプレイヤーは 0〜14。ダミーは 200〜253 を回して使う
    public const byte MinPlayerId = 200;
    private const byte MaxPlayerId = 253;
    /// <summary>本編の色の番号 (Palette.PlayerColors)</summary>
    public const byte ColorBlack = 6, ColorWhite = 7, ColorPurple = 8, ColorBrown = 9;
    private static readonly Vector2 Hidden = new(50f, 50f);

    private const float AfterIntro = 13f;
    private const float AfterMeeting = 10f;
    private const float MinSpawnGap = 0.2f;
    private const float VisibilityPerSecond = 12f;
    private const int VisibilityTargetsPerEnvelope = 4;
    private const float VisibilityDelay = 0.1f;
    private const float SpriteDelay = 0.15f;
    private const float ResnapDelay = 0.3f;
    private const float SpritePacketGap = 0.2f;
    private const int MaxPacketBytes = 1000;
    /// <summary>同時に出している途中にしてよいダミーの数</summary>
    private const int MaxInFlight = 3;

    private enum State { Waiting, Spawned, Visible, Sprited, Live, Gone }

    private static readonly List<CustomNetObject> all = new();
    private static readonly HashSet<byte> usedIds = new();
    private static byte nextId = MaxPlayerId;
    private static float? introEndedAt;
    private static float lastMeetingAt = float.NegativeInfinity;
    private static float nextSpawnAt;
    private static float nextSpritePacketAt;
    private static bool inMeeting;

    private State state = State.Waiting;
    private PlayerControl pc;
    private byte playerId;
    private float stepAt;
    private bool needsResend;

    public Vector2 Position { get; private set; }
    public string Sprite { get; private set; }
    public byte BodyColorId { get; private set; }
    public bool IsLive => state == State.Live;
    public bool IsGone => state == State.Gone;

    /// <summary>絵の名前を載せたホストの PlayerInfo を書き出している間だけ true。名前を書き換えるパッチはこの間は手を出さない</summary>
    public static bool SerializingSprite { get; private set; }

    private CustomNetObject(string sprite, Vector2 position, byte bodyColorId)
    {
        Sprite = sprite ?? "";
        Position = position;
        BodyColorId = bodyColorId;
    }

    /// <summary>ダミーを作る。すぐには映らない(イントロ明け 13 秒などの決まりを満たしてから出る)。ホストで試合中でなければ null</summary>
    public static CustomNetObject Create(string sprite, Vector2 position, byte bodyColorId)
    {
        // 役職を配る時点ではまだ「試合中」(GameStates.InGame)になっていないので、船があるかで判定する
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || ShipStatus.Instance == null || !PlayerControl.LocalPlayer) return null;
        var obj = new CustomNetObject(sprite, position, bodyColorId);
        all.Add(obj);
        return obj;
    }

    /// <summary>この PlayerControl はダミーか</summary>
    public static bool Is(PlayerControl pc) => pc != null && pc.PlayerId >= MinPlayerId;

    /// <summary>体の色(と絵)を変える。出す前なら出す時の見た目が変わる</summary>
    public void SetLook(byte bodyColorId, string sprite = null)
    {
        if (state == State.Gone) return;
        sprite ??= Sprite;
        if (bodyColorId == BodyColorId && sprite == Sprite) return;
        BodyColorId = bodyColorId;
        Sprite = sprite;
        if (state == State.Live) needsResend = true;
    }

    public void Despawn() => Despawn(sendToServer: true);

    private void Despawn(bool sendToServer)
    {
        if (state == State.Gone) return;
        var was = state;
        state = State.Gone;
        all.Remove(this);
        if (was == State.Waiting) return;
        try
        {
            if (pc)
            {
                uint netId = pc.NetId;
                var client = AmongUsClient.Instance;
                if (sendToServer && client && client.AmConnected)
                {
                    var msg = MessageWriter.Get(SendOption.Reliable);
                    msg.StartMessage(5);
                    msg.Write(client.GameId);
                    msg.StartMessage(5);
                    msg.WritePacked(netId);
                    msg.EndMessage();
                    msg.EndMessage();
                    client.SendOrDisconnect(msg);
                    msg.Recycle();
                }
                if (client) client.RemoveNetObject(pc);
                UnityEngine.Object.Destroy(pc.gameObject);
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, Tag);
        }
        finally
        {
            pc = null;
            usedIds.Remove(playerId);
        }
    }

    public static void DespawnAll(bool sendToServer)
    {
        foreach (var obj in all.ToArray()) obj.Despawn(sendToServer);
        all.Clear();
        usedIds.Clear();
        nextId = MaxPlayerId;
        introEndedAt = null;
        lastMeetingAt = float.NegativeInfinity;
        nextSpawnAt = nextSpritePacketAt = 0f;
    }

    private static bool IsOfficialServer
    {
        get
        {
            try
            {
                if (AmongUsClient.Instance == null || AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame) return false;
                var region = ServerManager.Instance?.CurrentRegion;
                if (region == null) return true;
                var ping = region.PingServer;
                return string.IsNullOrEmpty(ping) || ping.EndsWith("among.us", StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }
    }

    private static IEnumerable<PlayerControl> RealPlayers
        => PlayerControl.AllPlayerControls.ToArray().Where(p => p != null && p.PlayerId < MinPlayerId);

    // ---- 毎フレーム ----

    private static void Update()
    {
        float now = Time.realtimeSinceStartup;
        inMeeting = MeetingHud.Instance || ExileController.Instance;
        if (GameStates.IsInGame && inMeeting) lastMeetingAt = now;
        if (all.Count == 0) return;
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || !GameStates.IsInGame || !PlayerControl.LocalPlayer) return;

        // ダミーが本編のプレイヤー一覧に戻ってくることがある(変身先を選ぶ画面に出てしまう)ので毎回外す
        foreach (var obj in all)
            if (obj.pc && PlayerControl.AllPlayerControls.Contains(obj.pc)) PlayerControl.AllPlayerControls.Remove(obj.pc);

        // 出している途中(可視化・見た目・位置の段階)のダミーの数。決まった数までは並べて進める
        int inFlight = 0;
        foreach (var obj in all)
            if (obj.state is State.Spawned or State.Visible or State.Sprited) inFlight++;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (i >= all.Count) continue;
            var obj = all[i];
            try { obj.Tick(now, ref inFlight); }
            catch (Exception ex)
            {
                Logger.Exception(ex, Tag);
                obj.Despawn();
            }
        }
    }

    private void Tick(float now, ref int inFlight)
    {
        switch (state)
        {
            case State.Waiting:
                // 出し始めの間隔(可視化は max(0.2 秒, 客の数 / 12) 空ける)と、見た目の包みの間隔(0.2 秒)は守ったまま、
                // 前のダミーが出し終わるのを待たずに次を出し始める(全部出るまでが速くなる)
                if (inFlight >= MaxInFlight || !CanStartSpawn(now)) return;
                Spawn(now);
                inFlight++;
                return;
            case State.Spawned:
            case State.Visible:
            case State.Sprited:
                if (now < stepAt) return;
                if (!pc) { Despawn(); return; }
                if (state == State.Spawned) SendVisibility(now);
                else if (state == State.Visible)
                {
                    if (now < nextSpritePacketAt || inMeeting) return;
                    SendSprite(now, first: true);
                }
                else
                {
                    SendSnap();
                    state = State.Live;
                }
                return;
            case State.Live:
                if (!pc) { Despawn(); return; }
                if (needsResend && now >= nextSpritePacketAt && !inMeeting)
                {
                    needsResend = false;
                    SendSprite(now, first: false);
                }
                return;
        }
    }

    private static bool CanStartSpawn(float now)
    {
        if (introEndedAt is not { } intro || now - intro < AfterIntro) return false;
        if (now - lastMeetingAt < AfterMeeting) return false;
        return now >= nextSpawnAt;
    }

    // ---- 1. spawn ----

    private void Spawn(float now)
    {
        var client = AmongUsClient.Instance;
        playerId = AllocateId();
        pc = UnityEngine.Object.Instantiate(client.PlayerPrefab, Vector2.zero, Quaternion.identity);
        pc.PlayerId = playerId;
        pc.isNew = false;
        pc.notRealPlayer = true;
        pc.NetTransform.SnapTo(Hidden);
        client.NetIdCnt += 1U;

        var msg = MessageWriter.Get(SendOption.Reliable);
        msg.StartMessage(5);
        msg.Write(client.GameId);
        msg.StartMessage(4);
        client.CreateSpawnMessage(pc, -2, SpawnFlags.None).SerializeValues(msg);
        msg.EndMessage();
        if (IsOfficialServer)
        {
            // PlayerControl の子 (NetTransform など) 3 つの netId を空の spawn で埋めておく
            for (uint i = 1; i <= 3; ++i)
            {
                msg.StartMessage(4);
                msg.WritePacked(2U);
                msg.WritePacked(-2);
                msg.Write((byte)SpawnFlags.None);
                msg.WritePacked(1);
                msg.WritePacked(client.NetIdCnt - i);
                msg.StartMessage(1);
                msg.EndMessage();
                msg.EndMessage();
            }
        }
        msg.EndMessage();
        client.SendOrDisconnect(msg);
        msg.Recycle();

        HideFromHostList();
        pc.cosmetics.colorBlindText.color = Color.clear;
        pc.cosmetics.currentBodySprite.BodySprite.color = Color.clear;
        state = State.Spawned;
        stepAt = now + VisibilityDelay;
        Logger.Info($"ダミーを出した netId={pc.NetId} id={playerId} at ({Position.x:F2}, {Position.y:F2})", Tag);
    }

    // ホストの一覧に入っていると、TOH も本編もプレイヤーとして扱ってしまう
    private void HideFromHostList()
    {
        if (PlayerControl.AllPlayerControls.Contains(pc)) PlayerControl.AllPlayerControls.Remove(pc);
    }

    private static byte AllocateId()
    {
        for (int i = 0; i <= MaxPlayerId - MinPlayerId; i++)
        {
            byte candidate = nextId;
            nextId = nextId <= MinPlayerId ? MaxPlayerId : (byte)(nextId - 1);
            if (usedIds.Add(candidate)) return candidate;
        }
        Logger.Error("ダミーの ID が足りません", Tag);
        return nextId;
    }

    // ---- 2. 客ごとの可視化 ----

    // 自分の画面で死んでいる客に、その客自身の ID で送ると客が切れるので、生きている別の人の ID を使う
    private static byte VisibilityIdFor(PlayerControl target)
    {
        if (target.Data != null && !target.Data.IsDead) return target.PlayerId;
        foreach (var p in RealPlayers)
        {
            if (p.Data == null || p.Data.IsDead || p.Data.Disconnected) continue;
            return p.PlayerId;
        }
        return target.PlayerId;
    }

    private void SendVisibility(float now)
    {
        HideFromHostList();
        var client = AmongUsClient.Instance;
        var targets = RealPlayers.Where(p => !p.AmOwner && p.Data != null && !p.Data.Disconnected).ToList();
        for (int start = 0; start < targets.Count; start += VisibilityTargetsPerEnvelope)
        {
            var stream = MessageWriter.Get(SendOption.Reliable);
            stream.StartMessage(26);
            stream.WritePacked(client.GameId);
            foreach (var target in targets.Skip(start).Take(VisibilityTargetsPerEnvelope))
            {
                stream.StartMessage(6);
                stream.Write(client.GameId);
                stream.WritePacked(target.OwnerId);
                stream.StartMessage(1);
                stream.WritePacked(pc.NetId);
                stream.Write(VisibilityIdFor(target));
                stream.EndMessage();
                stream.StartMessage(2);
                stream.WritePacked(pc.NetId);
                stream.Write((byte)RpcCalls.MurderPlayer);
                stream.WriteNetObject(pc);
                stream.Write((int)MurderResultFlags.FailedError);
                stream.EndMessage();
                stream.StartMessage(1);
                stream.WritePacked(pc.NetId);
                stream.Write(pc.PlayerId);
                stream.EndMessage();
                stream.EndMessage();
            }
            stream.EndMessage();
            client.SendOrDisconnect(stream);
            stream.Recycle();
        }
        nextSpawnAt = now + Mathf.Max(MinSpawnGap, targets.Count / VisibilityPerSecond);
        pc.CachedPlayerData = PlayerControl.LocalPlayer.Data;
        state = State.Visible;
        stepAt = now + SpriteDelay;
    }

    // ---- 3. 見た目 ----

    // ホストの外見を一瞬だけ絵の名前と体の色に書き換えて送り、ダミーをホストに変身させてから外見を戻す(1 つの包みで)
    private void SendSprite(float now, bool first)
    {
        var client = AmongUsClient.Instance;
        var lp = PlayerControl.LocalPlayer;
        var outfit = lp.Data.Outfits[PlayerOutfitType.Default];
        string name = outfit.PlayerName, hat = outfit.HatId, skin = outfit.SkinId, pet = outfit.PetId, visor = outfit.VisorId;
        int color = outfit.ColorId;

        var w = MessageWriter.Get(SendOption.Reliable);
        try
        {
            w.StartMessage(5);
            w.Write(client.GameId);
            outfit.PlayerName = SpriteNamePrefix + Sprite;
            outfit.ColorId = BodyColorId;
            outfit.HatId = outfit.SkinId = outfit.PetId = outfit.VisorId = "";
            w.StartMessage(1);
            w.WritePacked(lp.Data.NetId);
            SerializingSprite = true;
            try { lp.Data.Serialize(w, false); }
            finally { SerializingSprite = false; }
            w.EndMessage();
            // ホストの画面のダミーも同じ見た目にする(体はスポーンの時に透明にしてあるので戻す)
            try
            {
                pc.Shapeshift(lp, false);
                pc.cosmetics.currentBodySprite.BodySprite.color = Color.white;
            }
            catch (Exception ex) { Logger.Warn($"手元の変身: {ex.Message}", Tag); }
            w.StartMessage(2);
            w.WritePacked(pc.NetId);
            w.Write((byte)RpcCalls.Shapeshift);
            w.WriteNetObject(lp);
            w.Write(false);
            w.EndMessage();
        }
        finally
        {
            outfit.PlayerName = name;
            outfit.ColorId = color;
            outfit.HatId = hat;
            outfit.SkinId = skin;
            outfit.PetId = pet;
            outfit.VisorId = visor;
        }
        // ホストの外見を元に戻して送る(ここは送る時に本当の名前に戻すパッチが効く)
        w.StartMessage(1);
        w.WritePacked(lp.Data.NetId);
        lp.Data.Serialize(w, false);
        w.EndMessage();
        if (first)
        {
            var nt = pc.NetTransform;
            nt.SnapTo(Position);
            w.StartMessage(2);
            w.WritePacked(nt.NetId);
            w.Write((byte)RpcCalls.SnapTo);
            NetHelpers.WriteVector2(Position, w);
            w.Write(nt.lastSequenceId);
            w.EndMessage();
        }
        w.EndMessage();

        nextSpritePacketAt = now + SpritePacketGap;
        if (w.Length > MaxPacketBytes)
        {
            // 分割されると順番が崩れて、ホストの外見が絵のまま残りうるので送らない
            Logger.Error($"見た目の包みが長すぎるので送りません ({w.Length} バイト)", Tag);
            w.Recycle();
        }
        else
        {
            client.SendOrDisconnect(w);
            w.Recycle();
        }
        if (first)
        {
            state = State.Sprited;
            stepAt = now + ResnapDelay;
        }
    }

    // ---- 4. 位置 ----

    // 手元は通し番号 +1、電文は +2 (客の側は spawn の時に 1 つ進んでいる)
    private void SendSnap()
    {
        var client = AmongUsClient.Instance;
        var nt = pc.NetTransform;
        nt.SnapTo(Position, (ushort)(nt.lastSequenceId + 1U));
        var w = MessageWriter.Get(SendOption.Reliable);
        w.StartMessage(5);
        w.Write(client.GameId);
        w.StartMessage(2);
        w.WritePacked(nt.NetId);
        w.Write((byte)RpcCalls.SnapTo);
        NetHelpers.WriteVector2(Position, w);
        w.Write((ushort)(nt.lastSequenceId + 2U));
        w.EndMessage();
        w.EndMessage();
        client.SendOrDisconnect(w);
        w.Recycle();
    }

    // ---- パッチ ----

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    private static class DrivePatch
    {
        public static void Postfix()
        {
            try { Update(); }
            catch (Exception ex) { Logger.Exception(ex, Tag); }
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    private static class IntroEndPatch
    {
        public static void Postfix()
        {
            if (GameStates.IsInGame) introEndedAt = Time.realtimeSinceStartup;
        }
    }

    // 試合が終わったら、まだつながっているうちに全部消す
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class GameEndPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Postfix() => DespawnAll(sendToServer: true);
    }

    /// <summary>会議の前にダミーを全部消した時に呼ばれる。会議の後に作り直すのに使う</summary>
    public static event Action HiddenForMeeting;

    /// <summary>
    /// 会議が始まる前にダミーを全部消す。
    /// ダミーが残ったまま会議が始まると、客の画面が会議に移れずに固まることがある
    /// (ダミーの中身は客の画面では客自身のプレイヤーの情報になっているため、死んだ人や全員の会議の処理が止まる)。
    /// 作り直すのは使う側(白狐のお皿など)で、会議の後に Create し直す。出るのは会議明けの 10 秒後から
    /// </summary>
    public static void HideForMeeting()
    {
        if (all.Count == 0) return;
        Logger.Info($"会議の前にダミーを{all.Count}体消します", Tag);
        foreach (var obj in all.ToArray()) obj.Despawn(sendToServer: true);
        all.Clear();
        lastMeetingAt = Time.realtimeSinceStartup;
        try { HiddenForMeeting?.Invoke(); }
        catch (Exception ex) { Logger.Exception(ex, Tag); }
    }

    // 会議を始める知らせ(StartMeeting)より先に消す知らせが届くよう、送る直前に消す
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcStartMeeting))]
    private static class StartMeetingPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            if (AmongUsClient.Instance.AmHost) HideForMeeting();
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    private static class MeetingStartPatch
    {
        public static void Postfix()
        {
            if (AmongUsClient.Instance.AmHost) HideForMeeting();
        }
    }

    // ロビーに戻ったら(切断などで消し損ねた分も)手元から消す
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    private static class LobbyPatch
    {
        public static void Postfix() => DespawnAll(sendToServer: false);
    }

    // 客のキルボタンがダミーを指すことがある。通すとダミーの Data(=ホストの Data)が死ぬので捨てる
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
    private static class CheckMurderPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!Is(target) && !Is(__instance)) return true;
            Logger.Info($"ダミーへのキルを捨てた: {__instance?.Data?.PlayerName}", Tag);
            // 返事をしないと、キルした人の画面でキル中のまま止まることがあるので「キル失敗」を返す
            try
            {
                if (AmongUsClient.Instance.AmHost && !Is(__instance) && !__instance.AmOwner && __instance.GetClientId() != -1)
                {
                    var writer = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, __instance.GetClientId());
                    writer.WriteNetObject(target);
                    writer.Write((int)MurderResultFlags.FailedError);
                    AmongUsClient.Instance.FinishRpcImmediately(writer);
                }
            }
            catch (Exception ex) { Logger.Exception(ex, Tag); }
            return false;
        }
    }

    // 変身先がダミーの時も捨てる
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckShapeshift))]
    private static class CheckShapeshiftPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!Is(target) && !Is(__instance)) return true;
            if (!AmongUsClient.Instance.AmHost) return false;
            // 客の変身先の一覧にはダミーが残る。白狐は誰を選んでもお揚げを食べるので、ダミーを選んでも食べられるようにする
            if (!Is(__instance) && __instance.GetRoleClass() is Roles.Neutral.KitsuneSpirit kitsune)
            {
                var animate = false;
                kitsune.OnCheckShapeshift(__instance, ref animate);
                kitsune.RespondShapeshift();
                return false;
            }
            __instance.RpcRejectShapeshift();
            return false;
        }
    }
}
