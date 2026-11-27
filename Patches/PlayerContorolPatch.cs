using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using UnityEngine;

using TownOfHostY.Modules;
using TownOfHostY.Roles;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;
using TownOfHostY.Roles.Neutral;
using TownOfHostY.Roles.AddOns.Common;
using TownOfHostY.Roles.AddOns.Crewmate;
using static TownOfHostY.Translator;
using TownOfHostY.Roles.Impostor;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace TownOfHostY;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
class CheckProtectPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        // ダミー(CustomNetObject)が絡む時は CustomNetObject 側で捨てているので、ここでは何もしない
        if (CustomNetObject.Is(target) || CustomNetObject.Is(__instance)) return false;
        if (!AmongUsClient.Instance.AmHost) return false;
        Logger.Info("CheckProtect発生: " + __instance.GetNameWithRole() + "=>" + target.GetNameWithRole(), "CheckProtect");
        if (__instance.Is(CustomRoles.Sheriff)
            || __instance.Is(CustomRoles.SillySheriff))
        {
            if (__instance.Data.IsDead)
            {
                Logger.Info("守護をブロックしました。", "CheckProtect");
                return false;
            }
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
class CheckMurderPatch
{
    public static Dictionary<byte, float> TimeSinceLastKill = new();
    public static void Update()
    {
        for (byte i = 0; i < 15; i++)
        {
            if (TimeSinceLastKill.ContainsKey(i))
            {
                TimeSinceLastKill[i] += Time.deltaTime;
                if (15f < TimeSinceLastKill[i]) TimeSinceLastKill.Remove(i);
            }
        }
    }
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        // ダミー(CustomNetObject)が絡む時は CustomNetObject 側で捨てているので、ここでは何もしない
        if (CustomNetObject.Is(target) || CustomNetObject.Is(__instance)) return false;
        if (!AmongUsClient.Instance.AmHost || AmongUsClient.Instance.IsGameOver) return false;
        RevealVisualRoleBeforeMurder(__instance, target);

        if (!CustomRoleManager.OnCheckMurder(__instance, target))
        {
            __instance.RpcMurderPlayer(target, false);
        }

        return false;
    }
    private static void RevealVisualRoleBeforeMurder(PlayerControl killer, PlayerControl target)
    {

        bool killerIsViper = killer.Is(CustomRoles.Viper) || killer.Is(CustomRoles.NormalViper);
        if (killerIsViper)
        {
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.PlayerId == killer.PlayerId) continue;
                if (seer.GetClientId() == -1) continue;
                killer.RpcSetRoleDesync(RoleTypes.Viper, seer.GetClientId());
            }

            var capKiller = killer;
            _ = new LateTask(() =>
            {
                if (capKiller == null) return;
                foreach (var seer in Main.AllPlayerControls)
                {
                    if (seer.PlayerId == capKiller.PlayerId) continue;
                    if (seer.GetClientId() == -1) continue;
                    capKiller.RpcSetRoleDesync(RoleTypes.Impostor, seer.GetClientId());
                }
            }, 2f, "RevertViperVisualReveal");
        }

        bool targetIsNoisemaker = target.Is(CustomRoles.Noisemaker) || target.Is(CustomRoles.NormalNoisemaker);
        if (targetIsNoisemaker)
        {
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.PlayerId == target.PlayerId) continue;
                if (seer.GetClientId() == -1) continue;
                target.RpcSetRoleDesync(RoleTypes.Noisemaker, seer.GetClientId());
            }
        }
    }

    // 不正キル防止チェック
    public static bool CheckForInvalidMurdering(MurderInfo info, bool killerMyselfInvoke)
    {
        (var killer, var target) = info.AttemptTuple;

        // Killerが既に死んでいないかどうか
        if (!killer.IsAlive())
        {
            Logger.Info($"{killer.GetNameWithRole()}は死亡しているためキャンセルされました。", "CheckMurder");
            return false;
        }
        // targetがキル可能な状態か
        if (
            // PlayerDataがnullじゃないか確認
            target.Data == null ||
            // targetの状態をチェック
            VentBooter.IsInVent(target) ||
            target.MyPhysics.Animations.IsPlayingEnterVentAnimation() ||
            target.MyPhysics.Animations.IsPlayingAnyLadderAnimation() ||
            target.inMovingPlat)
        {
            Logger.Info("targetは現在キルできない状態です。", "CheckMurder");
            return false;
        }
        // targetが既に死んでいないか
        if (!target.IsAlive())
        {
            Logger.Info("targetは既に死んでいたため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }
        // 会議中・または会議コール直後のキルでないか
        if (MeetingHud.Instance != null || MeetingStates.MeetingCalled)
        {
            Logger.Info("会議中（またはコール直後）のためキルをキャンセルしました。", "CheckMurder");
            return false;
        }

        // 連打キルでないか
        if (!killerMyselfInvoke)
        {
            float minTime = Mathf.Max(0.02f, AmongUsClient.Instance.Ping / 1000f * 6f); //※AmongUsClient.Instance.Pingの値はミリ秒(ms)なので÷1000
            //TimeSinceLastKillに値が保存されていない || 保存されている時間がminTime以上 => キルを許可
            //↓許可されない場合
            if (TimeSinceLastKill.TryGetValue(killer.PlayerId, out var time) && time < minTime)
            {
                Logger.Info("前回のキルからの時間が早すぎるため、キルをブロックしました。", "CheckMurder");
                return false;
            }
        }
        TimeSinceLastKill[killer.PlayerId] = 0f;

        // HideAndSeek_キルボタンが使用可能か
        if ((Options.CurrentGameMode == CustomGameMode.HideAndSeek || Options.IsStandardHAS) && Options.HideAndSeekKillDelayTimer > 0)
        {
            Logger.Info("HideAndSeekの待機時間中だったため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }
        // キルが可能なプレイヤーか(遠隔は除く)
        if (!info.IsFakeSuicide && !killer.CanUseKillButton())
        {
            Logger.Info(killer.GetNameWithRole() + "はKillできないので、キルはキャンセルされました。", "CheckMurder");
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
class MurderPlayerPatch
{
    private static readonly LogHandler logger = Logger.Handler(nameof(PlayerControl.MurderPlayer));
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] MurderResultFlags resultFlags, ref bool __state /* 成功したキルかどうか */ )
    {
        logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}({resultFlags})");
        var isProtectedByClient = resultFlags.HasFlag(MurderResultFlags.DecisionByHost) && target.IsProtected();
        var isProtectedByHost = resultFlags.HasFlag(MurderResultFlags.FailedProtected);
        var isFailed = resultFlags.HasFlag(MurderResultFlags.FailedError);
        var isSucceeded = __state = !isProtectedByClient && !isProtectedByHost && !isFailed;
        if (isProtectedByClient)
        {
            logger.Info("守護されているため，キルは失敗します");
        }
        if (isProtectedByHost)
        {
            logger.Info("守護されているため，キルはホストによってキャンセルされました");
        }
        if (isFailed)
        {
            logger.Info("キルはホストによってキャンセルされました");
        }

        if (isSucceeded)
        {
            if (target.shapeshifting)
            {
                //シェイプシフトアニメーション中
                //アニメーション時間を考慮して1s、加えてクライアントとのラグを考慮して+0.5s遅延する
                _ = new LateTask(
                    () =>
                    {
                        if (GameStates.IsInTask)
                        {
                            target.RpcShapeshift(target, false);
                        }
                    },
                    1.5f, "RevertShapeshift");
            }
            else
            {
                if (Main.CheckShapeshift.TryGetValue(target.PlayerId, out var shapeshifting) && shapeshifting)
                {
                    //シェイプシフト強制解除
                    target.RpcShapeshift(target, false);
                }
            }
            Camouflage.RpcSetSkin(Camouflage.IsCamouflage, target, ForceRevert: true, RevertToDefault: true);
            if (Options.IsSyncColorMode && Options.SCM_RestoredDeadPlayer.GetBool())
            {
                SkinChangeMode.RpcSetSkin(target, target);
            }
        }
    }
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, bool __state)
    {
        if (!__state) return; // キルが成功していない場合，何もしない

        if (target.AmOwner) RemoveDisableDevicesPatch.UpdateDisableDevices();
        if (!target.Data.IsDead || !AmongUsClient.Instance.AmHost) return;
        // 以降ホストしか処理しない
        // 処理は全てCustomRoleManager側で行う
        CustomRoleManager.OnMurderPlayer(__instance, target);
        _ = new LateTask(() =>
        {
            AntiBlackout.SetRoleChange(__instance);
        }, 0.5f, "PostMurderSetRoleChange");
        var killer = __instance;
        bool killerIsViper = killer.Is(CustomRoles.Viper) || killer.Is(CustomRoles.NormalViper);
        if (killerIsViper)
        {
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.GetClientId() == -1) continue;
                killer.RpcSetRoleDesync(RoleTypes.Viper, seer.GetClientId());
            }
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckShapeshift))]
public static class PlayerControlCheckShapeshiftPatch
{
    private static readonly LogHandler logger = Logger.Handler(nameof(PlayerControl.CheckShapeshift));

    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] bool shouldAnimate)
    {
        // 変身先がダミーの時は CustomNetObject 側で処理している(白狐はダミーを選んでも食べられる)
        if (CustomNetObject.Is(target) || CustomNetObject.Is(__instance)) return false;
        if (AmongUsClient.Instance.IsGameOver || !AmongUsClient.Instance.AmHost)
        {
            return false;
        }

        // 白狐はシェイプを「お揚げを食べる」ボタンとして使う(変身はしない)。
        // 白狐の画面では変身したままに見えていることがあり、その時は自分を選んだ「変身解除」の合図が来る。
        // 下の無効な変身のチェックで弾かれると食べられないので、白狐はここで先に処理する
        if (__instance.GetRoleClass() is Roles.Neutral.KitsuneSpirit kitsune)
        {
            var animate = false;
            kitsune.OnCheckShapeshift(target ?? __instance, ref animate);
            kitsune.RespondShapeshift();
            return false;
        }

        // 無効な変身を弾く．これより前に役職等の処理をしてはいけない
        if (!CheckInvalidShapeshifting(__instance, target, shouldAnimate))
        {
            __instance.RpcRejectShapeshift();
            return false;
        }
        var shapeshifter = __instance;
        var shapeshifting = shapeshifter.PlayerId != target.PlayerId;
        // 変身したとき一番近い人をマッドメイトにする処理
        if (shapeshifter.CanMakeMadmate() && shapeshifting)
        {
            var sidekickable = shapeshifter.GetRoleClass() as ISidekickable;
            var targetRole = sidekickable?.SidekickTargetRole ?? CustomRoles.SKMadmate;

            Vector2 shapeshifterPosition = shapeshifter.transform.position;//変身者の位置
            Dictionary<PlayerControl, float> mpdistance = new();
            float dis;
            foreach (var p in Main.AllAlivePlayerControls)
            {
                if (p.Data.Role.Role != RoleTypes.Shapeshifter && !p.Is(CustomRoleTypes.Impostor) && !p.Is(targetRole))
                {
                    dis = Vector2.Distance(shapeshifterPosition, p.transform.position);
                    mpdistance.Add(p, dis);
                }
            }
            if (mpdistance.Count != 0)
            {
                var min = mpdistance.OrderBy(c => c.Value).FirstOrDefault();//一番値が小さい
                PlayerControl targetm = min.Key;
                targetm.RpcSetCustomRole(targetRole);
                Logger.Info($"Make SKMadmate:{targetm.name}", "Shapeshift");
                Main.SKMadmateNowCount++;
                Utils.MarkEveryoneDirtySettings();
                Utils.NotifyRoles();
            }
        }
        // 役職の処理
        var role = shapeshifter.GetRoleClass();
        if (role?.OnCheckShapeshift(target, ref shouldAnimate) == false)
        {
            if (role.CanDesyncShapeshift)
            {
                shapeshifter.RpcSpecificRejectShapeshift(target, shouldAnimate);
            }
            else
            {
                shapeshifter.RpcRejectShapeshift();
            }
            return false;
        }

        shapeshifter.RpcShapeshift(target, shouldAnimate);
        return false;
    }
    private static bool CheckInvalidShapeshifting(PlayerControl instance, PlayerControl target, bool animate)
    {
        logger.Info($"Checking shapeshift {instance.GetNameWithRole()} -> {(target == null || target.Data == null ? "(null)" : target.GetNameWithRole())}");

        if (!target || target.Data == null)
        {
            logger.Info("targetがnullのため変身をキャンセルします");
            return false;
        }
        if (!instance.IsAlive())
        {
            logger.Info("変身者が死亡しているため変身をキャンセルします");
            return false;
        }
        // RoleInfoによるdesyncシェイプシフター用の判定を追加
        if (instance.Data.Role.Role != RoleTypes.Shapeshifter && instance.GetCustomRole().GetRoleInfo()?.BaseRoleType?.Invoke() != RoleTypes.Shapeshifter)
        {
            logger.Info("変身者がシェイプシフターではないため変身をキャンセルします");
            return false;
        }
        if (instance.Data.Disconnected)
        {
            logger.Info("変身者が切断済のため変身をキャンセルします");
            return false;
        }
        if (target.IsMushroomMixupActive() && animate)
        {
            logger.Info("キノコカオス中のため変身をキャンセルします");
            return false;
        }
        if (MeetingHud.Instance && animate)
        {
            logger.Info("会議中のため変身をキャンセルします");
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Shapeshift))]
class ShapeshiftPatch
{
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        // ダミー(CustomNetObject)の見た目合わせの変身は、プレイヤーの変身として扱わない
        if (CustomNetObject.Is(__instance)) return;
        Logger.Info($"{__instance?.GetNameWithRole()} => {target?.GetNameWithRole()}", "Shapeshift");

        var shapeshifter = __instance;
        var shapeshifting = shapeshifter.PlayerId != target.PlayerId;

        if (Main.CheckShapeshift.TryGetValue(shapeshifter.PlayerId, out var last) && last == shapeshifting)
        {
            Logger.Info($"{__instance?.GetNameWithRole()}:Cancel Shapeshift.Prefix", "Shapeshift");
            return;
        }

        Main.CheckShapeshift[shapeshifter.PlayerId] = shapeshifting;
        Main.ShapeshiftTarget[shapeshifter.PlayerId] = target.PlayerId;

        shapeshifter.GetRoleClass()?.OnShapeshift(target);

        if (!AmongUsClient.Instance.AmHost) return;

        if (!shapeshifting) Camouflage.RpcSetSkin(Camouflage.IsCamouflage, __instance);

        //変身解除のタイミングがずれて名前が直せなかった時のために強制書き換え
        if (!shapeshifting)
        {
            _ = new LateTask(() =>
            {
                Utils.NotifyRoles(NoCache: true);
            },
            1.2f, "ShapeShiftNotify");
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckVanish))]
public static class PlayerControlCheckVanishPatch
{
    private static readonly LogHandler logger = Logger.Handler(nameof(PlayerControl.CheckVanish));

    public static bool Prefix(PlayerControl __instance)
    {
        if (AmongUsClient.Instance.IsGameOver)
        {
            return false;
        }

        if (!AmongUsClient.Instance.AmHost)
        {
            return true;
        }

        var phantom = __instance;
        Logger.Info($"{phantom.GetNameWithRole()} : CheckVanishStart", "CheckVanish");

        // 役職の処理
        var role = phantom.GetRoleClass();

        /***************************** 
         * ◆下記OnCheckVanishがfalseの場合は設定できる
         * ・killCooldown : キルクールが10秒に設定されてしまうため任意に設定する
         * 　　ホストは仕様上キルクールは強制されないが、バニラの仕様に合わせセットする
         * ・canResetAbilityCooldown : アビリティクールダウンをリセットするか設定する
         *****************************/
        float killCooldown = 10.0f;
        bool canResetAbilityCooldown = false;
        if (role?.OnCheckVanish(ref killCooldown, ref canResetAbilityCooldown) == false)
        {
            Logger.Info($"{phantom.GetNameWithRole()} : OnCheckVanish() == false", "CheckVanish");

            if (phantom.IsAlive())
            {
                if (phantom.PlayerId != PlayerControl.LocalPlayer.PlayerId
                    && role.Player.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true)
                {
                    // ほかの人にクルーに見せている役職は、一瞬透明にする方法だと
                    // 本人の体が透けたり、ほかの人がベントから追い出されたりする。
                    // 透明にはせず、本人にだけ役職を送り直してボタンを元に戻す (TownOfHost-hamo と同じやり方)
                    ResetPhantomButtonForSelf(phantom);
                }
                else if (phantom.PlayerId != PlayerControl.LocalPlayer.PlayerId)
                {
                    SendVanishToSelfOnly(phantom);
                    _ = new LateTask(() =>
                    {
                        if (phantom != null && phantom.IsAlive()) SendAppearToSelfOnly(phantom);
                    }, role.VanishCancelAppearDelay, "PhantomVanishCancel");
                }
                else
                {
                    phantom.RpcResetAbilityCooldown();
                }
            }

            // キルクールリセット           
            if (killCooldown >= 0f)
                phantom.SetKillCooldown(killCooldown);
            // アビリティクールリセット
            if (canResetAbilityCooldown)
            {
                phantom.RpcResetAbilityCooldown();
            }
            return false;
        }

        return true;
    }
    private static void ResetPhantomButtonForSelf(PlayerControl phantom)
    {
        var clientId = phantom.GetClientId();
        foreach (var roleType in new[] { RoleTypes.Impostor, RoleTypes.Phantom })
        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                phantom.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, clientId);
            writer.Write((ushort)roleType);
            writer.Write(true);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }
    private static void SendVanishToSelfOnly(PlayerControl phantom)
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            phantom.NetId, (byte)RpcCalls.StartVanish, SendOption.Reliable, phantom.GetClientId());
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    private static void SendAppearToSelfOnly(PlayerControl phantom)
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            phantom.NetId, (byte)RpcCalls.StartAppear, SendOption.Reliable, phantom.GetClientId());
        writer.Write(false);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CmdCheckVanish))]
public static class PlayerControlCmdCheckVanishPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        if (!AmongUsClient.Instance.AmHost)
        {
            return true;
        }

        __instance?.CheckVanish();
        return false;
    }
}
[HarmonyPatch(typeof(PhantomRole), nameof(PhantomRole.UseAbility))]
public static class PhantomRoleUseAbilityPatch
{
    public static bool Prefix(PhantomRole __instance)
    {
        if (__instance.Player.AmOwner && !__instance.Player.Data.IsDead && __instance.Player.moveable && !Minigame.Instance && !__instance.IsCoolingDown && !__instance.fading)
        {
            System.Func<RoleEffectAnimation, bool> roleEffectAnimation = x => x.effectType == RoleEffectAnimation.EffectType.Vanish_Charge;
            if (!__instance.Player.currentRoleAnimations.Find(roleEffectAnimation) && !__instance.Player.walkingToVent && !__instance.Player.inMovingPlat)
            {
                if (__instance.isInvisible)
                {
                    __instance.MakePlayerVisible(true, true);
                    return false;
                }
                DestroyableSingleton<HudManager>.Instance.AbilityButton.SetSecondImage(__instance.Ability);
                DestroyableSingleton<HudManager>.Instance.AbilityButton.OverrideText(DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.PhantomAbilityUndo, new Il2CppReferenceArray<Il2CppSystem.Object>(0)));
                __instance.Player.CmdCheckVanish(10f);
                return false;
            }
        }
        // いずれの条件も満たさない場合はバニラ処理に委ねる
        return true;
    }

    private static float GetPhantomDuration()
    {
        var options = GameManager.Instance.LogicOptions;
        float duration = 5f;
        try
        {
            var field = options.GetType().GetField("_phantomDuration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                duration = (float)field.GetValue(options);
        }
        catch
        {
        }
        return duration;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
class ReportDeadBodyPatch
{
    public static NetworkedPlayerInfo reporter;
    public static NetworkedPlayerInfo ReportTarget;
    public static bool SpecialMeeting = reporter?.PlayerId == ReportTarget?.PlayerId;

    public static List<byte> CannotReportList;
    public static List<byte> CannotReportByDeadBodyList;
    public static List<byte> DontReportMarkList;
    public static Dictionary<byte, List<NetworkedPlayerInfo>> WaitReport = new();

    private static bool forceNext;
    /// <summary>
    /// 強制会議(設定画面のボタン・/fm)。緊急ボタンと同じ見た目(アニメーション)で開き、
    /// ボタンの回数・サボタージュ中・通報禁止などの制限は通さないが、会議の前の処理(役職の処理・名前の表示など)は普通に行う
    /// </summary>
    public static void ForceMeeting(PlayerControl reporter)
    {
        if (reporter == null || !AmongUsClient.Instance.AmHost) return;
        forceNext = true;
        reporter.ReportDeadBody(null);
        forceNext = false;
    }

    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] NetworkedPlayerInfo target)
    {
        var forced = forceNext;
        forceNext = false;
        reporter = __instance.Data;
        ReportTarget = target;
        SpecialMeeting = forced || reporter?.PlayerId == ReportTarget?.PlayerId;
        if (GameStates.IsMeeting) return false;
        Logger.Info($"{__instance.GetNameWithRole()} => {target?.Object?.GetNameWithRole() ?? "null"}{(forced ? " (強制会議)" : "")}", "ReportDeadBody");
        if (forced && AmongUsClient.Instance.AmHost && !__instance.Data.IsDead) goto MeetingConfirmed;
        if (SpecialMeeting) return true;
        if (Options.IsStandardHAS && target != null && __instance == target.Object) return true; //[StandardHAS] ボタンでなく、通報者と死体が同じなら許可
        if (Options.CurrentGameMode == CustomGameMode.HideAndSeek || Options.IsStandardHAS) return false;
        if (Options.IsCCMode && CatchCat.Option.IgnoreReport.GetBool() && target != null) return false;
        // ニートは緊急会議を開けない(設定)
        if (target == null && Roles.Neutral.Neet.CannotCallEmergency(__instance))
        {
            Logger.Info("ニートは緊急会議を開けない", "ReportDeadBody");
            return false;
        }
        if (target == null && Options.DisableButtonInMushroomMixup.GetBool() && MushroomMixupUpdateSystemPatch.InSabotage)
        {
            Logger.Info("DisableButton InMushroomMixup", "ReportDeadBody");
            return false;
        }
        if ((reporter.Object.Is(CustomRoles.NonReport) || reporter.Object.Is(CustomRoles.FoxSpirit)) &&
            target != null && !target.Object.Is(CustomRoles.Bait) && !target.Object.Is(CustomRoles.AddBait))
        {
            DontReportMarkList.Add(reporter.PlayerId);
            Utils.NotifyRoles(SpecifySeer: reporter.Object);
            _ = new LateTask(() =>
            {
                DontReportMarkList.Remove(reporter.PlayerId);
                Utils.NotifyRoles(SpecifySeer: reporter.Object);
            }, 5f, "Don't Report Mark Remove");
            return false;
        }
        // Scavenger
        if (target != null && CannotReportByDeadBodyList.Contains(target.PlayerId))
        {
            DontReportMarkList.Add(reporter.PlayerId);
            Utils.NotifyRoles(SpecifySeer: reporter.Object);
            _ = new LateTask(() =>
            {
                DontReportMarkList.Remove(reporter.PlayerId);
                Utils.NotifyRoles(SpecifySeer: reporter.Object);
            }, 5f, "Scavenger DontReportMark Remove");
            return false;
        }

        if (CannotReportList.Contains(__instance.PlayerId))
        {
            WaitReport[__instance.PlayerId].Add(target);
            Logger.Warn($"{__instance.GetNameWithRole()}:通報禁止中のため可能になるまで待機します", "ReportDeadBody");
            return false;
        }

        //会議ボタンの場合、サボタージュ中に呼び出しを受けない
        if (target == null && Utils.IsActiveDontOpenMeetingSabotage(out var sabotage))
        {
            Logger.Warn($"{sabotage}サボタージュ中です", "ReportDeadBody");
            return false;
        }

        if (!AmongUsClient.Instance.AmHost) return true;

        //通報者が死んでいる場合、本処理で会議がキャンセルされるのでここで止める
        if (__instance.Data.IsDead) return false;

        if (Options.SyncButtonMode.GetBool() && target == null)
        {
            Logger.Info("最大:" + Options.SyncedButtonCount.GetInt() + ", 現在:" + Options.UsedButtonCount, "ReportDeadBody");
            if (Options.SyncedButtonCount.GetFloat() <= Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数を超えているため、ボタンはキャンセルされました。", "ReportDeadBody");
                return false;
            }
            else Options.UsedButtonCount++;
            if (Options.SyncedButtonCount.GetFloat() == Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数に達しました。", "ReportDeadBody");
            }
        }

    //=============================================
    //以下、ボタンが押されることが確定したものとする。
    //=============================================
    MeetingConfirmed:
        MeetingStates.MeetingCalled = true;

        foreach (var pc in Main.AllPlayerControls)
        {
            var state = PlayerState.GetByPlayerId(pc.PlayerId);

            // ブラックアウト
            if (pc.IsAlive())
            {
                state.IsBlackOut = true;
                pc.MarkDirtySettings();
            }

            // 最終位置の部屋の取得・記憶
            state.LastRoom = pc.GetPlainShipRoom();

            // 変身解除処理
            if (Main.CheckShapeshift.ContainsKey(pc.PlayerId))
            {
                Camouflage.RpcSetSkin(Camouflage.IsCamouflage, pc, RevertToDefault: true);
            }
        }

        MushroomMixupDeterioratePatch.RestorName();

        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
        {
            role.OnReportDeadBody(__instance, target);
        }

        // 役職変更表示のリセット
        Main.ShowChangeMainRole.Clear();

        MeetingTimeManager.OnReportDeadBody();

        Utils.NotifyRoles(isForMeeting: true, NoCache: true);

        Utils.SyncAllSettings();

        if (!__instance.IsAlive())
        {
            //通報者が通報処理中に死亡した場合の強制会議実行
            RPC.ReportDeadBodyForced(__instance, target);
            return false;
        }

        return true;
    }
    public static async void ChangeLocalNameAndRevert(string name, int time)
    {
        //async Taskじゃ警告出るから仕方ないよね。
        var revertName = PlayerControl.LocalPlayer.name;
        PlayerControl.LocalPlayer.RpcSetNameEx(name);
        await Task.Delay(time);
        PlayerControl.LocalPlayer.RpcSetNameEx(revertName);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
public static class PlayerControlStartMeetingPatch
{
    public static void Prefix()
    {
        //foreach (var kvp in PlayerState.AllPlayerStates)
        //{
        //    var pc = Utils.GetPlayerById(kvp.Key);
        //    kvp.Value.LastRoom = pc.GetPlainShipRoom();
        //}
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
class PlayerStartPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        var roleText = UnityEngine.Object.Instantiate(__instance.cosmetics.nameText);
        roleText.transform.SetParent(__instance.cosmetics.nameText.transform);
        roleText.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        roleText.transform.localScale = new(1f, 1f, 1f);
        roleText.fontSize = Main.RoleTextSize;
        roleText.text = "RoleText";
        roleText.gameObject.name = "RoleText";
        roleText.enabled = false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetColor))]
class SetColorPatch
{
    public static bool IsAntiGlitchDisabled = false;
    public static bool Prefix(PlayerControl __instance, int bodyColor)
    {
        //色変更バグ対策
        if (!AmongUsClient.Instance.AmHost || __instance.CurrentOutfit.ColorId == bodyColor || IsAntiGlitchDisabled) return true;
        if ((AmongUsClient.Instance.IsGameStarted && Options.CurrentGameMode == CustomGameMode.HideAndSeek)
            || (AmongUsClient.Instance.IsGameStarted && Options.IsCCMode))
        {
            //ゲーム中に色を変えた場合
            __instance.RpcMurderPlayer(__instance);
        }
        return true;
    }
}
/// <summary>
/// ベントを使えない人をベントから出す。
/// 自分の画面でだけインポスターの役職は本人の画面ではベントに入れてしまうので、ホストが追い出す。
/// 追い出すまでの間にほかのベントへ移られると、入ったベントの番号で追い出しても空振りして、
/// ほかの人の画面ではベントに入ったまま(透明)になる。今いるベントを調べて、出るまで追い出し直す
/// </summary>
static class VentBooter
{
    private static readonly HashSet<byte> booting = new();

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        booting.Clear();
        enteredPos.Clear();
    }

    /// <summary>入ったベントの位置(ほかのベントへ移って出ても、ここへ戻す)</summary>
    private static readonly Dictionary<byte, Vector2> enteredPos = new();

    public static void Boot(PlayerPhysics physics, int enteredId)
    {
        var pc = physics?.myPlayer;
        if (pc == null || !booting.Add(pc.PlayerId)) return;
        try
        {
            var vent = ShipStatus.Instance?.AllVents?.FirstOrDefault(v => v != null && v.Id == enteredId);
            if (vent != null) enteredPos[pc.PlayerId] = (Vector2)vent.transform.position + new Vector2(0f, 0.3636f);
        }
        catch { }
        BootLoop(physics, pc.PlayerId, enteredId, 0);
    }

    /// <summary>追い出し終わり: 残った inVent を消し、ほかのベントへ移って出ていたら入ったベントへ戻す</summary>
    private static void Finish(PlayerPhysics physics, byte playerId)
    {
        booting.Remove(playerId);
        var pc = physics?.myPlayer;
        if (pc == null) { enteredPos.Remove(playerId); return; }
        if (CurrentVentId(playerId) == null)
        {
            pc.inVent = false;
            pc.walkingToVent = false;
        }
        if (enteredPos.Remove(playerId, out var pos) && GameStates.IsInTask && pc.IsAlive()
            && Vector2.Distance(pc.GetTruePosition(), pos) > 1.5f)
        {
            Logger.Info($"{pc.GetNameWithRole()} : ベントで移動したので、入ったベントへ戻す", "VentBooter");
            pc.RpcSnapTo(pos);
        }
    }

    private static void BootLoop(PlayerPhysics physics, byte playerId, int lastId, int count)
    {
        _ = new LateTask(() =>
        {
            if (physics == null || physics.myPlayer == null || !GameStates.IsInTask || count >= 10)
            {
                Finish(physics, playerId);
                return;
            }
            var ventId = CurrentVentId(playerId) ?? lastId;
            physics.RpcBootFromVent(ventId);
            // 少し待って、まだベントの中なら今いるベントでもう一度
            _ = new LateTask(() =>
            {
                if (physics != null && physics.myPlayer != null && GameStates.IsInTask
                    && (CurrentVentId(playerId) != null || physics.myPlayer.inVent))
                    BootLoop(physics, playerId, CurrentVentId(playerId) ?? ventId, count + 1);
                else
                    Finish(physics, playerId);
            }, 0.6f, "Cancel Vent Check");
        }, count == 0 ? 0.3f : 0f, "Cancel Vent");
    }

    /// <summary>ホストが知っている、その人が今入っているベントの番号(入っていなければ null)</summary>
    private static int? CurrentVentId(byte playerId)
    {
        try
        {
            if (ShipStatus.Instance == null) return null;
            if (!ShipStatus.Instance.Systems.TryGetValue(SystemTypes.Ventilation, out var systemType)) return null;
            var vent = systemType.TryCast<VentilationSystem>();
            if (vent != null && vent.PlayersInsideVents.TryGetValue(playerId, out var id)) return id;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 本当にベントの中にいるか。ホストの画面の inVent は、追い出した後も true のまま残ることがある
    /// (残ると、その人をキルや九尾の狐の妖術で倒せなくなる)ので、ベントの管理(VentilationSystem)を見る
    /// </summary>
    public static bool IsInVent(PlayerControl pc)
    {
        if (pc == null) return false;
        try
        {
            if (pc.MyPhysics != null && pc.MyPhysics.Animations.IsPlayingEnterVentAnimation()) return true;
            if (ShipStatus.Instance != null && ShipStatus.Instance.Systems.TryGetValue(SystemTypes.Ventilation, out var systemType)
                && systemType.TryCast<VentilationSystem>() is { } vent)
                return vent.PlayersInsideVents.ContainsKey(pc.PlayerId);
        }
        catch { }
        return pc.inVent;
    }
}

[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoEnterVent))]
class CoEnterVentPatch
{
    public static bool Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] int id)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            var user = __instance.myPlayer;
            if (Options.CurrentGameMode == CustomGameMode.HideAndSeek && Options.IgnoreVent.GetBool())
                __instance.RpcBootFromVent(id);


            bool roleAllowsVent = user.GetRoleClass()?.OnEnterVent(__instance, id) ?? true;
            bool ventTaskAllows = VentEnterTask.OnEnterVent(__instance, id);
            bool canVent = (VentEnterTask.HaveTask(user) && ventTaskAllows) ||
                (user.GetCustomRole().IsCCCatRoles() && CatchCat.CatPlayer.CanUseVent(user)) ||
                user.Data.Role.Role == RoleTypes.Engineer ||
                user.CanUseImpostorVentButton();

            if (!roleAllowsVent || !canVent)
            {
                VentBooter.Boot(__instance, id);
            }
            else if (user.GetCustomRole().IsCCCatRoles() && !CatchCat.CatPlayer.CanUseVent(user))
            {
                VentBooter.Boot(__instance, id);
            }
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.RpcEnterVent))]
class RpcEnterVentPatch
{
    public static void Postfix(PlayerPhysics __instance, [HarmonyArgument(0)] int id)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        var user = __instance.myPlayer;
        if (user == null) return;

        bool roleAllowsVent = user.GetRoleClass()?.OnEnterVent(__instance, id) ?? true;
        bool ventTaskAllows = VentEnterTask.OnEnterVent(__instance, id);
        bool canVent = (VentEnterTask.HaveTask(user) && ventTaskAllows) ||
            (user.GetCustomRole().IsCCCatRoles() && CatchCat.CatPlayer.CanUseVent(user)) ||
            user.Data.Role.Role == RoleTypes.Engineer ||
            user.CanUseImpostorVentButton();

        if (!roleAllowsVent || !canVent)
        {
            VentBooter.Boot(__instance, id);
        }
        else if (user.GetCustomRole().IsCCCatRoles() && !CatchCat.CatPlayer.CanUseVent(user))
        {
            VentBooter.Boot(__instance, id);
        }
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetName))]
class SetNamePatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] string name)
    {
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
class PlayerControlCompleteTaskPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        var pc = __instance;

        Logger.Info($"TaskComplete:{pc.GetNameWithRole()}", "CompleteTask");
        var taskState = pc.GetPlayerTaskState();
        taskState.Update(pc);

        var roleClass = pc.GetRoleClass();
        var ret = true;
        if (roleClass != null)
        {
            ret = roleClass.OnCompleteTask();
        }
        //属性クラスの扱いを決定するまで仮置き
        ret &= Workhorse.OnCompleteTask(pc);

        if (Options.IsCCMode) CatchCat.CatPlayer.OnCompleteTask(pc, taskState);

        Utils.NotifyRoles();
        return ret;
    }
    public static void Postfix()
    {
        //人外のタスクを排除して再計算
        GameData.Instance.RecomputeTaskCounts();
        Logger.Info($"TotalTaskCounts = {GameData.Instance.CompletedTasks}/{GameData.Instance.TotalTasks}", "TaskState.Update");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ProtectPlayer))]
class PlayerControlProtectPlayerPatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}", "ProtectPlayer");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RemoveProtection))]
class PlayerControlRemoveProtectionPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        Logger.Info($"{__instance.GetNameWithRole()}", "RemoveProtection");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
class PlayerControlSetRolePatch
{
    private const float GhostRoleSyncDelay = 0.5f;

    public static bool Prefix(PlayerControl __instance, ref RoleTypes roleType, bool canOverrideRole)
    {
        if (RpcSetRoleReplacer.DoReplace()) return true;

        var target = __instance;
        var targetName = __instance.GetNameWithRole();
        Logger.Info($"{targetName} =>{roleType}", "PlayerControl.RpcSetRole");
        if (!ShipStatus.Instance.enabled) return true;
        bool isGhostConversion = roleType is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost;
        if (isGhostConversion)
        {
            var targetIsKiller = target.GetRoleClass() is IKiller;
            var ghostRoles = new Dictionary<PlayerControl, RoleTypes>();
            foreach (var seer in Main.AllPlayerControls)
            {
                var self = seer.PlayerId == target.PlayerId;
                var seerIsKiller = seer.GetRoleClass() is IKiller;

                if ((self && targetIsKiller) || (!seerIsKiller && target.Is(CustomRoleTypes.Impostor)))
                {
                    ghostRoles[seer] = RoleTypes.ImpostorGhost;
                }
                else
                {
                    ghostRoles[seer] = RoleTypes.CrewmateGhost;
                }
                if (Pirate.TargetSetGhostAndTask(target)) ghostRoles[seer] = RoleTypes.CrewmateGhost;
            }
            if (ghostRoles.All(kvp => kvp.Value == RoleTypes.CrewmateGhost))
            {
                roleType = RoleTypes.CrewmateGhost;
            }
            else if (ghostRoles.All(kvp => kvp.Value == RoleTypes.ImpostorGhost))
            {
                roleType = RoleTypes.ImpostorGhost;
            }
            else
            {
                foreach ((var seer, var role) in ghostRoles)
                {
                    Logger.Info($"Desync {targetName} =>{role} for{seer.GetNameWithRole()}", "PlayerControl.RpcSetRole");
                    var capTarget = target;
                    var capRole = role;
                    var capSeer = seer;
                    _ = new LateTask(() =>
                    {
                        capTarget.RpcSetRoleDesync(capRole, capSeer.GetClientId());
                    }, GhostRoleSyncDelay, "DelayedGhostRoleSyncDesync");
                }
                return false;
            }
        }

        if (isGhostConversion)
        {
            var capTarget2 = target;
            var capRoleType = roleType;
            var capCanOverride = canOverrideRole;
            _ = new LateTask(() =>
            {
                capTarget2.RpcSetRoleNormal(capRoleType, capCanOverride);
            }, GhostRoleSyncDelay, "DelayedGhostRoleSyncNormal");
        }
        else
        {
            target.RpcSetRoleNormal(roleType, canOverrideRole);
        }

        return false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Die))]
public static class PlayerControlDiePatch
{
    public static void Postfix(PlayerControl __instance)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            // 死者の最終位置にペットが残るバグ対応
            __instance.RpcSetPet("");
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckUseZipline))]
public static class PlayerControlCheckUseZiplinePatch
{
    public static bool Prefix(PlayerControl target, ZiplineBehaviour ziplineBehaviour, bool fromTop)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            if (Options.FungleCanUseZiplineFromTop.GetBool() && fromTop) return false;
            if (Options.FungleCanUseZiplineFromUnder.GetBool() && !fromTop) return false;
        }

        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckSporeTrigger))]
public static class PlayerControlCheckSporeTriggerPatch
{
    public static bool Prefix()
    {
        return !Options.FungleCanSporeTrigger.GetBool();
    }
}