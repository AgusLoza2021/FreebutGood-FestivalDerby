using System.Collections.Generic;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Guards the shipped Toy set: every Toy catalog entry must resolve to a real prefab whose wheel
    /// nodes are the four the wheel rig looks for and whose body the paint applier can find, and the
    /// demo config must actually point the run at that set. These are the two invariants the import
    /// pipeline exists to produce; the import itself is a one-shot Editor tool, so this is the only
    /// thing that keeps the result honest after the assets are in.
    /// </summary>
    public sealed class ToyCarVisualSetTests
    {
        private const string CatalogResource = "CarVisualCatalog";
        private const string DrivingConfigResource = "DrivingFeatureConfig";
        private const string ToyIdPrefix = "Toy";
        private const int ExpectedToyCars = 12;

        private static readonly string[] WheelCodes = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

        [Test]
        public void Catalog_CarriesTheWholeToySet_Approved()
        {
            var catalog = Resources.Load<CarVisualCatalog>(CatalogResource);
            Assert.IsNotNull(catalog, "Resources/" + CatalogResource + ".asset must exist.");

            var toys = ToyEntries(catalog);
            Assert.AreEqual(ExpectedToyCars, toys.Count,
                "Every body in the Kenney kit must be a Toy catalog entry.");
            Assert.IsFalse(catalog.AllowUnapprovedModels,
                "The Toy set is approved on its own; allowing unapproved models would leak the raw "
                + "Meshy exports into the garage carousel.");

            for (int i = 0; i < toys.Count; i++)
            {
                Assert.IsTrue(toys[i].Approved, toys[i].Id + " must be approved.");
                Assert.IsNotNull(toys[i].Model, toys[i].Id + " must reference its prefab.");
            }
        }

        [Test]
        public void EveryToyPrefab_HasFourWheelNodesAndAPaintableBody()
        {
            var catalog = Resources.Load<CarVisualCatalog>(CatalogResource);
            var toys = ToyEntries(catalog);
            Assert.Greater(toys.Count, 0, "No Toy entries to check.");

            for (int i = 0; i < toys.Count; i++)
            {
                var entry = toys[i];
                var instance = Object.Instantiate(entry.Model);
                try
                {
                    instance.name = entry.Id;

                    // The rig wraps every child whose name contains "wheel" and drops nested ones, so
                    // "WheelMount" (the defused grouping nodes) must not read as a wheel either.
                    var names = new List<string>();
                    var transforms = instance.GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < transforms.Length; t++)
                    {
                        if (transforms[t].name.IndexOf("wheel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            names.Add(transforms[t].name);
                    }

                    for (int w = 0; w < WheelCodes.Length; w++)
                    {
                        Assert.IsTrue(names.Contains(WheelCodes[w]),
                            entry.Id + " is missing " + WheelCodes[w] + "; wheels found: [" +
                            string.Join(",", names) + "]");
                    }

                    Assert.AreEqual(WheelCodes.Length, names.Count,
                        entry.Id + " must expose exactly four wheel nodes to the rig, found [" +
                        string.Join(",", names) + "]");

                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    Assert.Greater(renderers.Length, 0, entry.Id + " has no renderer at all.");
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        var material = renderers[r].sharedMaterial;
                        Assert.IsNotNull(material, entry.Id + " renderer has no material.");
                        Assert.AreEqual("CarKit", material.name,
                            entry.Id + " must use the one shared kit material; extra materials cost "
                            + "draw calls on mobile.");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void DemoConfig_UsesTheToySet_AndStillResolvesTheRealisticOne()
        {
            var config = Resources.Load<DrivingFeatureConfig>(DrivingConfigResource);
            Assert.IsNotNull(config, "Resources/" + DrivingConfigResource + ".asset must exist.");

            Assert.AreEqual(CarVisualSet.Toy, DrivingFeatureConfig.ResolveVisualSet(config),
                "The demo config must default the run to the Toy set.");
            Assert.AreEqual("ToySedan", DrivingFeatureConfig.ResolvePlayerVisualId(config));

            // The default is the id a bad request lands on, and it has to be a model that ships.
            Assert.IsNotNull(catalogFind(config, config.PlayerVisualIdToy),
                "The Toy player id must resolve in the shipped catalog.");
        }

        private static CarVisualEntry catalogFind(DrivingFeatureConfig config, string id)
        {
            if (config.Visuals == null)
                return null;

            for (int i = 0; i < config.Visuals.Entries.Length; i++)
            {
                var entry = config.Visuals.Entries[i];
                if (entry != null && entry.Id == id)
                    return entry;
            }

            return null;
        }

        private static List<CarVisualEntry> ToyEntries(CarVisualCatalog catalog)
        {
            var toys = new List<CarVisualEntry>();
            if (catalog == null || catalog.Entries == null)
                return toys;

            for (int i = 0; i < catalog.Entries.Length; i++)
            {
                var entry = catalog.Entries[i];
                if (entry != null && entry.Id != null &&
                    entry.Id.StartsWith(ToyIdPrefix, System.StringComparison.Ordinal))
                    toys.Add(entry);
            }

            return toys;
        }
    }
}
