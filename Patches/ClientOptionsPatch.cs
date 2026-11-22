using HarmonyLib;

using TownOfHostY.Modules.ClientOptions;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Start))]
    public static class OptionsMenuBehaviourStartPatch
    {
        private static ClientActionItem UnloadMod;
        private static ClientActionItem DumpLog;
        private static ClientActionItem OpenLogFolder;
        private static ClientActionItem IsMuteLobbyBGM;
        private static ClientActionItem AbandonGame;
        private static float abandonPressedAt = float.NegativeInfinity;
        private const float AbandonConfirmSeconds = 4f;

        public static void Postfix(OptionsMenuBehaviour __instance)
        {
            if (__instance.DisableMouseMovement == null)
            {
                return;
            }

            if (UnloadMod == null || UnloadMod.ToggleButton == null)
            {
                UnloadMod = ClientActionItem.Create("UnloadMod", ModUnloaderScreen.Show, __instance);
            }
            if (DumpLog == null || DumpLog.ToggleButton == null)
            {
                DumpLog = ClientActionItem.Create("DumpLog", Utils.DumpLog, __instance);
            }
            if (OpenLogFolder == null || OpenLogFolder.ToggleButton == null)
            {
                OpenLogFolder = ClientActionItem.Create("OpenLogFolder", Utils.OpenLogFolder, __instance);
            }
            if (IsMuteLobbyBGM == null || IsMuteLobbyBGM.ToggleButton == null)
            {
                IsMuteLobbyBGM = ClientOptionItem.Create("IsMuteLobbyBGM", Main.IsMuteLobbyBGM, __instance);
            }

            if (AbandonGame == null || AbandonGame.ToggleButton == null)
            {
                AbandonGame = ClientActionItem.Create("AbandonGame", OnAbandonClicked, __instance);
            }
            abandonPressedAt = float.NegativeInfinity;
            if (AbandonGame?.ToggleButton != null)
                AbandonGame.ToggleButton.Text.text = Translator.GetString("AbandonGame");

            if (ModUnloaderScreen.Popup == null)
            {
                ModUnloaderScreen.Init(__instance);
            }
        }

        /// <summary>廃村ボタン: 試合中のホストだけ。誤操作防止に、4秒以内にもう一度押したら廃村</summary>
        private static void OnAbandonClicked()
        {
            var label = AbandonGame?.ToggleButton?.Text;
            if (!AmongUsClient.Instance.AmHost || !GameStates.IsInGame || !Main.IsSetRoleFinished)
            {
                if (label != null) label.text = Translator.GetString("AbandonGameUnavailable");
                return;
            }
            if (UnityEngine.Time.realtimeSinceStartup - abandonPressedAt > AbandonConfirmSeconds)
            {
                abandonPressedAt = UnityEngine.Time.realtimeSinceStartup;
                if (label != null) label.text = Translator.GetString("AbandonGameConfirm");
                return;
            }
            abandonPressedAt = float.NegativeInfinity;
            if (label != null) label.text = Translator.GetString("AbandonGame");
            if (Modules.AutoAbort.EndAsDraw("設定画面の廃村ボタン"))
            {
                // 設定画面を閉じる
                if (ClientActionItem.CustomBackground != null) ClientActionItem.CustomBackground.gameObject.SetActive(false);
                UnityEngine.Object.FindObjectOfType<OptionsMenuBehaviour>()?.Close();
            }
        }
    }

    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Close))]
    public static class OptionsMenuBehaviourClosePatch
    {
        public static void Postfix()
        {
            if (ClientActionItem.CustomBackground != null)
            {
                ClientActionItem.CustomBackground.gameObject.SetActive(false);
            }
            ModUnloaderScreen.Hide();
        }
    }
}
