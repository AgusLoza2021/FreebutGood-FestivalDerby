using NUnit.Framework;
using UnityEngine;

namespace FestivalDerby.Menu.Tests
{
    public sealed class LocalizationTests
    {
        private LocalizationTable _table;

        [SetUp]
        public void SetUp()
        {
            _table = ScriptableObject.CreateInstance<LocalizationTable>();
            var so = new UnityEditor.SerializedObject(_table);
            var entries = so.FindProperty("_entries");
            entries.arraySize = 2;
            Fill(entries.GetArrayElementAtIndex(0), "menu.play", "PLAY", "JUGAR");
            Fill(entries.GetArrayElementAtIndex(1), "menu.only.english", "Only English", "");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_table);
        }

        private static void Fill(UnityEditor.SerializedProperty entry, string key, string en, string es)
        {
            entry.FindPropertyRelative("Key").stringValue = key;
            entry.FindPropertyRelative("English").stringValue = en;
            entry.FindPropertyRelative("Spanish").stringValue = es;
        }

        [Test]
        public void Resolve_ReturnsTheTextOfTheRequestedLanguage()
        {
            Assert.AreEqual("PLAY", Loc.Resolve(_table, "menu.play", Language.English));
            Assert.AreEqual("JUGAR", Loc.Resolve(_table, "menu.play", Language.Spanish));
        }

        [Test]
        public void Resolve_FallsBackToEnglishWhenTheLanguageHasNoText()
        {
            Assert.AreEqual("Only English", Loc.Resolve(_table, "menu.only.english", Language.Spanish));
        }

        [Test]
        public void Resolve_ReturnsTheKeyWhenItIsUnknownOrThereIsNoTable()
        {
            Assert.AreEqual("menu.missing", Loc.Resolve(_table, "menu.missing", Language.English));
            Assert.AreEqual("menu.play", Loc.Resolve(null, "menu.play", Language.Spanish));
        }

        [Test]
        public void Detect_PrefersTheSavedLanguageThenTheDevice()
        {
            Assert.AreEqual(Language.English, Loc.Detect(0, SystemLanguage.Spanish));
            Assert.AreEqual(Language.Spanish, Loc.Detect(1, SystemLanguage.English));
            Assert.AreEqual(Language.Spanish, Loc.Detect(-1, SystemLanguage.Spanish));
            Assert.AreEqual(Language.English, Loc.Detect(-1, SystemLanguage.German));
            Assert.AreEqual(Language.English, Loc.Detect(7, SystemLanguage.English));
        }
    }
}
