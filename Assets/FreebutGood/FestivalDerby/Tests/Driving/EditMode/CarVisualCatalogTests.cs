using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The resolve rules are the whole guard for the seven uncleaned models: what the catalog says
    /// no to must never reach a car, and what it cannot answer must leave the gray box alone.
    /// </summary>
    public sealed class CarVisualCatalogTests
    {
        private const string DefaultId = "GreenVan";
        private const string VariantId = "BeigeSedan";

        private CarVisualCatalog _catalog;
        private CarVisualEntry _defaultEntry;
        private CarVisualEntry _variantEntry;
        private GameObject _defaultModel;
        private GameObject _variantModel;

        [SetUp]
        public void SetUp()
        {
            _defaultModel = new GameObject("Approved default model");
            _variantModel = new GameObject("Unapproved variant model");
            _defaultEntry = new CarVisualEntry { Id = DefaultId, Model = _defaultModel, Approved = true };
            _variantEntry = new CarVisualEntry { Id = VariantId, Model = _variantModel };
            _catalog = ScriptableObject.CreateInstance<CarVisualCatalog>();
            _catalog.DefaultId = DefaultId;
            _catalog.Entries = new[] { _defaultEntry, _variantEntry };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_defaultModel);
            Object.DestroyImmediate(_variantModel);
        }

        [Test]
        public void TryResolve_ApprovedId_ReturnsThatEntry()
        {
            Assert.That(_catalog.TryResolve(DefaultId, out CarVisualEntry entry), Is.True);
            Assert.That(entry, Is.SameAs(_defaultEntry));
        }

        [Test]
        public void TryResolve_UnapprovedId_FallsBackToTheDefaultEntry()
        {
            Assert.That(_variantEntry.Approved, Is.False);

            Assert.That(_catalog.TryResolve(VariantId, out CarVisualEntry entry), Is.True);
            Assert.That(entry, Is.SameAs(_defaultEntry));
            Assert.That(_variantEntry.Approved, Is.False, "Resolving must not approve anything.");
        }

        [Test]
        public void TryResolve_AllowUnapprovedModels_ReturnsTheUnapprovedEntry()
        {
            _catalog.AllowUnapprovedModels = true;

            Assert.That(_catalog.TryResolve(VariantId, out CarVisualEntry entry), Is.True);
            Assert.That(entry, Is.SameAs(_variantEntry));
        }

        [TestCase("UnknownCar")]
        [TestCase("greenvan")]
        [TestCase("")]
        public void TryResolve_UnknownOrEmptyId_ReturnsTheDefaultEntry(string id)
        {
            Assert.That(_catalog.TryResolve(id, out CarVisualEntry entry), Is.True);
            Assert.That(entry, Is.SameAs(_defaultEntry));
        }

        [Test]
        public void TryResolve_NullId_ReturnsTheDefaultEntry()
        {
            Assert.That(_catalog.TryResolve(null, out CarVisualEntry entry), Is.True);
            Assert.That(entry, Is.SameAs(_defaultEntry));
        }

        [Test]
        public void TryResolve_MatchingEntryWithoutModel_FallsBackToTheDefaultEntry()
        {
            _defaultEntry.Approved = true;
            _variantEntry.Approved = true;
            _variantEntry.Model = null;

            Assert.That(_catalog.TryResolve(VariantId, out CarVisualEntry entry), Is.True);
            Assert.That(entry, Is.SameAs(_defaultEntry));
        }

        [Test]
        public void TryResolve_DefaultWithoutModel_ReturnsFalse()
        {
            _defaultEntry.Model = null;

            Assert.That(_catalog.TryResolve(VariantId, out CarVisualEntry entry), Is.False);
            Assert.That(entry, Is.Null);
        }

        [Test]
        public void TryResolve_UnknownDefaultId_ReturnsFalse()
        {
            _catalog.DefaultId = "Missing";

            Assert.That(_catalog.TryResolve(null, out CarVisualEntry entry), Is.False);
            Assert.That(entry, Is.Null);
        }

        [Test]
        public void TryResolve_StaticNullCatalog_ReturnsFalse()
        {
            Assert.That(CarVisualCatalog.TryResolve(null, DefaultId, out CarVisualEntry entry), Is.False);
            Assert.That(entry, Is.Null);
        }

        [Test]
        public void TryResolve_StaticCatalog_DelegatesToTheInstance()
        {
            Assert.That(CarVisualCatalog.TryResolve(_catalog, VariantId, out CarVisualEntry entry), Is.True);
            Assert.That(entry, Is.SameAs(_defaultEntry));
        }

        [Test]
        public void Sanitize_NullArrayBecomesEmpty()
        {
            _catalog.Entries = null;

            _catalog.Sanitize();

            Assert.That(_catalog.Entries, Is.Not.Null);
            Assert.That(_catalog.Entries, Is.Empty);
            Assert.That(_catalog.TryResolve(DefaultId, out _), Is.False);
            Assert.That(_catalog.DefaultId, Is.EqualTo(DefaultId), "Sanitize never rewrites ids.");
        }

        [TestCase(0f)]
        [TestCase(-2f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Sanitize_UnusableScaleMultiplierBecomesOne(float value)
        {
            _variantEntry.ScaleMultiplier = value;

            _catalog.Sanitize();

            Assert.That(_variantEntry.ScaleMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Sanitize_KeepsUsableValues()
        {
            _defaultEntry.ScaleMultiplier = 0.5f;
            _variantEntry.ScaleMultiplier = 3f;
            _catalog.Entries = new[] { _defaultEntry, null, _variantEntry };

            _catalog.Sanitize();

            Assert.That(_defaultEntry.ScaleMultiplier, Is.EqualTo(0.5f));
            Assert.That(_variantEntry.ScaleMultiplier, Is.EqualTo(3f));
            Assert.That(_catalog.TryResolve(VariantId, out _), Is.True, "A null element must not break lookup.");
        }
    }
}
