using System.Collections.Generic;
using FestivalDerby.World.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the L2 authoring data and the two pieces it owns that Core does not: the grid
    /// setup the tools snap with, and the editor-side completion of a symmetrical half. The bake tests
    /// go through MapBaker so the authoring numbers are proven to survive the real bake, not a stub.
    /// </summary>
    [TestFixture]
    public sealed class DerbyMapAuthoringTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void Sanitize_ClampsTheNumbersTheToolsRead()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            authoring.RoadWidth = -4f;
            authoring.GridStep = -2f;
            authoring.Symmetry.RotationalCount = 99;
            authoring.MapId = string.Empty;

            authoring.Sanitize();

            Assert.That(authoring.RoadWidth, Is.GreaterThanOrEqualTo(2f));
            Assert.That(authoring.GridStep, Is.EqualTo(0f));
            Assert.That(authoring.Symmetry.RotationalCount, Is.InRange(2, 12));
            Assert.That(authoring.MapId, Is.Not.Empty);
        }

        [Test]
        public void Sanitize_OrdersSurfaceSpanEndpoints()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            authoring.SurfaceSpans = new[] { new SurfaceSpan(30f, 10f, SurfaceId.Gravel, -5f) };

            authoring.Sanitize();

            Assert.That(authoring.SurfaceSpans[0].StartDistance, Is.EqualTo(10f));
            Assert.That(authoring.SurfaceSpans[0].EndDistance, Is.EqualTo(30f));
            Assert.That(authoring.SurfaceSpans[0].TransitionLength, Is.EqualTo(0f));
        }

        [Test]
        public void GridSnap_RoundsToTheAuthoringOriginAndStep()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            authoring.GridStep = 2f;
            authoring.GridOrigin = new Vector3(1f, 0f, 1f);

            PathVector3 snapped = authoring.BuildGridSnap().Snap(new PathVector3(4.2f, 0f, -2.9f));

            Assert.That(snapped.x, Is.EqualTo(5f).Within(1e-4f));
            Assert.That(snapped.z, Is.EqualTo(-3f).Within(1e-4f));
        }

        [Test]
        public void OvalPreset_BakesAClosedPathWithThePresetPerimeter()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            DerbyMapPresets.Apply(authoring, DerbyMapPresets.Kind.Oval);

            BakedPath path = Bake(authoring);

            float expected = OvalMapGenerator.LapLength();
            Assert.That(path.Closed, Is.True);
            Assert.That(path.Count, Is.GreaterThan(16));
            Assert.That(path.TotalLength, Is.EqualTo(expected).Within(expected * 0.01f));
        }

        [Test]
        public void BakePath_CarriesTheAuthoredSurfaceSpans()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            DerbyMapPresets.Apply(authoring, DerbyMapPresets.Kind.Oval);

            float lap = OvalMapGenerator.LapLength();
            authoring.SurfaceSpans = new[] { new SurfaceSpan(lap * 0.25f, lap * 0.5f, SurfaceId.Mud) };

            BakedPath path = Bake(authoring);

            Assert.That(path.SurfacePrimary.Length, Is.EqualTo(path.Count));
            Assert.That(path.SurfaceAt(lap * 0.375f).Primary, Is.EqualTo(SurfaceId.Mud));
            Assert.That(path.SurfaceAt(lap * 0.1f).Primary, Is.EqualTo(SurfaceId.Street));
        }

        [Test]
        public void ArenaPreset_SetsTheDerbySpawnGrid()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            DerbyMapPresets.Apply(authoring, DerbyMapPresets.Kind.Arena);

            BakedMapSettings settings = DerbyMapBake.SettingsFrom(authoring);

            Assert.That(authoring.Kind, Is.EqualTo(DerbyMapAuthoring.MapKind.Derby));
            Assert.That(settings.SpawnColumns, Is.GreaterThan(2));
            Assert.That(settings.SpawnRows, Is.GreaterThan(2));
            Assert.That(authoring.RoadWidth, Is.EqualTo(settings.RoadWidth));
        }

        [Test]
        public void MirrorSymmetry_CompletesTheHalfAndWeldsBothSeams()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            authoring.Symmetry.Mode = DerbyMapAuthoring.SymmetryMode.MirrorX;
            authoring.Symmetry.Pivot = Vector3.zero;
            authoring.ClosedLoop = true;

            // A right half circle: both ends sit on the x = 0 plane, so both seams are duplicates.
            Vector3[] half = ArcPoints(20f, 8, -90f, 90f);

            Vector3[] full = DerbyMapGeometry.BuildSymmetry(authoring, half);

            Assert.That(full.Length, Is.EqualTo(2 * half.Length - 2));
            Assert.That(HasPointNear(full, new Vector3(-20f, 0f, 0f)), Is.True);
            Assert.That(HasPointNear(full, new Vector3(20f, 0f, 0f)), Is.True);
        }

        [Test]
        public void RotationalSymmetry_FillsEverySectorAndWeldsTheWrap()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            authoring.Symmetry.Mode = DerbyMapAuthoring.SymmetryMode.Rotational;
            authoring.Symmetry.Pivot = Vector3.zero;
            authoring.Symmetry.RotationalCount = 4;
            authoring.ClosedLoop = true;

            // A proper quarter: its end rotated by 270 degrees lands exactly on its start, which is
            // what makes four copies chain into one loop and the wrap seam weldable.
            Vector3[] quarter = ArcPoints(20f, 8, 0f, 90f);

            Vector3[] full = DerbyMapGeometry.BuildSymmetry(authoring, quarter);

            Assert.That(full.Length, Is.EqualTo(4 * quarter.Length - 1));
            Assert.That(HasPointNear(full, new Vector3(0f, 0f, -20f)), Is.True);
        }

        [Test]
        public void SamplePreview_PlacesEdgesAtHalfTheRoadWidth()
        {
            DerbyMapAuthoring authoring = CreateAuthoring();
            DerbyMapPresets.Apply(authoring, DerbyMapPresets.Kind.Oval);
            authoring.RoadWidth = 20f;

            DerbyMapGeometry.EdgePreview preview =
                DerbyMapGeometry.SamplePreview(DerbyMapGeometry.ContainerOf(authoring), authoring, 64);

            Assert.That(preview.Left.Length, Is.EqualTo(65));
            Assert.That(preview.Length, Is.GreaterThan(100f));
            float offset = (preview.Left[32] - preview.Right[32]).magnitude;
            Assert.That(offset, Is.EqualTo(20f).Within(0.05f));
        }

        /// <summary>
        /// Points on a circle of the given radius, from <paramref name="fromDegrees"/> to
        /// <paramref name="toDegrees"/> inclusive. The in-plane angle is (x = cos, z = sin), which is
        /// the convention the rotational symmetry uses.
        /// </summary>
        private static Vector3[] ArcPoints(float radius, int segments, float fromDegrees, float toDegrees)
        {
            var points = new List<Vector3>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, (float)i / segments);
                points.Add(new Vector3(radius * Mathf.Cos(angle), 0f, radius * Mathf.Sin(angle)));
            }

            return points.ToArray();
        }

        private static bool HasPointNear(Vector3[] points, Vector3 expected)
        {
            const float tolerance = 0.01f;
            for (int i = 0; i < points.Length; i++)
            {
                if ((points[i] - expected).sqrMagnitude <= tolerance * tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private BakedPath Bake(DerbyMapAuthoring authoring)
        {
            authoring.Sanitize();
            SplineContainer container = DerbyMapGeometry.ContainerOf(authoring);
            return MapBaker.BakePath(container, container.Splines[0], DerbyMapBake.SettingsFrom(authoring));
        }

        private DerbyMapAuthoring CreateAuthoring()
        {
            _host = new GameObject("DerbyMapAuthoringTests");
            var container = _host.AddComponent<SplineContainer>();
            container.Splines = new List<Spline> { new Spline(new float3[0], TangentMode.AutoSmooth, true) };
            return _host.AddComponent<DerbyMapAuthoring>();
        }
    }
}
