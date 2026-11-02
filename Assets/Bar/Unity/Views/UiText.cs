using System.Collections.Generic;
using UnityEngine;

namespace Bar.Unity
{
    public enum UiLanguage { Chinese, English, Japanese }
    /// <summary>Presentation-only language preference. Domain names, operation IDs and research data stay unchanged.</summary>
    public static partial class UiText
    {
        public const string PreferenceKey = "Bar.UI.Language";
        public static UiLanguage Language { get; private set; }
        public static bool English => Language == UiLanguage.English;
        public static int Revision { get; private set; }
        private static Font japaneseFont;
        public static Font DisplayFont(Font original) => Language == UiLanguage.Japanese ? japaneseFont : original;
        public static string T(string chinese, string english) => Language switch
        {
            UiLanguage.Chinese => chinese, UiLanguage.English => english,
            UiLanguage.Japanese => JapaneseText[chinese],
            _ => throw new System.ArgumentOutOfRangeException(nameof(Language))
        };
        public static void Load()
        {
            japaneseFont = Resources.Load<Font>("BarUISubsetSC");
            if (!japaneseFont) throw new System.InvalidOperationException("Missing bundled UI font");
            var language = PlayerPrefs.GetString(PreferenceKey, "zh") switch
            {
                "zh" => UiLanguage.Chinese, "en" => UiLanguage.English, "ja" => UiLanguage.Japanese,
                _ => throw new System.InvalidOperationException("Unsupported stored UI language")
            };
            if (Language != language) { Language = language; Revision++; }
        }
        public static void SetLanguage(UiLanguage language)
        {
            string code = language switch
            {
                UiLanguage.Chinese => "zh", UiLanguage.English => "en", UiLanguage.Japanese => "ja",
                _ => throw new System.ArgumentOutOfRangeException(nameof(language))
            };
            if (Language != language) { Language = language; Revision++; }
            PlayerPrefs.SetString(PreferenceKey, code); PlayerPrefs.Save();
        }
        public static void SetEnglish(bool value) => SetLanguage(value ? UiLanguage.English : UiLanguage.Chinese);
        private static readonly Dictionary<string, string> EnglishText = new Dictionary<string, string>
        {
            {"今夜的点单", "Tonight's order"}, {"碟形鸡尾酒杯", "Coupe glass"}, {"高球杯", "Highball glass"},
            {"想要一杯冰凉、酸甜平衡，仍有酒体的酸酒。", "A chilled Sour, balanced between sweet and tart, with a clear spirit backbone."},
            {"想要清爽冰凉的金汤力，气泡要留得住。", "A cool, refreshing Gin & Tonic. Keep those bubbles alive."},
            {"来一杯清爽的柠檬金酒，酸甜平衡，带些气泡。", "A refreshing lemon-and-gin drink: balanced sweet and tart, with some fizz."},
            {"今夜，收工", "That's a wrap"}, {"来自客人的话", "A word from your guest"},
            {"三杯已上齐。谢谢你今夜的招待。", "All three drinks served. Thank you for tonight's hospitality."},
            {"再做一次", "Try again"}, {"下一单", "Next order"}, {"正合我意，谢谢。", "Just what I wanted. Thank you."},
            {"酒味有点淡。", "A little light on the spirit."}, {"喝不出酒味。", "I can barely taste the spirit."},
            {"有点冲。", "A little strong."}, {"酒味太重了。", "The spirit is overpowering."},
            {"这杯的风味有些失衡。", "The balance feels off."}, {"有点酸。", "A little tart."}, {"太酸了。", "Too sour."},
            {"偏甜。", "On the sweet side."}, {"太甜了。", "Too sweet."}, {"有点太冰了。", "A little too cold."},
            {"冰得过头了。", "Far too cold."}, {"还不够冰。", "Not quite cold enough."}, {"这杯太温了。", "Too warm."},
            {"味道有点浓。", "A little concentrated."}, {"味道太浓了。", "Too concentrated."},
            {"有点水。", "A little watery."}, {"这杯太水了。", "Too watery."},
            {"气泡不太够。", "Could use a little more fizz."}, {"没什么气了。", "Almost flat."},
            {"气泡有点多。", "A little too fizzy."}, {"气泡太强了。", "Too fizzy."}, {"这杯子不对。", "That's the wrong glass."},
            {"金酒瓶", "Gin bottle"}, {"金酒瓶盖", "Gin cap"}, {"糖浆瓶", "Syrup bottle"}, {"糖浆瓶盖", "Syrup cap"},
            {"苏打水瓶", "Soda bottle"}, {"苏打水瓶盖", "Soda cap"}, {"汤力水瓶", "Tonic bottle"}, {"汤力水瓶盖", "Tonic cap"},
            {"雪克壶壶身", "Shaker body"}, {"三段式雪克壶", "Cobbler shaker"}, {"雪克壶", "Shaker"},
            {"隔冰器", "Strainer"}, {"雪克壶上盖", "Shaker cap"}, {"量酒器30ml端", "Jigger 30 ml"}, {"量酒器45ml端", "Jigger 45 ml"},
            {"双头量酒器", "Double jigger"}, {"量酒器", "Jigger"}, {"Coupe杯", "Coupe glass"}, {"Highball杯", "Highball glass"},
            {"吧勺", "Barspoon"}, {"冰铲", "Ice scoop"}, {"冰夹", "Ice tongs"}, {"手压榨汁器", "Lemon squeezer"},
            {"半颗柠檬", "Lemon half"}, {"冰桶", "Ice bin"}, {"水槽", "Sink"}, {"出杯垫", "Serving mat"}, {"砧板", "Cutting board"}, {"吧台", "Counter"}, {"工具", "Tool"},
            {"沿吧勺引流", "Barspoon guided pour"}, {"擦干壶身", "Dry shaker"}, {"盖上滤冰器", "Attach strainer"},
            {"取下滤冰器", "Remove strainer"}, {"盖上壶盖", "Attach cap"}, {"取下壶盖", "Remove cap"},
            {"翻转量酒器 (30ml ⇄ 45ml)", "Flip jigger (30ml ⇄ 45ml)"}, {"倒入水槽", "Empty into sink"},
            {"取冰", "Scoop ice"}, {"放入冰块", "Add ice"}, {"装入柠檬", "Load lemon"}, {"压汁", "Squeeze"},
            {"双手摇壶", "Shake with both hands"}, {"搅拌", "Stir"}, {"壶倒出", "Pour from shaker"},
            {"倒掉冰和液体", "Discard ice and liquid"}, {"丢弃用完的柠檬", "Discard spent lemon"}, {"放到出杯垫", "Serve on mat"},
            {"倒掉工具上的冰", "Discard tool ice"}, {"翻转量酒器", "Flip jigger"}, {"取下瓶盖", "Uncap bottle"},
            {"盖上瓶盖", "Cap bottle"}, {"拿起", "Pick up"}, {"放下", "Put down"}, {"装上上盖", "Attach cap"},
            {"取下上盖", "Remove cap"}, {"装上隔冰器", "Attach strainer"}, {"取下隔冰器", "Remove strainer"},
            {"倒入", "Pour"}, {"量酒器倒出", "Pour from jigger"}, {"擦干", "Dry"},
            {"[右键] 拿起", "[RMB] Pick up"}, {"[右键] 放置", "[RMB] Place"},
            {"双手已满或当前物品不能拿起；对准空吧台右键放下主手物品", "Hands full or item unavailable. Aim at clear counter space and right-click to place the main-hand item."},
            {"动作进行中不能切换左右手", "Finish the current action before changing handedness."},
            {"请对准吧台上表面的空位置", "Aim at a clear spot on top of the counter."},
            {"这里已有物品，请选择空位置", "That spot is occupied. Choose a clear space."},
            {"这里是水槽、冰槽开口或台面边缘，请选择完整的空台面", "Choose a solid, clear part of the countertop, away from openings and edges."},
            {"量酒器倒空后才能把玩", "Empty the jigger before inspecting it."},
            {"点击恢复", "Click to resume"}, {"[F] 检视", "[F] Inspect"},
            {"调酒工作台 · 点击开始以启用声音", "Cocktail workbench · Click to enable audio"}, {"点击开始", "Click to start"}
        };
        public static string Translate(string text)
        {
            if (Language == UiLanguage.Chinese || string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf('\n') >= 0) return string.Join("\n", System.Array.ConvertAll(text.Split('\n'), Translate));
            var dictionary = Language == UiLanguage.Japanese ? JapaneseText : EnglishText;
            if (dictionary.TryGetValue(text, out var translated)) return translated;
            // Composed display messages, not domain/action identifiers.
            if (text.EndsWith("空着才能把玩")) return Language == UiLanguage.Japanese
                ? Translate(text.Substring(0, text.Length - 6)) + "を空にしてからインスペクトしてください。"
                : "Empty " + Translate(text.Substring(0, text.Length - 6)) + " before inspecting it.";
            if (text.StartsWith("动作条件已变化：")) return (Language == UiLanguage.Japanese ? "動作条件が変わりました：" : "Action no longer available: ") + Translate(text.Substring(8));
            return text; // Proper names and technical diagnostics remain verbatim.
        }
    }
}
