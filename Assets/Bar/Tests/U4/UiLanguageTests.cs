using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
namespace Bar.Unity.Tests
{
    public sealed class UiLanguageTests
    {
        private bool existed; private string stored;
        [SetUp] public void SavePreference()
        {
            existed = PlayerPrefs.HasKey(UiText.PreferenceKey);
            stored = PlayerPrefs.GetString(UiText.PreferenceKey);
            UiText.Load();
        }
        [TearDown] public void RestorePreference()
        {
            if (existed) PlayerPrefs.SetString(UiText.PreferenceKey, stored);
            else PlayerPrefs.DeleteKey(UiText.PreferenceKey);
            PlayerPrefs.Save(); UiText.Load();
        }
        [TestCase(UiLanguage.Chinese, "zh", "设置")]
        [TestCase(UiLanguage.English, "en", "Settings")]
        [TestCase(UiLanguage.Japanese, "ja", "設定")]
        public void SelectionPersistsAndRefreshesCachedLabels(UiLanguage language, string code, string label)
        {
            UiText.SetLanguage(language == UiLanguage.Chinese ? UiLanguage.English : UiLanguage.Chinese);
            int revision = UiText.Revision; UiText.SetLanguage(language);
            Assert.That(UiText.Revision, Is.GreaterThan(revision));
            Assert.That(UiText.T("设置", "Settings"), Is.EqualTo(label));
            Assert.That(PlayerPrefs.GetString(UiText.PreferenceKey), Is.EqualTo(code));
            UiText.Load(); Assert.That(UiText.Language, Is.EqualTo(language));
            Assert.That(UiText.T("设置", "Settings"), Is.EqualTo(label));
        }
        [Test] public void FeedbackLinesTranslateWhileNamesAndIdsStayVerbatim()
        {
            UiText.SetLanguage(UiLanguage.Japanese);
            Assert.That(UiText.Translate("太酸了。\n太甜了。"), Is.EqualTo("酸っぱすぎます。\n甘すぎます。"));
            foreach (string name in new[] { "Gin Sour", "Gin & Tonic", "vessel:item:shaker.cobbler/body", "co2GL" })
                Assert.That(UiText.Translate(name), Is.EqualTo(name));
            Assert.That(UiText.Translate("雪克壶空着才能把玩"), Is.EqualTo("シェーカーを空にしてからインスペクトしてください。"));
            Assert.Throws<KeyNotFoundException>(() => UiText.T("missing internal key", "Missing"));
        }
        [Test] public void BundledJapaneseFontRendersSettingsAndInspector()
        {
            UiText.SetLanguage(UiLanguage.Japanese); var font = UiText.DisplayFont(null);
            Assert.That(font, Is.Not.Null);
            string text = "日本語設定シェーカーひらがなシステム状態読み取り専用";
            foreach (char c in text) Assert.That(font.HasCharacter(c), Is.True, "Missing glyph: " + c);
            var label = new GUIStyle { font = font, fontSize = 16, wordWrap = true };
            Assert.That(label.CalcHeight(new GUIContent(UiText.Translate("调酒工作台 · 点击开始以启用声音")), 360), Is.LessThanOrEqualTo(40), "Startup prompt must fit its fixed label");
            Assert.That(UiText.T("水 / 溶解 CO₂", "Water / Dissolved CO₂"), Is.EqualTo("水 / 溶存 CO2"));
            Assert.That(UiText.T("溶液冰点", "Freezing point"), Is.EqualTo("溶液の凝固点"));
        }
    }
}
