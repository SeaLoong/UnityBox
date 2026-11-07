using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityBox.AvatarSecuritySystem.Editor;

namespace UnityBox.AvatarSecuritySystem.Tests
{
    public sealed class LocalizationTests
    {
        private SystemLanguage previousLanguage;

        [SetUp]
        public void RememberLanguage() => previousLanguage = I18n.GetCurrentLanguage();

        [TearDown]
        public void RestoreLanguage() => I18n.SetLanguage(previousLanguage);

        [Test]
        public void EveryRegisteredMessageHasTextInAllSupportedLanguages()
        {
            Assert.That(I18n.SupportedLanguages, Does.Contain(SystemLanguage.ChineseTraditional));
            foreach (string key in I18n.Keys)
            foreach (SystemLanguage language in I18n.SupportedLanguages)
            {
                string value = I18n.Translate(key, language);
                Assert.That(string.IsNullOrWhiteSpace(value), Is.False, key + " / " + language);
                Assert.That(value, Does.Not.Contain("[Missing:"), key + " / " + language);
                Assert.That(value, Does.Not.Contain("#{"), key + " / " + language);
            }
        }

        [Test]
        public void InspectorLabelsAndActualRuntimeTooltipTokensAllResolve()
        {
            string source = InspectorSource();
            var keys = new HashSet<string>();
            foreach (Match match in Regex.Matches(source, @"\b(?:T|Content)\(""(?<key>[A-Za-z0-9_.]+)""\s*[,)]"))
                keys.Add(match.Groups["key"].Value);
            foreach (Match match in Regex.Matches(source, "DrawProperty\\(\"[^\"]+\",\\s*\"(?<key>[A-Za-z0-9_.]+)\""))
                keys.Add(match.Groups["key"].Value);
            foreach (var field in typeof(ASSComponent).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            foreach (TooltipAttribute tooltip in field.GetCustomAttributes(typeof(TooltipAttribute), true))
            foreach (Match match in Regex.Matches(tooltip.tooltip, @"#\{(?<key>[A-Za-z0-9_.]+)\}"))
                keys.Add(match.Groups["key"].Value);
            Assert.That(keys.Count, Is.GreaterThan(25));
            foreach (string key in keys)
            foreach (SystemLanguage language in I18n.SupportedLanguages)
                Assert.That(I18n.Translate(key, language), Does.Not.StartWith("[Missing:"), key + " / " + language);
        }

        [Test]
        public void TraditionalChineseUsesItsOwnTextAndGenericChineseUsesSimplified()
        {
            Assert.That(I18n.Translate("language.title", SystemLanguage.ChineseTraditional), Is.EqualTo("語言"));
            Assert.That(I18n.Translate("language.title", SystemLanguage.ChineseSimplified), Is.EqualTo("语言"));
            Assert.That(I18n.Translate("language.title", SystemLanguage.Chinese), Is.EqualTo("语言"));
            Assert.That(I18n.Translate("migration.menu_label", SystemLanguage.ChineseTraditional), Is.EqualTo("修復舊版元件引用"));
            Assert.That(I18n.Translate("language.title", SystemLanguage.French), Is.EqualTo("Language"));
        }

        [Test]
        public void RealTooltipTokensExpandForTheSelectedComponentLanguage()
        {
            var tooltip = typeof(ASSComponent).GetField("gestureHoldTime").GetCustomAttribute<TooltipAttribute>();
            I18n.SetLanguage(SystemLanguage.Japanese);
            string text = I18n.ExpandTokens(tooltip.tooltip);
            Assert.That(text, Is.EqualTo(I18n.T("gesture.hold_time_tooltip")));
            Assert.That(text, Does.Contain("連続"));
            Assert.That(text, Does.Not.Contain("#{"));
            I18n.SetLanguage(SystemLanguage.ChineseTraditional);
            var content = I18n.Content("gesture.hold_time", "gesture.hold_time_tooltip");
            Assert.That(content.text, Is.EqualTo(I18n.T("gesture.hold_time")));
            Assert.That(content.tooltip, Does.Contain("連續"));
        }

        [Test]
        public void LocalizedMigrationFormattingKeepsCountsAndBackupPaths()
        {
            foreach (SystemLanguage language in I18n.SupportedLanguages)
            {
                I18n.SetLanguage(language);
                string text = I18n.T("migration.restored", 3, 2, "UserSettings/ASS/{original}");
                Assert.That(text, Does.Contain("3"));
                Assert.That(text, Does.Contain("2"));
                Assert.That(text, Does.Contain("UserSettings/ASS/{original}"));
                Assert.That(text, Does.Not.Contain("{0}"));
                Assert.That(I18n.T("build.duplicate_configuration", "Avatar/Child"), Does.Contain("Avatar/Child"));
            }
        }

        [Test]
        public void GestureChoicesAreLocalizedWithoutChangingInputValues()
        {
            foreach (SystemLanguage language in I18n.SupportedLanguages)
            {
                I18n.SetLanguage(language);
                string[] choices = I18n.GetGestureNames();
                Assert.That(choices.Length, Is.EqualTo(8));
                for (int value = 0; value < choices.Length; value++)
                    Assert.That(choices[value], Does.StartWith(value + ": "));
            }
            I18n.SetLanguage(SystemLanguage.ChineseSimplified);
            Assert.That(I18n.GetGestureNames()[0], Does.Contain("闲置"));
            I18n.SetLanguage(SystemLanguage.Japanese);
            Assert.That(I18n.GetGestureNames()[0], Does.Contain("待機"));
        }

        [Test]
        public void InspectorDescribesAutomaticBuildAndContinuousHoldWithoutLockControls()
        {
            string source = InspectorSource();
            Assert.That(source, Does.Not.Contain("Generate Standalone Unity Controller"));
            Assert.That(source, Does.Not.Contain("SaveFilePanelInProject"));
            Assert.That(source, Does.Not.Contain("DrawLockSection"));
            Assert.That(source, Does.Not.Contain("disableRootChildren"));
            Assert.That(source, Does.Contain("build.automatic_help"));
            Assert.That(source, Does.Contain("gesture.continuous_hold_help"));
            Assert.That(source, Does.Contain("ExpandTokens(property.tooltip)"));
            Assert.That(I18n.Translate("build.automatic_help", SystemLanguage.English), Does.Contain("automatically"));
            Assert.That(I18n.Translate("gesture.continuous_hold_help", SystemLanguage.English), Does.Contain("uninterrupted"));
        }

        private static string InspectorSource()
        {
            string component = AssetDatabase.GUIDToAssetPath(LegacyComponentMigration.CanonicalGuid);
            string package = Path.GetDirectoryName(Path.GetDirectoryName(component));
            return File.ReadAllText(Path.Combine(package, "Editor/Inspector.cs"));
        }
    }
}
