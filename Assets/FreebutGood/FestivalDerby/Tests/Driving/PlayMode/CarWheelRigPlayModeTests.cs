using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The wheel rig is a purely cosmetic re-parent: it must wrap the model's wheel nodes in FL, FR,
    /// RL, RR order without moving anything visually, spin them by ground speed over the measured
    /// radius, steer only the front pair, and leave a model without wheel nodes completely alone.
    /// </summary>
    public sealed class CarWheelRigPlayModeTests
    {
        private GameObject _visualRoot;
        private GameObject _rigHost;
        private CarWheelRig _rig;

        [TearDown]
        public void TearDown()
        {
            if (_visualRoot != null)
                Object.Destroy(_visualRoot);
            if (_rigHost != null)
                Object.Destroy(_rigHost);
        }

        private GameObject CreateWheel(float x, float z, string name)
        {
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wheel.name = name;
            wheel.transform.SetParent(_visualRoot.transform, false);
            wheel.transform.localPosition = new Vector3(x, 0f, z);
            return wheel;
        }

        private CarWheelRig BuildRig(float spinSign = 1f)
        {
            _rigHost = new GameObject("RigHost");
            _rig = _rigHost.AddComponent<CarWheelRig>();
            _rig.Build(_visualRoot.transform, null, spinSign);
            return _rig;
        }

        /// <summary>
        /// Where a point on the wheel's tread ends up in the car's own frame. The spin is judged from
        /// this instead of from an Euler value, because the axle a model is round about is the model's
        /// business and the requirement is only ever "the top of the wheel travels towards the nose".
        /// </summary>
        private static Vector3 TopOfWheel(Transform wheel, Transform visual)
            => visual.InverseTransformPoint(wheel.transform.TransformPoint(Vector3.up * 0.5f));

        [Test]
        public void Build_WrapsFourWheelNodes_InFrontLeftToRearRightOrder_WithoutMovingAnything()
        {
            _visualRoot = new GameObject("Visual");
            // Front is +z, left is -x.
            var fl = CreateWheel(-1f, 2f, "Wheel_Mesh_01");
            var fr = CreateWheel(1f, 2f, "Wheel_Mesh_02");
            var rl = CreateWheel(-1f, -2f, "Wheel_Mesh_03");
            var rr = CreateWheel(1f, -2f, "Wheel_Mesh_04");

            CarWheelRig rig = BuildRig();

            Assert.That(rig.WheelCount, Is.EqualTo(4));
            AssertPivotAt(rig.GetSteerPivot(0), -1f, 2f);
            AssertPivotAt(rig.GetSteerPivot(1), 1f, 2f);
            AssertPivotAt(rig.GetSteerPivot(2), -1f, -2f);
            AssertPivotAt(rig.GetSteerPivot(3), 1f, -2f);

            // The pivots sit at each node's renderer-bounds centre, and the nodes themselves never
            // moved: no FBX pivot is trusted, but nothing shifts either.
            AssertWheelStillAt(fl.transform, -1f, 2f);
            AssertWheelStillAt(fr.transform, 1f, 2f);
            AssertWheelStillAt(rl.transform, -1f, -2f);
            AssertWheelStillAt(rr.transform, 1f, -2f);
        }

        [Test]
        public void Apply_SpinsByGroundSpeedOverRadius_AndSteersOnlyTheFrontPair()
        {
            _visualRoot = new GameObject("Visual");
            CreateWheel(-1f, 2f, "Wheel_Mesh_01");
            CreateWheel(1f, 2f, "Wheel_Mesh_02");
            CreateWheel(-1f, -2f, "Wheel_Mesh_03");
            CreateWheel(1f, -2f, "Wheel_Mesh_04");
            CarWheelRig rig = BuildRig();

            // Unit cubes: every wheel's renderer-bounds radius is 0.5 m. 2 m/s for 0.1 s rolls 0.2 m
            // of tread, which is 22.9183 degrees about the axle; positive speed rolls forward.
            var readout = new CarHandlingReadout(2f, 2f, 0f, 0f, false, 0, 0f, 0f, 0f, 5f, 4, default);
            Transform wheel = rig.GetSpinPivot(0).GetChild(0);
            Vector3 topBefore = TopOfWheel(wheel, _visualRoot.transform);
            rig.Apply(readout, 0.1f);
            Vector3 topAfter = TopOfWheel(wheel, _visualRoot.transform);

            float expectedSpinDegrees = 2f / 0.5f * Mathf.Rad2Deg * 0.1f;
            var spinRotation = Quaternion.Euler(expectedSpinDegrees, 0f, 0f);
            Assert.That(Quaternion.Angle(rig.GetSpinPivot(0).localRotation, spinRotation), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(rig.GetSpinPivot(3).localRotation, spinRotation), Is.LessThan(0.01f));
            Assert.That(topAfter.z, Is.GreaterThan(topBefore.z),
                "A wheel rolls forward when the car does: its top travels towards the nose.");

            // A positive core steer angle is a LEFT turn, because the handling core's yaw is the mirror
            // of Unity's. Asserted against the car's own right axis rather than as an Euler value: the
            // sign is the whole point, and pointing the wheels away from the corner was the defect.
            Assert.That(Vector3.Dot(rig.GetSteerPivot(0).forward, _visualRoot.transform.right), Is.LessThan(0f),
                "A positive core steer points the front wheels at the car's left, not at its right.");
            Assert.That(Vector3.Dot(rig.GetSteerPivot(1).forward, _visualRoot.transform.right), Is.LessThan(0f));
            Assert.That(Quaternion.Angle(rig.GetSteerPivot(2).localRotation, Quaternion.identity), Is.LessThan(0.01f),
                "The rear pair never steers.");
            Assert.That(Quaternion.Angle(rig.GetSteerPivot(3).localRotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void Apply_RollsForward_EvenWhenTheWheelNodeIsMirrored()
        {
            _visualRoot = new GameObject("Visual");
            // An exporter's wheel node can carry a half turn, which is the one rotation that flips the
            // mesh's own axle inside the car's frame; the roll still has to come out forwards.
            var fl = CreateWheel(-1f, 2f, "Wheel_Mesh_01");
            CreateWheel(1f, 2f, "Wheel_Mesh_02");
            CreateWheel(-1f, -2f, "Wheel_Mesh_03");
            CreateWheel(1f, -2f, "Wheel_Mesh_04");
            fl.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            CarWheelRig rig = BuildRig();

            Transform wheel = rig.GetSpinPivot(0).GetChild(0);
            Vector3 topBefore = TopOfWheel(wheel, _visualRoot.transform);
            rig.Apply(new CarHandlingReadout(2f, 2f, 0f, 0f, false, 0, 0f, 0f, 0f, 0f, 4, default), 0.1f);
            Vector3 topAfter = TopOfWheel(wheel, _visualRoot.transform);

            Assert.That(topAfter.z, Is.GreaterThan(topBefore.z));
        }

        [Test]
        public void Apply_HonoursTheModelsOwnSpinSign()
        {
            _visualRoot = new GameObject("Visual");
            var fl = CreateWheel(-1f, 2f, "Wheel_Mesh_01");
            CreateWheel(1f, 2f, "Wheel_Mesh_02");
            CreateWheel(-1f, -2f, "Wheel_Mesh_03");
            CreateWheel(1f, -2f, "Wheel_Mesh_04");
            // The catalogue's per-model knob: a negative sign is the owner saying this model's wheels
            // roll the other way round the same axle, and only this model is affected.
            CarWheelRig rig = BuildRig(-1f);

            Transform wheel = rig.GetSpinPivot(0).GetChild(0);
            Vector3 topBefore = TopOfWheel(wheel, _visualRoot.transform);
            rig.Apply(new CarHandlingReadout(2f, 2f, 0f, 0f, false, 0, 0f, 0f, 0f, 0f, 4, default), 0.1f);
            Vector3 topAfter = TopOfWheel(wheel, _visualRoot.transform);

            Assert.That(fl.transform.parent.parent, Is.SameAs(rig.GetSteerPivot(0)));
            Assert.That(topAfter.z, Is.LessThan(topBefore.z));
        }

        [Test]
        public void Apply_WithoutWheelNodes_LeavesTheTreeAlone()
        {
            _visualRoot = new GameObject("Visual");
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(_visualRoot.transform, false);
            panel.transform.localPosition = new Vector3(0f, 1f, 0f);
            CarWheelRig rig = BuildRig();

            Assert.That(rig.WheelCount, Is.EqualTo(0));
            rig.Apply(default, 0.1f);

            Assert.That(_visualRoot.transform.childCount, Is.EqualTo(1));
            Assert.That(panel.transform.parent, Is.SameAs(_visualRoot.transform));
            Assert.That(panel.transform.localPosition, Is.EqualTo(new Vector3(0f, 1f, 0f)));
        }

        private static void AssertPivotAt(Transform pivot, float x, float z)
        {
            Assert.That(pivot, Is.Not.Null);
            Assert.That(pivot.localPosition.x, Is.EqualTo(x).Within(0.001f));
            Assert.That(pivot.localPosition.z, Is.EqualTo(z).Within(0.001f));
            Assert.That(pivot.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(pivot.localScale, Is.EqualTo(Vector3.one));
        }

        private static void AssertWheelStillAt(Transform wheel, float x, float z)
        {
            Assert.That(wheel.position.x, Is.EqualTo(x).Within(0.001f));
            Assert.That(wheel.position.z, Is.EqualTo(z).Within(0.001f));
        }
    }
}
