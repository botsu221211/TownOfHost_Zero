// TownOfHost_L の Modules/ChatInput.cs (GPL-3.0) の、入力できる文字を広げる部分を元にしています。
// (元: More-Roles-Plus src/Commands/ChatInput.cs ← EndlessHostRoles・Aeterna-End-K-not・TownOfNext)
using System;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostY.Modules;

/// <summary>
/// 自分のチャットの入力欄で、全角の数字・英字・記号(１ ２ Ａ ！ ／ など)や &lt; &gt; も打てるようにする。
/// 本編は allowAllCharacters を立てても全角の数字などを落とす。落とすと本編の SetText は
/// カーソルを落とした分も進めたまま位置を測って例外になるので、チャット欄では見える文字を全部通す。
/// 自分の入力欄だけを触り電文は送らないので、ホストでも MOD 入りの客でも使える
/// </summary>
public static class ChatInput
{
    /// <summary>チャット欄の入力欄(文字を全部通す相手)</summary>
    internal static IntPtr ChatArea;

    public static void Apply(FreeChatInputField field)
    {
        var area = field ? field.textArea : null;
        if (!area) return;
        ChatArea = area.Pointer;
        area.allowAllCharacters = true;
        area.AllowSymbols = true;
        area.AllowEmail = true;
        // < > も打てるので、入力欄では <b> などを装飾として読まない
        // (読むと見える文字数と文字列の長さがずれ、本編のカーソル位置の計算が範囲の外を読んで例外を出し続ける)
        if (area.outputText) area.outputText.richText = false;
    }

    /// <summary>入力欄の文字が変わった(本編の SetText の後)。日本語入力の変換中の文字をカーソルの所に描き直す</summary>
    internal static void OnSetText(TextBoxTMP box, string compo)
    {
        if (box.Pointer != ChatArea) return;
        string text = box.text ?? "";
        // 変換中の文字を本編は常に末尾に描く(確定すると本編がカーソルの所へ入れる)
        if (!string.IsNullOrEmpty(compo) && box.caretPos >= 0 && box.caretPos < text.Length && box.outputText)
            box.outputText.text = text.Insert(box.caretPos, compo);
    }

    // この関数の char の引数は下位 1 バイトに切られて届く(一 U+4E00 → U+0000・会 U+4F1A → U+001A・下位バイトが 0x80 以上なら U+FFFD)。
    // 届いた値だけで制御文字を見ると、一・三・上・会・言 などが落ちる。そこで本物の文字を別に持つ:
    // SetText の中では渡された文字列、キー入力の処理ではその時の Input.inputString。
    // 本編は文字を先頭から順に 1 つずつ尋ねるので、届いた値と切った値を照らしながら位置を進める。合わない時は届いた値で判定する。
    // チャット欄以外は本編の判定のまま、切られた値で断られた時だけ本物の文字で本編と同じ範囲を見直す
    internal static string pending;   // 本編が今 1 文字ずつ尋ねている文字列(null = 分からない)
    internal static int pendingAt;
    internal static bool inSetText;
    private static int inputFrame = -1;

    internal static void OnIsCharAllowed(TextBoxTMP box, char c, ref bool result)
    {
        // キー入力の処理から尋ねられた時は、本編が断った時だけその回の入力の文字列を読む(毎フレームは読まない)
        if (!inSetText && !result && inputFrame != Time.frameCount)
        {
            inputFrame = Time.frameCount;
            pending = Input.inputString;
            pendingAt = 0;
        }
        // 通した文字でも位置は進める(進めないと、後の文字を前の位置の文字と取り違える)
        char real = Advance(c);
        if (result) return;
        if (box.Pointer == ChatArea)
        {
            if (!char.IsControl(real)) result = true;
            return;
        }
        if (real != c) result = VanillaAllows(box, real);
    }

    // 切られる 0x80 以上の文字の判定。字か数字なら通し、記号は欄が記号やメールアドレスを許す時だけ本編の表で見る
    private static bool VanillaAllows(TextBoxTMP box, char c)
    {
        if (box.IpMode) return false;
        if (box.allowAllCharacters) return !char.IsControl(c);
        if (char.IsLetterOrDigit(c)) return true;
        if (box.AllowSymbols && TextBoxTMP.SymbolChars != null && TextBoxTMP.SymbolChars.Contains(c)) return true;
        return box.AllowEmail && TextBoxTMP.EmailChars != null && TextBoxTMP.EmailChars.Contains(c);
    }

    // 届いた値 c に合う本物の文字を、尋ねている文字列の今の位置から探して返す(見つからなければ c のまま)
    private static char Advance(char c)
    {
        var s = pending;
        if (s == null) return c;
        for (int i = pendingAt; i < s.Length; i++)
        {
            if (!Matches(s[i], c)) continue;
            pendingAt = i + 1;
            return s[i];
        }
        return c;
    }

    // 本物の文字 real が、1 バイトに切られて c として届いたものか(切られずに届く環境ならそのまま一致する)
    private static bool Matches(char real, char c)
    {
        if (real == c) return true;
        int low = real & 0xFF;
        return low < 0x80 ? c == low : c == (char)0xFFFD;
    }

    /// <summary>
    /// コマンドを読むための半角化。全角の英数字・記号(！〜～)を半角に、全角の空白を半角の空白にする。
    /// 送られた文そのもの(チャットに出る文)は変えない
    /// </summary>
    public static string ToHalfWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (ch == '　') chars[i] = ' ';
            else if (ch >= '！' && ch <= '～') chars[i] = (char)(ch - 0xFEE0);
        }
        return new string(chars);
    }
}

[HarmonyPatch]
internal static class ChatInputPatches
{
    [HarmonyPatch(typeof(FreeChatInputField), nameof(FreeChatInputField.Awake)), HarmonyPostfix]
    public static void Awake(FreeChatInputField __instance)
    {
        try { ChatInput.Apply(__instance); }
        catch (Exception e) { Logger.Error($"入力欄の設定に失敗: {e.Message}", "ChatInput"); }
    }

    // 本編がチャットの種類の切り替えなどで入力欄を作り直した時のため、開くたびにも立て直す
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Toggle)), HarmonyPostfix]
    public static void Toggle(ChatController __instance)
    {
        try { ChatInput.Apply(__instance.freeChatField); }
        catch (Exception e) { Logger.Error($"入力欄の設定に失敗: {e.Message}", "ChatInput"); }
    }

    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.IsCharAllowed)), HarmonyPostfix]
    public static void IsCharAllowed(TextBoxTMP __instance, [HarmonyArgument(0)] char c, ref bool __result)
    {
        try { ChatInput.OnIsCharAllowed(__instance, c, ref __result); }
        catch { }
    }

    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText)), HarmonyPrefix]
    public static void SetTextPrefix([HarmonyArgument(0)] string input, [HarmonyArgument(1)] string inputCompo)
    {
        ChatInput.inSetText = true;
        ChatInput.pending = string.IsNullOrEmpty(inputCompo) ? input : input + inputCompo;
        ChatInput.pendingAt = 0;
    }

    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText)), HarmonyPostfix]
    public static void SetTextPostfix(TextBoxTMP __instance, [HarmonyArgument(1)] string inputCompo)
    {
        try { ChatInput.OnSetText(__instance, inputCompo); }
        catch (Exception e) { Logger.Error($"入力欄の文字の処理に失敗: {e.Message}", "ChatInput"); }
    }

    // 後始末は本編の SetText が例外を出した時も走らせる(残ると、次のキー入力で古い文字列と照らして字を落とす)
    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText)), HarmonyFinalizer]
    public static void SetTextFinalizer()
    {
        ChatInput.inSetText = false;
        ChatInput.pending = null;
    }
}
