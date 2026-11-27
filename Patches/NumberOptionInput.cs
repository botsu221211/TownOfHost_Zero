using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostY;

/// <summary>
/// ルーム設定の数値(＋/－で変える項目)の数字をクリック(タップ)すると、数字を直接打ち込めるようにする (TOH_Zero)。
/// ・クリックすると数字が青い「入力中」になる。数字(全角も可)・小数点・マイナスを打ち、Enter で決定、Esc でやめる
/// ・最小〜最大の間の数だけ受け付ける(外れていたら元のまま)。MOD の設定は刻みに合わせて一番近い値になる
/// ・＋/－を押したり、ほかの数字をクリックしたりすると、打ちかけの入力はやめる
/// </summary>
[HarmonyPatch]
public static class NumberOptionInput
{
    private static NumberOption editing;
    private static string buffer = "";

    /// <summary>入力中の数字と印(_)の色(青っぽい色)</summary>
    private static readonly Color CursorColor = new(0.35f, 0.7f, 1f, 1f);

    // ───── 数字をクリックできるようにする ─────
    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Initialize)), HarmonyPostfix]
    public static void InitializePostfix(NumberOption __instance)
    {
        try { MakeClickable(__instance); }
        catch (Exception ex) { Logger.Warn($"数値の入力欄を作れませんでした: {ex.Message}", nameof(NumberOptionInput)); }
    }

    private static void MakeClickable(NumberOption option)
    {
        var text = option.ValueText;
        if (text == null) return;
        var go = text.gameObject;
        if (go.GetComponent<PassiveButton>() != null) return;

        var collider = go.AddComponent<BoxCollider2D>();
        var rt = text.rectTransform;
        // ＋/－のボタンに重ならない大きさにする
        collider.size = new Vector2(Mathf.Clamp(rt.sizeDelta.x, 0.4f, 0.8f), Mathf.Clamp(rt.sizeDelta.y, 0.25f, 0.4f));
        collider.isTrigger = true;

        var button = go.AddComponent<PassiveButton>();
        button.OnClick = new();
        button.OnMouseOver = new();
        button.OnMouseOut = new();
        // スクロールの枠の外ではクリックさせない(＋ボタンと同じ枠を使う)
        var plus = option.PlusBtn;
        if (plus != null) button.ClickMask = plus.ClickMask;
        var target = option;
        button.OnClick.AddListener((Action)(() => Begin(target)));
    }

    // ───── 入力 ─────
    private static void Begin(NumberOption option)
    {
        if (option == null) return;
        if (editing != null && editing != option) Cancel();
        editing = option;
        buffer = "";
        Draw();
    }

    private static void Draw()
    {
        if (editing == null || editing.ValueText == null) return;
        editing.ValueText.text = Utils.ColorString(CursorColor, buffer + "_");
    }

    /// <summary>入力をやめて、今の値の表示に戻す</summary>
    private static void Cancel()
    {
        var option = editing;
        editing = null;
        buffer = "";
        if (option == null) return;
        try
        {
            // 値が変わった時だけ表示を作り直す仕組みなので、作り直させる
            option.oldValue = float.MinValue;
        }
        catch { }
    }

    private static void Commit()
    {
        var option = editing;
        var typed = buffer;
        Cancel();
        if (option == null || typed.Length == 0) return;
        if (!float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return;

        if (ModGameOptionsMenu.OptionList.TryGetValue(option, out var index) && index >= 0 && index < OptionItem.AllOptions.Count)
        {
            var item = OptionItem.AllOptions[index];
            switch (item)
            {
                case IntegerOptionItem intItem:
                    {
                        var rule = intItem.Rule;
                        if (value < rule.MinValue || value > rule.MaxValue) { Logger.Info($"{item.Name}: {typed} は範囲外", nameof(NumberOptionInput)); return; }
                        var i = rule.GetNearestIndex((int)Math.Round(value));
                        intItem.SetValue(i);
                        option.Value = rule.GetValueByIndex(i);
                        break;
                    }
                case FloatOptionItem floatItem:
                    {
                        var rule = floatItem.Rule;
                        if (value < rule.MinValue - 0.0001f || value > rule.MaxValue + 0.0001f) { Logger.Info($"{item.Name}: {typed} は範囲外", nameof(NumberOptionInput)); return; }
                        var i = rule.GetNearestIndex(value);
                        floatItem.SetValue(i);
                        option.Value = rule.GetValueByIndex(i);
                        break;
                    }
                default:
                    return;
            }
            option.oldValue = float.MinValue;
            Logger.Info($"{item.Name} を {item.GetString()} にしました(数字の入力)", nameof(NumberOptionInput));
            return;
        }

        // 本編の設定(キルクールなど)
        var range = option.ValidRange;
        if (value < range.min - 0.0001f || value > range.max + 0.0001f) { Logger.Info($"{typed} は範囲外 ({range.min}〜{range.max})", nameof(NumberOptionInput)); return; }
        if (option.Increment > 0f)
            value = range.min + Mathf.Round((value - range.min) / option.Increment) * option.Increment;
        option.Value = Mathf.Clamp(value, range.min, range.max);
        option.UpdateValue();
        option.oldValue = float.MinValue;
    }

    /// <summary>毎フレーム: 入力中なら打った文字を読む</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update)), HarmonyPostfix]
    public static void HudUpdatePostfix()
    {
        if (editing == null) return;
        // 設定画面が閉じた・部品が消えた
        if (!editing || editing.ValueText == null || !editing.gameObject.activeInHierarchy)
        {
            editing = null;
            buffer = "";
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Commit(); return; }

        var input = Input.inputString;
        if (string.IsNullOrEmpty(input)) return;
        foreach (var raw in Modules.ChatInput.ToHalfWidth(input))
        {
            var ch = raw;
            if (ch == '\b')
            {
                if (buffer.Length > 0) buffer = buffer[..^1];
            }
            else if (ch is '\n' or '\r')
            {
                Commit();
                return;
            }
            else if (char.IsDigit(ch) && ch <= '9' || (ch == '.' && !buffer.Contains('.')) || (ch == '-' && buffer.Length == 0))
            {
                if (buffer.Length < 8) buffer += ch;
            }
        }
        Draw();
    }

    // ＋/－を押したら、打ちかけの入力はやめる
    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Increase)), HarmonyPrefix]
    public static void IncreasePrefix(NumberOption __instance) { if (editing == __instance) Cancel(); }
    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Decrease)), HarmonyPrefix]
    public static void DecreasePrefix(NumberOption __instance) { if (editing == __instance) Cancel(); }
}
