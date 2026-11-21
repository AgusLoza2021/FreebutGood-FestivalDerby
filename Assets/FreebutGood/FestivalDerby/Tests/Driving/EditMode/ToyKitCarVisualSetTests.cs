using System.Collections.Generic;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// Guards the imported Kenney Toy Car Kit set before the owner approves it: the catalog must
    /// carry all eight cars with the approval kill-switch still on, and every prefab must expose
    /// the wheel nodes, the paintable body and the readable mesh the rig, the paint and the dent
    /// systems assume. Like the Toy set test, this is what keeps the one-shot importer honest
    /// after the assets land.
    /// </summary>
    public sealed class ToyKitCarVisualSetTests
    {
        private const string CatalogResource = "CarVisualCatalog";
        private const string KitIdPrefix = "Kit";
        private const int ExpectedKitCars = 8;
        private const int MaxBodyVertices = 4000;

        private static readonly string[] WheelCodes = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

        [Test]
        public void Catalog_CarriesTheWholeKitSet_StillUnapproved()
        {
            var catalog = Resources.Load<CarVisualCatalog>(CatalogResource);
            Assert.IsNotNull(catalog, "Resources/" + CatalogResource + ".asset must exist.");

            var kits = KitEntries(catalog);
            Assert.AreEqual(ExpectedKitCars, kits.Count,
                "Every car in the Toy Car Kit must be a catalog entry.");

            // The set ships unapproved: it must not leak into the garage or the run until the
            // owner judges each car, which is exactly what Approved = false enforces.
            Assert.IsFalse(catalog.AllowUnapprovedModels,
                "AllowUnapprovedModels would bypass the kill-switch and put the raw kit cars on screen.");

            for (int i = 0; i < kits.Count; i++)
            {
                Assert.IsNotNull(kits[i].Model, kits[i].Id + " must reference its prefab.");
                Assert.IsFalse(kits[i].Approved,
                    kits[i].Id + " must stay unapproved until the owner flips the flag.");
            }
        }

        [Test]
        public void EveryKitPrefab_HasFourWheelNodesAndABodyRenderer()
        {
            foreach (var entry in KitEntriesOfLoadedCatalog())
            {
                var instance = Object.Instantiate(entry.Model);
                try
                {
                    instance.name = entry.Id;

                    // Same rule as the Toy set: the rig wraps every transform whose name reads as
                    // "wheel", so anything besides the four real wheels would be grabbed too.
                    var wheelNames = new List<string>();
                    var transforms = instance.GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < transforms.Length; t++)
                    {
                        if (transforms[t].name.IndexOf("wheel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            wheelNames.Add(transforms[t].name);
                    }

                    for (int w = 0; w < WheelCodes.Length; w++)
                    {
                        Assert.IsTrue(wheelNames.Contains(WheelCodes[w]),
                            entry.Id + " is missing " + WheelCodes[w] + "; wheels found: [" +
                            string.Join(",", wheelNames) + "]");
                    }
                    Assert.AreEqual(WheelCodes.Length, wheelNames.Count,
                        entry.Id + " must expose exactly four wheel nodes, found [" +
                        string.Join(",", wheelNames) + "]");

                    bool foundBody = false;
                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        if (renderers[r].name == entry.Id + "_Body")
                            foundBody = true;
                        Assert.IsNotNull(renderers[r].sharedMaterial,
                            entry.Id + " renderer '" + renderers[r].name + "' has no material.");
                    }
                    Assert.IsTrue(foundBody,
                        entry.Id + " must keep a renderer named '" + entry.Id + "_Body' for the paint targets.");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void EveryKitBody_IsSmallEnoughForMobile_AndReadableForDents()
        {
            foreach (var entry in KitEntriesOfLoadedCatalog())
            {
                var instance = Object.Instantiate(entry.Model);
                try
                {
                    int totalVertices = 0;
                    var meshFilters = instance.GetComponentsInChildren<MeshFilter>(true);
                    for (int m = 0; m < meshFilters.Length; m++)
                    {
                        // Wheels are excluded: the vertex budget covers what the dent system bends.
                        if (meshFilters[m].gameObject.name.IndexOf("wheel",
                                System.StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        var mesh = meshFilters[m].sharedMesh;
                        Assert.IsNotNull(mesh, entry.Id + " has a MeshFilter without a mesh.");
                        Assert.IsTrue(mesh.isReadable,
                            entry.Id + " mesh must stay Read/Write for the dent system.");
                        totalVertices += mesh.vertexCount;
                    }

                    Assert.LessOrEqual(totalVertices, MaxBodyVertices,
                        entry.Id + " body has " + totalVertices + " vertices, above the mobile budget.");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void EveryKitPrefab_NosesAlongZ()
        {
            foreach (var entry in KitEntriesOfLoadedCatalog())
            {
                var instance = Object.Instantiate(entry.Model);
                try
                {
                    // The catalog assumes +Z forward with no EulerOffset. World bounds, not local mesh
                    // bounds: an FBX that keeps its axis conversion on the transform has Z as height.
                    // CarWheelRig calls the larger-z pair the front, so the front wheels must lead.
                    var bounds = new Bounds();
                    bool any = false;
                    float frontZ = 0f, rearZ = 0f;
                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        var b = renderers[r].bounds;
                        if (any) bounds.Encapsulate(b); else { bounds = b; any = true; }
                        string n = renderers[r].name;
                        if (n == "Wheel_FL" || n == "Wheel_FR") frontZ += b.center.z;
                        if (n == "Wheel_RL" || n == "Wheel_RR") rearZ += b.center.z;
                    }
                    Assert.IsTrue(any, entry.Id + " has no renderer to measure.");
                    Assert.Greater(bounds.size.z, bounds.size.x,
                        entry.Id + " is longer on X (" + bounds.size.x + ") than Z (" + bounds.size.z +
                        "); the nose is no longer on +Z.");
                    Assert.Greater(bounds.size.z, bounds.size.y, entry.Id + " is taller than it is long.");
                    Assert.Greater(frontZ, rearZ, entry.Id + " front wheels are behind the rear ones.");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private static List<CarVisualEntry> KitEntriesOfLoadedCatalog()
        {
            var catalog = Resources.Load<CarVisualCatalog>(CatalogResource);
            Assert.IsNotNull(catalog, "Resources/" + CatalogResource + ".asset must exist.");
            var kits = KitEntries(catalog);
            Assert.Greater(kits.Count, 0, "No Kit entries to check.");
            return kits;
        }

        private static List<CarVisualEntry> KitEntries(CarVisualCatalog catalog)
        {
            var kits = new List<CarVisualEntry>();
            if (catalog == null || catalog.Entries == null)
                return kits;

            for (int i = 0; i < catalog.Entries.Length; i++)
            {
                var entry = catalog.Entries[i];
                if (entry != null && entry.Id != null &&
                    entry.Id.StartsWith(KitIdPrefix, System.StringComparison.Ordinal))
                    kits.Add(entry);
            }

            return kits;
        }
    }
}
