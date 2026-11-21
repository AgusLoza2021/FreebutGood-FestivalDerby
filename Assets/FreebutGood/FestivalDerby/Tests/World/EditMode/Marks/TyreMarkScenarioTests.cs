using System;
using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Drives the mark core the way the Unity adapter will: one synthetic <see cref="TyreContact"/> per
    /// wheel per fixed tick, through the rules, the trail and the burst list. This is the seam contract,
    /// so it is worth an end-to-end test even though every unit above has its own.
    ///
    /// The Unity side must supply, per wheel per tick: the contact X/Z, the planar lateral slip speed,
    /// the longitudinal slip (lock or spin), the ground speed, grounded and the surface. That is exactly
    /// the <see cref="TyreContact"/> payload; nothing else is required to lay the marks.
    /// </summary>
    [TestFixture]
    public sealed class TyreMarkScenarioTests
    {
        private const float Dt = 0.02f;

        private static float LaneZ(int wheel)
        {
            // Four lanes, two axles apart longitudinally and two tracks apart laterally.
            float axle = (wheel % 2 == 0) ? -1.25f : 1.25f;
            float track = (wheel < 2) ? -0.8f : 0.8f;
            return axle + track;
        }

        private static void Step(
            TyreMarkTrail trail,
            TyreMarkRules rules,
            TyreMarkWheelState[] states,
            TyreFxEvents fx,
            float x,
            float lateralSlip,
            bool grounded,
            SurfaceId surface)
        {
            for (int w = 0; w < states.Length; w++)
            {
                float z = LaneZ(w);
                var contact = new TyreContact(x, z, lateralSlip, 0f, 20f, grounded, surface);
                TyreMarkDecision decision = TyreMarkRules.Evaluate(rules, contact, ref states[w]);
                trail.Submit(w, decision, x, z);
                fx.Submit(decision, x, z);
            }

            trail.Advance(Dt);
        }

        [Test]
        public void HandbrakeSlide_LaysMarksThenBreaksOnRelease()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var trail = new TyreMarkTrail(capacitySegments: 512, wheelCount: 4);
            var states = new TyreMarkWheelState[4];
            var fx = new TyreFxEvents(16);

            float x = 0f;
            // Grip: nothing is laid.
            for (int i = 0; i < 20; i++)
            {
                x += 0.4f;
                Step(trail, rules, states, fx, x, 0.3f, true, SurfaceId.Street);
            }

            Assert.That(trail.Count, Is.EqualTo(0), "a gripping car leaves no marks");

            // Handbrake: all four wheels slide sideways.
            for (int i = 0; i < 40; i++)
            {
                x += 0.4f;
                Step(trail, rules, states, fx, x, 4f, true, SurfaceId.Street);
            }

            int sliding = trail.Count;
            Assert.That(sliding, Is.GreaterThan(0), "the slide lays marks");
            Assert.That(fx.Count, Is.GreaterThan(0), "and throws smoke");
            Assert.That(fx.At(0).Kind, Is.EqualTo(TyreFxKind.Smoke));

            // Release: the car grips again and the strips end.
            for (int i = 0; i < 20; i++)
            {
                x += 0.4f;
                Step(trail, rules, states, fx, x, 0.3f, true, SurfaceId.Street);
            }

            Assert.That(trail.Count, Is.EqualTo(sliding), "a released wheel adds no segment");

            // The mesh sees the whole thing and matches its advertised counts.
            int vertices = TyreMarkMesh.RequiredVertexCount(trail);
            int indices = TyreMarkMesh.RequiredIndexCount(trail);
            var positions = new float[vertices * 3];
            var colors = new float[vertices * 4];
            var indexBuffer = new int[indices];
            TyreMarkMeshFill fill = TyreMarkMesh.Fill(
                trail, TyreMarkMesh.LiftMeters, positions, colors, indexBuffer, null);

            Assert.That(fill.VertexCount, Is.EqualTo(vertices));
            Assert.That(fill.IndexCount, Is.EqualTo(indices));
            for (int i = 0; i < vertices * 3; i++)
                Assert.That(float.IsNaN(positions[i]), Is.False);
        }

        [Test]
        public void AirborneMidSlide_BreaksTheStrip()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var trail = new TyreMarkTrail(capacitySegments: 128, wheelCount: 1);
            var states = new TyreMarkWheelState[1];
            var fx = new TyreFxEvents(4);

            float x = 0f;
            for (int i = 0; i < 6; i++)
            {
                x += 0.4f;
                Step(trail, rules, states, fx, x, 4f, true, SurfaceId.Dirt);
            }

            int before = trail.Count;
            Assert.That(before, Is.GreaterThan(0));
            int firstStrip = trail.SegmentAt(0).StripId;

            // One airborne tick, then back on the dirt.
            x += 0.4f;
            Step(trail, rules, states, fx, x, 4f, false, SurfaceId.Dirt);
            x += 0.4f;
            Step(trail, rules, states, fx, x, 4f, true, SurfaceId.Dirt);
            x += 0.4f;
            Step(trail, rules, states, fx, x, 4f, true, SurfaceId.Dirt);

            Assert.That(trail.Count, Is.GreaterThan(before));
            Assert.That(trail.SegmentAt(trail.Count - 1).StripId, Is.Not.EqualTo(firstStrip));
        }

        [Test]
        public void DirtSlide_ThrowsDustNotSmoke()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var trail = new TyreMarkTrail(capacitySegments: 128, wheelCount: 1);
            var states = new TyreMarkWheelState[1];
            var fx = new TyreFxEvents(4);

            float x = 0f;
            for (int i = 0; i < 5; i++)
            {
                x += 0.4f;
                Step(trail, rules, states, fx, x, 5f, true, SurfaceId.Dirt);
            }

            Assert.That(fx.Count, Is.GreaterThan(0));
            Assert.That(fx.At(0).Kind, Is.EqualTo(TyreFxKind.Dust));
            Assert.That(fx.At(0).Surface, Is.EqualTo(SurfaceId.Dirt));
        }

        [Test]
        public void Deterministic_TwoRunsGiveIdenticalSegments()
        {
            TyreMarkSegment[] first = RunSlide();
            TyreMarkSegment[] second = RunSlide();

            Assert.That(second.Length, Is.EqualTo(first.Length));
            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(second[i].X0, Is.EqualTo(first[i].X0));
                Assert.That(second[i].Z0, Is.EqualTo(first[i].Z0));
                Assert.That(second[i].StripId, Is.EqualTo(first[i].StripId));
                Assert.That(second[i].Intensity, Is.EqualTo(first[i].Intensity));
                Assert.That(second[i].Age, Is.EqualTo(first[i].Age));
            }
        }

        private static TyreMarkSegment[] RunSlide()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var trail = new TyreMarkTrail(capacitySegments: 256, wheelCount: 4);
            var states = new TyreMarkWheelState[4];
            var fx = new TyreFxEvents(16);

            float x = 0f;
            for (int i = 0; i < 80; i++)
            {
                x += 0.35f;
                Step(trail, rules, states, fx, x, 3.5f, true, SurfaceId.Gravel);
            }

            var segments = new TyreMarkSegment[trail.Count];
            for (int i = 0; i < trail.Count; i++)
                segments[i] = trail.SegmentAt(i);
            return segments;
        }

        [Test]
        public void TenThousandTicks_AllocateNothing()
        {
            TyreMarkRules rules = TyreMarkRules.Default;
            var trail = new TyreMarkTrail(capacitySegments: 256, wheelCount: 4);
            var states = new TyreMarkWheelState[4];
            var fx = new TyreFxEvents(8);

            // Warm up so JIT and any first-call allocation happen outside the measured region.
            for (int i = 0; i < 200; i++)
            {
                Step(trail, rules, states, fx, i * 0.1f, 4f, true, SurfaceId.Street);
                fx.Clear();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                Step(trail, rules, states, fx, i * 0.1f, 4f, true, SurfaceId.Street);
                fx.Clear();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("metric tyre marks allocations: " + allocated + " bytes over 10000 ticks");
            Assert.That(allocated, Is.EqualTo(0L), "the mark tick allocated " + allocated + " bytes");
        }
    }
}
