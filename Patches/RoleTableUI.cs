using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TownOfHostY.Roles.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHostY;

/// <summary>
/// 役職タブ(インポスター〜コンビネーション)を「役職名・出現確率・最大数・？・⚙」の表で表示する
/// ⚙ を押すとその役職の詳細設定だけを表示し、？ を押すと役職の説明を行の下に開く
/// </summary>
public static class RoleTableUI
{
    // ==== レイアウト調整用 (設定一覧の中の座標) ====
    private const float StartY = 2.0f;
    private const float OptionX = 0.952f;   // 既存の設定行の基準X
    private const float HeaderX = -0.903f;  // 既存の見出しの基準X
    private const float RowHeight = 0.45f;
    private const float SectionHeight = 0.63f;
    private const float TitleBlockHeight = 1.0f;
    private const float ColumnBlockHeight = 0.5f;
    // 一覧の見える範囲はおよそ X=-2.1〜3.35
    private const float NameLeft = -2.0f;       // 役職名の列の左端
    private const float NameWidth = 1.9f;       // 役職名の列の幅
    private const float PercentCenter = 0.68f;  // 出現確率の列の中心
    private const float PercentSpan = 1.12f;    // 出現確率の － と ＋ の中心どうしの間隔
    private const float PercentBoxWidth = 0.7f; // 出現確率の値の枠の幅
    private const float MaxCenter = 2.1f;       // 最大数の列の中心
    private const float MaxSpan = 1.0f;
    private const float MaxBoxWidth = 0.62f;
    private const float HelpX = 2.98f;
    private const float GearX = 3.42f;
    private const float SmallButtonPpu = 280f;  // 120px → 0.43
    private const float SmallButtonCollider = 0.41f;
    private const float IconPpu = 300f;         // 160px → 0.53
    private const float InfoWidth = 4.9f;
    private const float InfoLineHeight = 0.27f; // 説明文の高さが測れなかった時の1行の高さ
    private const int InfoCharsPerLine = 26;

    private class RowUI
    {
        public CustomRoles Role;
        public string Reading;
        public int SpawnIndex;
        public int CountIndex = -1;
        public OptionItem Spawn;
        public OptionItem Count;
        public PassiveButton Help;
        public PassiveButton Gear;
        public CategoryHeaderMasked Info;
        public float InfoHeight;
    }
    private class TableUI
    {
        public CategoryHeaderMasked Title;
        public CategoryHeaderMasked SubTitle;
        public SpriteRenderer Icon;
        public CategoryHeaderMasked[] Columns;
        public PassiveButton Back;
        public CategoryHeaderMasked BackTitle;
        public readonly List<RowUI> Rows = new();
        public PassiveButton[] SortButtons;
        public CategoryHeaderMasked[] SortLabels;
        public readonly Dictionary<int, RowUI> RowBySpawn = new();
    }
    private class Snapshot
    {
        public readonly List<(Transform t, Vector3 pos, Vector3 scale, bool active)> Children = new();
        public readonly List<(RectTransform rt, Vector2 size)> Sizes = new();
    }

    private static readonly Dictionary<TabGroup, TableUI> tables = new();
    private static readonly Dictionary<TabGroup, int> detailIndex = new();
    private static readonly HashSet<int> expanded = new();
    private static readonly Dictionary<int, Snapshot> snapshots = new();

    public static bool IsTableTab(TabGroup tab) => tab != TabGroup.ModMainSettings;

    public static void Clear()
    {
        tables.Clear();
        detailIndex.Clear();
        expanded.Clear();
        snapshots.Clear();
    }

    /******** 作成 ********/
    public static void Build(GameOptionsMenu menu, TabGroup tab)
    {
        try
        {
            BuildInternal(menu, tab);
        }
        catch (Exception ex)
        {
            // 失敗したら今までの一覧表示のまま使えるようにする
            tables.Remove(tab);
            Logger.Exception(ex, "RoleTableUI.Build");
        }
    }
    private static void BuildInternal(GameOptionsMenu menu, TabGroup tab)
    {
        tables.Remove(tab);
        detailIndex[tab] = -1;

        var optionIndex = new Dictionary<OptionItem, int>();
        for (int i = 0; i < OptionItem.AllOptions.Count; i++) optionIndex[OptionItem.AllOptions[i]] = i;

        var ui = new TableUI();
        PassiveButton template = null;
        foreach (var (role, spawn) in Options.CustomRoleSpawnChances)
        {
            if (spawn == null || spawn.Tab != tab) continue;
            if (!optionIndex.TryGetValue(spawn, out var spawnIndex)) continue;
            if (!ModGameOptionsMenu.BehaviourList.TryGetValue(spawnIndex, out var spawnBehaviour)) continue;

            template ??= spawnBehaviour.transform.Find("PlusButton")?.GetComponent<PassiveButton>();

            var row = new RowUI { Role = role, Reading = ReadingOf(role), SpawnIndex = spawnIndex, Spawn = spawn };
            TakeSnapshot(spawnIndex, spawnBehaviour);
            if (Options.CustomRoleCounts.TryGetValue(role, out var count) && optionIndex.TryGetValue(count, out var countIndex)
                && ModGameOptionsMenu.BehaviourList.TryGetValue(countIndex, out var countBehaviour))
            {
                row.Count = count;
                row.CountIndex = countIndex;
                TakeSnapshot(countIndex, countBehaviour);
            }
            ui.Rows.Add(row);
            ui.RowBySpawn[spawnIndex] = row;
        }
        if (template == null || ui.Rows.Count == 0) return;
        ui.Rows.Sort((a, b) => a.SpawnIndex.CompareTo(b.SpawnIndex));

        var container = menu.settingsContainer;
        var templateRenderer = template.GetComponentInChildren<SpriteRenderer>(true);

        // タイトル (アイコン + タブ名 + 説明)
        ui.Title = NewText(menu, ZeroSettingUI.GetTabName(tab), 1.1f, TextAlignmentOptions.Left);
        ui.SubTitle = NewText(menu, Translator.GetString($"MenuDescriptionText.{tab}"), 0.55f, TextAlignmentOptions.Left);
        var iconObj = new GameObject("TabIcon");
        iconObj.layer = container.gameObject.layer;
        iconObj.transform.SetParent(container, false);
        ui.Icon = iconObj.AddComponent<SpriteRenderer>();
        ui.Icon.sprite = ZeroSettingUI.LoadSprite($"Icon_{tab}.png", IconPpu);
        CopyRenderSettings(templateRenderer, ui.Icon);
        ui.Icon.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        iconObj.SetActive(false);

        // 列の見出し
        ui.Columns = new[]
        {
            NewText(menu, "役職名", 0.75f, TextAlignmentOptions.Center),
            NewText(menu, "出現確率", 0.75f, TextAlignmentOptions.Center),
            NewText(menu, "最大数", 0.75f, TextAlignmentOptions.Center),
        };

        // 各行の ？ / ⚙ と説明文
        foreach (var row in ui.Rows)
        {
            var r = row;
            row.Help = CloneButton(template, container, "Btn_Help.png", () => ShowRoleInfo(ui, r));
            row.Gear = CloneButton(template, container, "Btn_Gear.png", () => OpenDetail(menu, tab, r.SpawnIndex));
        }

        // 並び順の切り替え(MOD順 / あいうえお順)
        ui.SortButtons = new[]
        {
            CreateBoxButton(template, container, SortMode.Mod, () => SetSort(menu, SortMode.Mod)),
            CreateBoxButton(template, container, SortMode.Kana, () => SetSort(menu, SortMode.Kana)),
        };
        ui.SortLabels = new[]
        {
            NewText(menu, "MOD順", 0.6f, TextAlignmentOptions.Center),
            NewText(menu, "あいうえお順", 0.6f, TextAlignmentOptions.Center),
        };

        // 詳細表示の「戻る」
        ui.Back = CloneButton(template, container, "Btn_Back.png", () => CloseDetail(menu, tab));
        ui.BackTitle = NewText(menu, "", 0.8f, TextAlignmentOptions.Left);

        tables[tab] = ui;
    }

    /******** 並び順 ********/
    public enum SortMode { Mod, Kana }
    /// <summary>役職の並び順(全部の役職タブで共通)</summary>
    public static SortMode Sort = SortMode.Mod;
    private static readonly float[] SortButtonX = { 1.55f, 2.75f };
    private const float SortButtonWidth = 1.1f;
    private const float SortButtonHeight = 0.32f;
    private static readonly Color SortSelectedColor = new(0.2f, 0.45f, 0.85f, 1f);
    private static readonly Color SortNormalColor = new(0.16f, 0.2f, 0.3f, 1f);
    private static readonly Color SortHoverColor = new(0.26f, 0.34f, 0.5f, 1f);
    private static readonly Color SortSelectedHoverColor = new(0.3f, 0.55f, 0.95f, 1f);

    /// <summary>ボタンの色: 選んでいる方は青、マウスを乗せている時は少し明るく(白くはしない)</summary>
    private static void PaintSortButton(PassiveButton button, SortMode mode, bool hovered)
    {
        if (button == null) return;
        var selected = Sort == mode;
        var color = selected ? (hovered ? SortSelectedHoverColor : SortSelectedColor) : (hovered ? SortHoverColor : SortNormalColor);
        foreach (var r in button.GetComponentsInChildren<SpriteRenderer>(true)) r.color = color;
    }
    private static Sprite boxSprite;

    private static void SetSort(GameOptionsMenu menu, SortMode mode)
    {
        if (Sort == mode) return;
        Sort = mode;
        foreach (var tab in tables.Keys.ToArray())
            if (ModGameOptionsMenu.SettingsMenus.TryGetValue(tab, out var m) && m != null) Layout(m, tab, true);
    }

    /// <summary>四角いボタン(文字は別に重ねる)</summary>
    private static PassiveButton CreateBoxButton(PassiveButton template, Transform parent, SortMode mode, Action onClick)
    {
        if (boxSprite == null)
        {
            var tex = new Texture2D(4, 4);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            boxSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            boxSprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
        }
        var button = Object.Instantiate(template, parent);
        button.name = "Zero_SortButton";
        button.transform.localScale = Vector3.one;
        // 元のボタンのマウスを乗せた時の色替え(真っ白に光る・離しても戻らない)を外して、色は自分で付ける
        foreach (var handler in button.GetComponentsInChildren<ButtonRolloverHandler>(true)) Object.Destroy(handler);
        button.OnMouseOver = new();
        button.OnMouseOut = new();
        button.OnMouseOver.AddListener((Action)(() => PaintSortButton(button, mode, hovered: true)));
        button.OnMouseOut.AddListener((Action)(() => PaintSortButton(button, mode, hovered: false)));
        foreach (var tmp in button.GetComponentsInChildren<TextMeshPro>(true)) tmp.enabled = false;
        foreach (var renderer in button.GetComponentsInChildren<SpriteRenderer>(true))
        {
            renderer.sprite = boxSprite;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.transform.localScale = new Vector3(SortButtonWidth, SortButtonHeight, 1f);
        }
        var collider = button.GetComponent<BoxCollider2D>();
        if (collider != null)
        {
            collider.size = new Vector2(SortButtonWidth, SortButtonHeight);
            collider.offset = Vector2.zero;
        }
        button.OnClick = new();
        button.OnClick.AddListener(onClick);
        button.gameObject.SetActive(false);
        return button;
    }

    // 漢字を含む役職名の読み
    private static readonly Dictionary<string, string> KanjiReadings = new()
    {
        ["宿敵"] = "しゅくてき", ["パン屋"] = "ぱんや", ["探偵"] = "たんてい", ["決闘者"] = "けっとうしゃ",
        ["花火職人"] = "はなびしょくにん", ["占い師"] = "うらないし", ["妖狐"] = "ようこ", ["一味"] = "いちみ",
        ["神"] = "かみ", ["守護天使"] = "しゅごてんし", ["背徳者"] = "はいとくしゃ", ["白狐"] = "びゃっこ",
        ["マッド科学者"] = "まっどかがくしゃ", ["猫又"] = "ねこまた", ["通常インポスター"] = "つうじょういんぽすたー",
        ["科学者"] = "かがくしゃ", ["鬼"] = "おに", ["海賊"] = "かいぞく", ["純愛者"] = "じゅんあいしゃ",
        ["霊媒師"] = "れいばいし", ["シュレディンガーの猫"] = "しゅれでぃんがーのねこ", ["自爆魔"] = "じばくま",
        ["共鳴者"] = "きょうめいしゃ",
    };

    /// <summary>並べるための読み(ひらがな)。カタカナはひらがなにし、記号は除く</summary>
    private static string ReadingOf(CustomRoles role)
    {
        var name = Utils.GetRoleName(role) ?? role.ToString();
        if (KanjiReadings.TryGetValue(name, out var reading)) return reading;
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (c >= '\u30A1' && c <= '\u30F6') sb.Append((char)(c - 0x60)); // カタカナ → ひらがな
            else if (c == 'ー' || (c >= '\u3041' && c <= '\u3096') || char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : name;
    }

    /******** 配置 ********/
    /// <summary>表形式で並べ直す。表形式でないタブなら false</summary>
    public static bool Layout(GameOptionsMenu menu, TabGroup tab, bool updateNavigation)
    {
        if (!tables.TryGetValue(tab, out var ui)) return false;
        try
        {
            LayoutInternal(menu, tab, ui);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "RoleTableUI.Layout");
            return false;
        }

        if (updateNavigation)
        {
            menu.ControllerSelectable.Clear();
            foreach (var x in menu.scrollBar.GetComponentsInChildren<UiElement>())
                menu.ControllerSelectable.Add(x);
        }
        return true;
    }
    private static void LayoutInternal(GameOptionsMenu menu, TabGroup tab, TableUI ui)
    {
        var container = menu.settingsContainer;
        var mode = Options.CurrentGameMode;

        // いったん全部隠す
        for (int index = 0; index < OptionItem.AllOptions.Count; index++)
        {
            if (OptionItem.AllOptions[index].Tab != tab) continue;
            if (ModGameOptionsMenu.CategoryHeaderList.TryGetValue(index, out var header)) header.gameObject.SetActive(false);
            if (ModGameOptionsMenu.BehaviourList.TryGetValue(index, out var behaviour))
            {
                RestoreSnapshot(index, behaviour);
                behaviour.gameObject.SetActive(false);
            }
        }
        SetActive(ui.Title, false); SetActive(ui.SubTitle, false); ui.Icon.gameObject.SetActive(false);
        foreach (var c in ui.Columns) SetActive(c, false);
        foreach (var row in ui.Rows)
        {
            row.Help.gameObject.SetActive(false);
            row.Gear.gameObject.SetActive(false);
            SetActive(row.Info, false);
        }
        ui.Back.gameObject.SetActive(false);
        SetActive(ui.BackTitle, false);
        foreach (var b in ui.SortButtons) b.gameObject.SetActive(false);
        foreach (var l in ui.SortLabels) SetActive(l, false);

        float num = StartY;
        int detail = detailIndex.TryGetValue(tab, out var d) ? d : -1;

        if (detail < 0)
        {
            /**** 一覧(表) ****/
            ui.Icon.transform.localPosition = new Vector3(NameLeft + 0.3f, num - 0.2f, -2f);
            ui.Icon.gameObject.SetActive(true);
            Place(ui.Title, container, NameLeft + 0.7f, num - 0.05f, center: false);
            Place(ui.SubTitle, container, NameLeft + 0.7f, num - 0.42f, center: false);
            num -= TitleBlockHeight;

            Place(ui.Columns[0], container, NameLeft + NameWidth / 2f, num, center: true);
            Place(ui.Columns[1], container, PercentCenter, num, center: true);
            Place(ui.Columns[2], container, MaxCenter, num, center: true);
            num -= ColumnBlockHeight;

            // 並び順の切り替えボタン(タイトルの右)
            for (int k = 0; k < ui.SortButtons.Length; k++)
            {
                var selected = (int)Sort == k;
                var x = SortButtonX[k];
                var button = ui.SortButtons[k];
                button.transform.localPosition = new Vector3(x, StartY - 0.1f, -3f);
                PaintSortButton(button, (SortMode)k, hovered: false);
                button.gameObject.SetActive(true);
                Place(ui.SortLabels[k], container, x, StartY - 0.1f, center: true);
                ui.SortLabels[k].transform.localPosition += new Vector3(0f, 0f, -1.5f);
                ui.SortLabels[k].Title.color = selected ? Color.white : new Color(0.75f, 0.8f, 0.9f);
            }

            void PlaceRow(RowUI row)
            {
                if (row.Spawn.IsHiddenOn(mode)) return;
                if (!ModGameOptionsMenu.BehaviourList.TryGetValue(row.SpawnIndex, out var spawnBehaviour)) return;

                spawnBehaviour.transform.localPosition = new Vector3(OptionX, num, -2f);
                spawnBehaviour.gameObject.SetActive(true);
                FitNameColumn(spawnBehaviour);
                MoveValueCluster(spawnBehaviour, PercentCenter, PercentSpan, PercentBoxWidth);

                if (row.Count != null && ModGameOptionsMenu.BehaviourList.TryGetValue(row.CountIndex, out var countBehaviour)
                    && row.Spawn.GetInt() > 0 && !row.Count.IsHiddenOn(mode))
                {
                    countBehaviour.transform.localPosition = new Vector3(OptionX, num, -2.1f);
                    countBehaviour.gameObject.SetActive(true);
                    countBehaviour.transform.Find("LabelBackground")?.gameObject.SetActive(false);
                    countBehaviour.transform.Find("Title Text")?.gameObject.SetActive(false);
                    MoveValueCluster(countBehaviour, MaxCenter, MaxSpan, MaxBoxWidth);
                }

                row.Help.transform.localPosition = new Vector3(HelpX, num - 0.05f, -3f);
                row.Gear.transform.localPosition = new Vector3(GearX, num - 0.05f, -3f);
                row.Help.gameObject.SetActive(true);
                row.Gear.gameObject.SetActive(true);
                num -= RowHeight;
            }

            if (Sort == SortMode.Kana)
            {
                // あいうえお順: 区切りの見出しは出さずに、役職名の読みで並べる
                foreach (var row in ui.Rows.OrderBy(r => r.Reading, StringComparer.Ordinal).ThenBy(r => r.SpawnIndex))
                    PlaceRow(row);
            }
            else
            {
                // MOD順: 今まで通り、MOD の中の順番で区切りの見出しを付けて並べる
                for (int index = 0; index < OptionItem.AllOptions.Count; index++)
                {
                    var option = OptionItem.AllOptions[index];
                    if (option.Tab != tab) continue;

                    if (option is TextOptionItem && !option.IsHiddenOn(mode)
                        && ModGameOptionsMenu.CategoryHeaderList.TryGetValue(index, out var section))
                    {
                        section.transform.localPosition = new Vector3(HeaderX, num, -2f);
                        section.gameObject.SetActive(true);
                        num -= SectionHeight;
                    }

                    if (ui.RowBySpawn.TryGetValue(index, out var row)) PlaceRow(row);
                }
            }
        }
        else
        {
            /**** ⚙ で開いた役職の詳細 ****/
            var root = OptionItem.AllOptions[detail];
            ui.Back.transform.localPosition = new Vector3(NameLeft + 0.25f, num - 0.05f, -3f);
            ui.Back.gameObject.SetActive(true);
            ui.BackTitle.Title.text = $"{root.GetName()} の詳細設定";
            Place(ui.BackTitle, container, NameLeft + 0.6f, num - 0.05f, center: false);
            num -= 0.7f;

            for (int index = 0; index < OptionItem.AllOptions.Count; index++)
            {
                var option = OptionItem.AllOptions[index];
                if (option.Tab != tab || !IsInSubtree(option, root)) continue;
                var enabled = GameOptionsMenuPatch.IsOptionEnabled(option);

                if (ModGameOptionsMenu.CategoryHeaderList.TryGetValue(index, out var header))
                {
                    header.transform.localPosition = new Vector3(HeaderX, num, -2f);
                    header.gameObject.SetActive(enabled);
                    if (enabled) num -= SectionHeight;
                }
                if (ModGameOptionsMenu.BehaviourList.TryGetValue(index, out var behaviour))
                {
                    behaviour.transform.localPosition = new Vector3(OptionX, num, -2f);
                    behaviour.gameObject.SetActive(enabled);
                    if (enabled) num -= RowHeight;
                }
            }
        }

        menu.scrollBar.SetYBoundsMax(-num - 1.65f);
    }

    /******** ボタン操作 ********/
    private static readonly Color HelpSelectedColor = new(0.45f, 1f, 1f, 1f);
    /// <summary>？ : 右側の説明欄に役職の詳しい説明を出す</summary>
    private static void ShowRoleInfo(TableUI ui, RowUI row)
    {
        var menu = Object.FindObjectOfType<GameSettingMenu>();
        if (menu == null || menu.MenuDescriptionText == null) return;

        string text = Enum.TryParse(typeof(CustomRoles), row.Spawn.Name, true, out var role)
            ? Utils.GetRoleInfoLongWithColor((CustomRoles)role)
            : $"<b>{row.Spawn.GetName(colorLighter: true)}</b>\n{row.Spawn.IsHeaderName}";
        menu.MenuDescriptionText.DestroyTranslator();
        menu.MenuDescriptionText.text = text;

        // 押した ？ だけ色を変える
        foreach (var r in ui.Rows)
            foreach (var sr in r.Help.GetComponentsInChildren<SpriteRenderer>(true))
                sr.color = r == row ? HelpSelectedColor : Color.white;
    }
    private static void OpenDetail(GameOptionsMenu menu, TabGroup tab, int spawnIndex)
    {
        detailIndex[tab] = spawnIndex;
        Layout(menu, tab, true);
        try { menu.scrollBar.ScrollToTop(); } catch { }
    }
    private static void CloseDetail(GameOptionsMenu menu, TabGroup tab)
    {
        detailIndex[tab] = -1;
        Layout(menu, tab, true);
        try { menu.scrollBar.ScrollToTop(); } catch { }
    }

    /******** 部品 ********/
    private static bool IsInSubtree(OptionItem option, OptionItem root)
    {
        for (var o = option; o != null; o = o.Parent)
            if (o == root) return true;
        return false;
    }

    private static CategoryHeaderMasked NewText(GameOptionsMenu menu, string text, float sizeRate, TextAlignmentOptions alignment)
    {
        var header = Object.Instantiate(menu.categoryHeaderOrigin, Vector3.zero, Quaternion.identity, menu.settingsContainer);
        header.SetHeader(StringNames.RolesCategory, 20);
        header.transform.localScale = Vector3.one * 0.63f;
        header.transform.Find("LabelSprite")?.gameObject.SetActive(false);
        header.transform.Find("DividerImage")?.gameObject.SetActive(false);

        var tmp = header.Title;
        tmp.DestroyTranslator();
        tmp.enableAutoSizing = false;
        tmp.fontSize = tmp.fontSizeMin = tmp.fontSizeMax = tmp.fontSize * sizeRate;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = false;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.text = text;
        header.gameObject.SetActive(false);
        return header;
    }
    /// <summary>折り返しありの文章にして、必要な高さ(設定一覧の座標)を返す</summary>
    private static float SetupWrappedText(CategoryHeaderMasked header, float width)
    {
        var tmp = header.Title;
        var rt = tmp.rectTransform;
        var container = header.transform.parent;
        float unit = Mathf.Abs(container.InverseTransformVector(rt.TransformVector(Vector3.right)).x);
        if (unit <= 0.0001f) unit = 0.63f;

        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.fontStyle = FontStyles.Normal;
        tmp.color = new Color(0.85f, 0.9f, 1f);
        float localWidth = width / unit;

        // 一時的に表示して高さを測る
        bool wasActive = header.gameObject.activeSelf;
        header.gameObject.SetActive(true);
        rt.sizeDelta = new Vector2(localWidth, 10f / unit);
        tmp.ForceMeshUpdate();
        float height = tmp.GetPreferredValues(tmp.text, localWidth, 0f).y * unit;
        header.gameObject.SetActive(wasActive);

        if (height <= 0.05f || float.IsNaN(height))
        {
            int lines = 0;
            foreach (var line in tmp.text.Split('\n'))
                lines += Mathf.Max(1, Mathf.CeilToInt(line.Length / (float)InfoCharsPerLine));
            height = lines * InfoLineHeight;
        }
        rt.sizeDelta = new Vector2(localWidth, height / unit + 0.1f / unit);
        return Mathf.Max(0.4f, height + 0.2f);
    }
    /// <summary>文字の位置を、設定一覧の座標で指定する(center=false なら左端)</summary>
    private static void Place(CategoryHeaderMasked header, Transform container, float x, float y, bool center)
    {
        header.transform.localPosition = new Vector3(0f, 0f, -2f);
        var rt = header.Title.rectTransform;
        var rect = rt.rect;
        var point = center ? new Vector3(rect.center.x, rect.center.y, 0f) : new Vector3(rect.xMin, rect.center.y, 0f);
        var offset = container.InverseTransformPoint(rt.TransformPoint(point));
        header.transform.localPosition = new Vector3(x - offset.x, y - offset.y, -2f);
        header.gameObject.SetActive(true);
    }
    /// <summary>文章の左上を、設定一覧の座標で指定する</summary>
    private static void PlaceTopLeft(CategoryHeaderMasked header, Transform container, float x, float y)
    {
        header.transform.localPosition = new Vector3(0f, 0f, -2f);
        var rt = header.Title.rectTransform;
        var rect = rt.rect;
        var offset = container.InverseTransformPoint(rt.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f)));
        header.transform.localPosition = new Vector3(x - offset.x, y - offset.y, -2f);
        header.gameObject.SetActive(true);
    }
    private static void SetActive(CategoryHeaderMasked header, bool active)
    {
        if (header != null) header.gameObject.SetActive(active);
    }

    private static PassiveButton CloneButton(PassiveButton template, Transform parent, string spriteFile, Action onClick)
    {
        var button = Object.Instantiate(template, parent);
        button.name = $"Zero_{spriteFile}";
        button.transform.localScale = Vector3.one;
        foreach (var tmp in button.GetComponentsInChildren<TextMeshPro>(true)) tmp.enabled = false;
        var sprite = ZeroSettingUI.LoadSprite(spriteFile, SmallButtonPpu);
        foreach (var renderer in button.GetComponentsInChildren<SpriteRenderer>(true))
        {
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.color = Color.white;
            renderer.transform.localScale = Vector3.one;
        }
        var collider = button.GetComponent<BoxCollider2D>();
        if (collider != null)
        {
            collider.size = new Vector2(SmallButtonCollider, SmallButtonCollider);
            collider.offset = Vector2.zero;
        }
        button.OnClick = new();
        button.OnClick.AddListener(onClick);
        button.gameObject.SetActive(false);
        return button;
    }
    private static void CopyRenderSettings(SpriteRenderer from, SpriteRenderer to)
    {
        if (from == null) return;
        to.sortingLayerID = from.sortingLayerID;
        to.sortingOrder = from.sortingOrder;
        to.maskInteraction = from.maskInteraction;
    }

    // 設定一覧の座標 → 行(OptionBehaviour)の中の座標
    private static float ToLocalX(Transform row, float containerX)
        => row.InverseTransformPoint(row.parent.TransformPoint(new Vector3(containerX, 0f, 0f))).x;
    private static float ToLocalLength(Transform target, float containerLength)
    {
        var container = target.parent;
        while (container != null && container.GetComponent<OptionBehaviour>() != null) container = container.parent;
        float worldPerContainer = container != null ? container.TransformVector(Vector3.right).magnitude : 1f;
        float worldPerLocal = target.TransformVector(Vector3.right).magnitude;
        return worldPerLocal <= 0.0001f ? containerLength : containerLength * worldPerContainer / worldPerLocal;
    }

    /// <summary>役職名の列の幅に合わせる</summary>
    private static void FitNameColumn(OptionBehaviour behaviour)
    {
        var t = behaviour.transform;
        float centerLocal = ToLocalX(t, NameLeft + NameWidth / 2f);
        var background = t.Find("LabelBackground");
        var renderer = background?.GetComponent<SpriteRenderer>();
        if (renderer?.sprite != null && renderer.sprite.bounds.size.x > 0f)
        {
            // 背景の幅: 親(行)の座標で NameWidth になる拡大率
            float widthInRow = ToLocalLength(t, NameWidth);
            background.localScale = new Vector3(widthInRow / renderer.sprite.bounds.size.x, background.localScale.y, background.localScale.z);
            background.localPosition = new Vector3(centerLocal, background.localPosition.y, background.localPosition.z);
        }
        var title = t.Find("Title Text");
        if (title != null)
        {
            var rt = title.GetComponent<RectTransform>();
            float widthInTitle = ToLocalLength(title, NameWidth - 0.15f);
            rt.sizeDelta = new Vector2(widthInTitle, rt.sizeDelta.y);
            title.localPosition = new Vector3(centerLocal + ToLocalLength(t, 0.05f), title.localPosition.y, title.localPosition.z);
        }
    }
    /// <summary>－ 値 ＋ のまとまりを、指定した列の中心に移動する</summary>
    private static void MoveValueCluster(OptionBehaviour behaviour, float centerX, float span, float boxWidth)
    {
        var t = behaviour.transform;
        var minus = t.Find("MinusButton");
        var plus = t.Find("PlusButton");
        if (minus == null || plus == null) return;
        var box = t.Find("ValueBox");
        var value = t.Find("Value_TMP");

        float current = (minus.localPosition.x + plus.localPosition.x) / 2f;
        float target = ToLocalX(t, centerX);
        float half = ToLocalLength(t, span) / 2f;

        minus.localPosition = new Vector3(target - half, minus.localPosition.y, minus.localPosition.z);
        plus.localPosition = new Vector3(target + half, plus.localPosition.y, plus.localPosition.z);
        foreach (var part in new[] { box, value })
        {
            if (part == null) continue;
            var p = part.localPosition;
            part.localPosition = new Vector3(target, p.y, p.z);
        }
        FitWidth(box, boxWidth);
    }
    /// <summary>見た目の幅を、設定一覧の座標で指定した幅にする</summary>
    private static void FitWidth(Transform target, float containerWidth)
    {
        if (target == null) return;
        var renderer = target.GetComponent<SpriteRenderer>() ?? target.GetComponentInChildren<SpriteRenderer>();
        if (renderer == null) return;
        float current = renderer.bounds.size.x;
        if (current <= 0.0001f) return;

        var container = target.parent;
        while (container != null && container.GetComponent<OptionBehaviour>() != null) container = container.parent;
        float desired = containerWidth * (container != null ? container.TransformVector(Vector3.right).magnitude : 1f);
        var s = target.localScale;
        target.localScale = new Vector3(s.x * desired / current, s.y, s.z);
    }

    private static void TakeSnapshot(int index, OptionBehaviour behaviour)
    {
        var snapshot = new Snapshot();
        var t = behaviour.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            snapshot.Children.Add((child, child.localPosition, child.localScale, child.gameObject.activeSelf));
            var rt = child.GetComponent<RectTransform>();
            if (rt != null) snapshot.Sizes.Add((rt, rt.sizeDelta));
        }
        snapshots[index] = snapshot;
    }
    private static void RestoreSnapshot(int index, OptionBehaviour behaviour)
    {
        if (!snapshots.TryGetValue(index, out var snapshot)) return;
        foreach (var (t, pos, scale, active) in snapshot.Children)
        {
            if (t == null) continue;
            t.localPosition = pos;
            t.localScale = scale;
            t.gameObject.SetActive(active);
        }
        foreach (var (rt, size) in snapshot.Sizes)
            if (rt != null) rt.sizeDelta = size;
    }
}
