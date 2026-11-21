using NUnit.Framework;

namespace FestivalDerby.Menu.Tests
{
    public sealed class MenuSelectionTests
    {
        [Test]
        public void Clamp_KeepsLapsAndBotsInsideTheirLimits()
        {
            var selection = new MenuSelection { Laps = 99, BotCount = 99 };
            selection.Clamp();
            Assert.AreEqual(MenuSelection.MaxLaps, selection.Laps);
            Assert.AreEqual(MenuSelection.MaxBots, selection.BotCount);

            selection.Laps = -4;
            selection.BotCount = -1;
            selection.Clamp();
            Assert.AreEqual(MenuSelection.MinLaps, selection.Laps);
            Assert.AreEqual(0, selection.BotCount);
        }

        [Test]
        public void Clamp_ReplacesAnUnknownBotLevelAndNullIds()
        {
            var selection = new MenuSelection { BotLevel = (BotLevel)42, MapId = null };
            selection.Clamp();
            Assert.AreEqual(BotLevel.Medium, selection.BotLevel);
            Assert.AreEqual("", selection.MapId);
        }

        [Test]
        public void SerializeThenParse_KeepsEveryChoice()
        {
            var original = new MenuSelection
            {
                ModeId = "Race", MapId = "BigOval", CarId = "ToyRace",
                Laps = 5, BotCount = 7, BotLevel = BotLevel.Hard,
            };

            MenuSelection copy = MenuSession.Parse(MenuSession.Serialize(original));

            Assert.AreEqual("Race", copy.ModeId);
            Assert.AreEqual("BigOval", copy.MapId);
            Assert.AreEqual("ToyRace", copy.CarId);
            Assert.AreEqual(5, copy.Laps);
            Assert.AreEqual(7, copy.BotCount);
            Assert.AreEqual(BotLevel.Hard, copy.BotLevel);
        }

        [Test]
        public void Parse_GivesDefaultsForEmptyOrCorruptText()
        {
            Assert.AreEqual(3, MenuSession.Parse("").Laps);
            Assert.AreEqual(3, MenuSession.Parse(null).Laps);
            Assert.AreEqual(3, MenuSession.Parse("{not json").Laps);
        }

        [Test]
        public void Parse_ClampsAValueSavedOutOfRange()
        {
            MenuSelection selection = MenuSession.Parse("{\"Laps\":500,\"BotCount\":500}");
            Assert.AreEqual(MenuSelection.MaxLaps, selection.Laps);
            Assert.AreEqual(MenuSelection.MaxBots, selection.BotCount);
        }

        [Test]
        public void EffectiveBotCount_IsZeroInFreeDriveAndTheChosenCountOtherwise()
        {
            var selection = new MenuSelection { ModeId = MenuSelection.ModeFreeDrive, BotCount = 7 };
            Assert.AreEqual(0, selection.EffectiveBotCount);

            selection.ModeId = MenuSelection.ModeRace;
            Assert.AreEqual(7, selection.EffectiveBotCount);
            Assert.AreEqual(MenuSelection.MaxBots, new MenuSelection { BotCount = 999 }.EffectiveBotCount);
        }

        [Test]
        public void Launch_IsOnlyActiveBetweenMarkAndReset()
        {
            MenuSession.ResetLaunch();
            Assert.IsFalse(MenuSession.HasLaunch);

            MenuSession.MarkLaunched();
            Assert.IsTrue(MenuSession.HasLaunch);

            MenuSession.ResetLaunch();
            Assert.IsFalse(MenuSession.HasLaunch);
        }

        [Test]
        public void BotLevelPace_StaysInsideTheBotRangeAndGrowsWithTheLevel()
        {
            float previousFastest = 0f;
            foreach (BotLevel level in new[] { BotLevel.Easy, BotLevel.Medium, BotLevel.Hard })
            {
                float slowest, fastest;
                BotLevelRules.Pace(level, out slowest, out fastest);
                Assert.GreaterOrEqual(slowest, 0.5f);
                Assert.LessOrEqual(fastest, 1f);
                Assert.LessOrEqual(slowest, fastest);
                Assert.Greater(fastest, previousFastest, level + " must be faster than the level before");
                previousFastest = fastest;
            }
        }
    }
}
