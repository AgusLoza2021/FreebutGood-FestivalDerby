using FestivalDerby.World.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the shipped Festival Circuit asset, read the way a runtime reads it: through the
    /// MapAsset, not through the generator's control points. The numbers below are the brief's, so a
    /// later retune of the layout has to keep them true or fail here.
    /// </summary>
    [TestFixture]
    public sealed class CompactMapGeneratorTests
    {
        private MapAsset _map;

        [SetUp]
        public void SetUp()
        {
            _map = AssetDatabase.LoadAssetAtPath<MapAsset>(CompactMapGenerator.MapAssetPath);
            Assert.IsNotNull(_map, "the circuit is not in the project; bake it with "
                + CompactMapGenerator.GenerateMenu + " and commit " + CompactMapGenerator.MapAssetPath);
        }

        private BakedPath Path
        {
            get { return _map.PrimaryRoad.Path; }
        }

        [Test]
        public void Map_IsOneClosedEighteenMetreRoadThatCarriesItsOwnMeshes()
        {
            Assert.AreEqual(CompactMapGenerator.MapId, _map.MapId);
            Assert.AreEqual(1, _map.Roads.Length, "the circuit is a single road");
            Assert.IsNotNull(_map.PrimaryRoad, "the loader spawns on the primary road");
            Assert.IsTrue(Path.IsUsable, "the baked path cannot be driven");
            Assert.IsTrue(Path.Closed, "a circuit is a closed lap, not a point-to-point");

            Assert.That(Path.WidthLeft[0] + Path.WidthRight[0], Is.EqualTo(CompactMapGenerator.RoadWidth)
                .Within(0.01f), "the brief asks for an 18 m road");

            Assert.IsNotNull(_map.Roads[0].Chunks, "chunk meshes are what the loader builds colliders from");
            Assert.Greater(_map.Roads[0].Chunks.Length, 0);
            for (int i = 0; i < _map.Roads[0].Chunks.Length; i++)
                Assert.IsNotNull(_map.Roads[0].Chunks[i].Mesh, "road chunk " + i + " has no mesh");

            Assert.IsTrue(_map.HasCameraOverride);
            Assert.AreEqual(new Vector3(-16f, 30f, -14f), _map.CameraOffset,
                "the camera offset is the look's; a map must not disagree with the profile");
            Assert.That(_map.CameraLookHeight, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void Lap_IsInsideTheNineHundredToTwelveHundredMetreBand()
        {
            Debug.Log("[agent] circuit asset lap " + Path.TotalLength.ToString("F1") + " m");
            Assert.That(Path.TotalLength, Is.InRange(CompactMapGenerator.MinLapLength,
                CompactMapGenerator.MaxLapLength));
        }

        [Test]
        public void TightestCorner_KeepsAtLeastTwentyOneMetres()
        {
            float radius = CompactMapGenerator.MinRadius(Path);
            Debug.Log("[agent] circuit asset min radius " + radius.ToString("F3") + " m");
            Assert.That(radius, Is.GreaterThanOrEqualTo(CompactMapGenerator.MinimumRadius),
                "a tighter corner than the floor pinches the road the bots drive on");
        }

        [Test]
        public void Validator_FindsNothingToReport()
        {
            MapIssue[] issues = CompactMapGenerator.Validate(_map, new MapIssue[64]);
            Assert.AreEqual(0, issues.Length, Describe(issues));
        }

        [Test]
        public void Checkpoints_SplitTheLapEvenlyAndInOrder()
        {
            float[] checkpoints = _map.CheckpointDistances;
            Assert.That(checkpoints.Length, Is.InRange(4, 6));

            float spacing = Path.TotalLength / checkpoints.Length;
            for (int i = 0; i < checkpoints.Length; i++)
            {
                Assert.That(checkpoints[i], Is.EqualTo(i * spacing).Within(0.01f),
                    "checkpoint " + i + " is not where an even lap would put it");
                if (i > 0)
                    Assert.Greater(checkpoints[i], checkpoints[i - 1]);
            }
        }

        [Test]
        public void Dirt_RunsOnceAcrossTheLongLeftHander()
        {
            float dirt = CompactMapGenerator.SurfaceLength(Path, SurfaceId.Dirt);
            float first = CompactMapGenerator.FirstSurfaceDistance(Path, SurfaceId.Dirt);

            Assert.That(dirt, Is.GreaterThan(40f), "the long left-hander is supposed to be a real dirt section");
            Assert.That(dirt, Is.LessThan(Path.TotalLength * 0.25f), "one section, not a dirt map");
            Assert.AreEqual(1, SurfaceRuns(SurfaceId.Dirt), "the brief asks for a single dirt span");
            Assert.That(first, Is.GreaterThan(Path.TotalLength * 0.7f),
                "the dirt belongs on the far side of the lap, on the long left-hander");
            Assert.That(first + dirt, Is.LessThan(Path.TotalLength * 0.95f));
            Assert.That(CompactMapGenerator.SurfaceLength(Path, SurfaceId.Street),
                Is.GreaterThan(Path.TotalLength * 0.7f), "asphalt is still the base surface");
        }

        [Test]
        public void StartGrid_StandsOnTheStraightThatOpensTheLap()
        {
            Assert.AreEqual(3, _map.SpawnColumns);
            Assert.AreEqual(2, _map.SpawnRows);

            // The runtime lays the grid out behind the line, so the road it stands on is the end of the
            // lap: the straight the closing and opening elements share. Every sample under the grid is
            // dead flat except the last one, at the loop's seam, where the sampler measures the two
            // chords that meet at the wrap and reports 3.1e-3 1/m - a 320 m radius, which is rounding
            // and not a corner. The gate sits several times below the 1.5e-2 1/m even the lap's widest
            // sweeper (R67.5) carries, so a grid put on any real bend still fails here.
            float depth = _map.SpawnBackOffset + _map.SpawnSpacing;
            Assert.That(depth, Is.LessThanOrEqualTo(10f), "the grid has to fit inside the straight");
            Assert.That(CurvatureOver(Path.TotalLength - depth - 5f, Path.TotalLength), Is.LessThan(0.005f),
                "the grid is not standing on a straight");

            // And it is the lap's one long straight: the road ahead of the line has to stay straight all
            // the way to the sweeper, not just for the grid's nine metres.
            Assert.That(CurvatureOver(0f, 50f), Is.LessThan(0.005f),
                "the lap does not open with a long straight into the sweeper");
        }

        private float CurvatureOver(float from, float to)
        {
            float worst = 0f;
            for (int i = 0; i < Path.Count; i++)
            {
                if (Path.Distances[i] < from || Path.Distances[i] > to)
                    continue;
                float magnitude = Mathf.Abs(Path.Curvature[i]);
                if (magnitude > worst)
                    worst = magnitude;
            }

            return worst;
        }

        private int SurfaceRuns(SurfaceId surface)
        {
            byte wanted = (byte)surface;
            int runs = 0;
            for (int i = 0; i < Path.Count; i++)
            {
                if (Path.SurfacePrimary[i] != wanted)
                    continue;
                if (i == 0 || Path.SurfacePrimary[i - 1] != wanted)
                    runs++;
            }

            return runs;
        }

        private static string Describe(MapIssue[] issues)
        {
            var text = new System.Text.StringBuilder("the validator reported " + issues.Length + " issue(s):");
            for (int i = 0; i < issues.Length; i++)
                text.Append("\n  ").Append(issues[i].Severity).Append(' ').Append(issues[i].Code)
                    .Append(" @ ").Append(issues[i].Distance);
            return text.ToString();
        }
    }
}
