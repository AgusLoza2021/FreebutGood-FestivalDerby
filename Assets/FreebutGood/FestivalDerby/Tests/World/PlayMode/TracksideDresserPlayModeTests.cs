using System.Collections;
using FestivalDerby.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Pins the dresser's contract on the shipped circuit and kit: it builds kerbs, edge lines and props,
    /// adds no collider the server would not know about, dresses the same way every time, and leaves nothing
    /// behind when disposed.
    /// </summary>
    public sealed class TracksideDresserPlayModeTests
    {
        private const string MapResource = "Maps/Map_FestivalCircuit";
        private const string KitResource = "World/TracksideKit";
        private const uint Seed = 7u;

        private GameObject _anchor;
        private LoadedMap _loaded;
        private TracksideKit _kit;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var map = Resources.Load<MapAsset>(MapResource);
            Assert.IsNotNull(map, MapResource + " must ship in Resources");
            _kit = Resources.Load<TracksideKit>(KitResource);
            Assert.IsNotNull(_kit, KitResource + " must ship in Resources");

            _anchor = new GameObject("trackside-dresser-anchor");
            _loaded = MapLoader.Load(map, false, _anchor.transform);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // The baked chunk meshes are project assets; the owner would destroy them with the root.
            RoadMeshOwner[] owners = _anchor != null
                ? _anchor.GetComponentsInChildren<RoadMeshOwner>(true)
                : new RoadMeshOwner[0];
            for (int i = 0; i < owners.Length; i++)
                owners[i].Own(null);

            if (_loaded != null)
                _loaded.Dispose();
            _loaded = null;

            if (_anchor != null)
                Object.DestroyImmediate(_anchor);
            _anchor = null;
            yield return null;
        }

        [Test]
        public void Spawn_WithoutKitOrMap_DressesNothing()
        {
            Assert.IsNull(TracksideDresser.Spawn(_loaded, _anchor.transform, null, Seed));
            Assert.IsNull(TracksideDresser.Spawn(null, _anchor.transform, _kit, Seed));
        }

        [UnityTest]
        public IEnumerator Spawn_OnTheShippedCircuit_BuildsStripsAndPropsWithoutColliders()
        {
            TracksideDresser dresser = TracksideDresser.Spawn(_loaded, _anchor.transform, _kit, Seed);

            Assert.IsNotNull(dresser);
            Assert.That(dresser.StripCount, Is.GreaterThan(0), "A circuit with corners must get kerbs and lines.");
            Assert.That(dresser.PropCount, Is.GreaterThan(0), "The far band must place props.");
            Assert.IsNotNull(dresser.Root.transform.Find("Kerb strips"), "Corners must get a kerb mesh.");
            Assert.IsNotNull(dresser.Root.transform.Find("Edge line strips"), "The lap must get edge lines.");
            Assert.AreEqual(2, dresser.Root.transform.Find("Kerb strips").GetComponent<MeshRenderer>()
                .sharedMaterials.Length, "Kerbs alternate two colours, one material each.");

            yield return null; // Object.Destroy on stripped colliders lands at the end of the frame.
            Assert.IsEmpty(dresser.Root.GetComponentsInChildren<Collider>(true),
                "Dressing is visual only: the server simulates physics without it.");

            dresser.Dispose();
        }

        [UnityTest]
        public IEnumerator Spawn_SameSeed_PlacesTheSameProps()
        {
            TracksideDresser first = TracksideDresser.Spawn(_loaded, _anchor.transform, _kit, Seed);
            int props = first.PropCount;
            int strips = first.StripCount;
            first.Dispose();
            yield return null;

            TracksideDresser second = TracksideDresser.Spawn(_loaded, _anchor.transform, _kit, Seed);
            Assert.AreEqual(props, second.PropCount);
            Assert.AreEqual(strips, second.StripCount);
            second.Dispose();
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysTheRootAndIsIdempotent()
        {
            TracksideDresser dresser = TracksideDresser.Spawn(_loaded, _anchor.transform, _kit, Seed);
            GameObject root = dresser.Root;

            dresser.Dispose();
            dresser.Dispose();
            yield return null;

            Assert.IsTrue(root == null, "The dressing root must not outlive the dresser.");
            Assert.IsNull(GameObject.Find(TracksideDresser.RootName));
        }
    }
}
