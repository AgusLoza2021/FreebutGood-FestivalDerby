using System;
using System.Collections;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// G3b.1 RED fixture: the 3/4 chase camera against its inert skeleton. Every case measures
    /// the camera transform against a point the test itself computes from the target transform,
    /// so an unimplemented or inert surface must fail with a real assertion; the stop case
    /// asserts the absence of motion and is expected to pass vacuously in RED, recorded as
    /// non-discriminating, never as coverage.
    /// </summary>
    public sealed class LocalChaseCameraPlayModeTests
    {
        // World-space framing must stay independent of target yaw.
        private static readonly Vector3 Offset = new Vector3(0f, 4f, -7f);
        private const float LookHeight = 2f;
        private const float Sharpness = 10f;
        private const float SnapTolerance = 0.001f;
        private const float SettledTolerance = 0.05f;
        // The task's contract for a borrowed view is "1 cm": its world pose must land on the
        // framing, not merely somewhere near it.
        private const float FramingTolerance = 0.01f;

        private GameObject _cameraHost;
        private GameObject _target;

        [SetUp]
        public void SetUp()
        {
            _cameraHost = new GameObject("chase camera host");
            _target = new GameObject("chase camera target");
        }

        [TearDown]
        public void TearDown()
        {
            if (_cameraHost != null) Object.Destroy(_cameraHost);
            if (_target != null) Object.Destroy(_target);
            _cameraHost = null;
            _target = null;
        }

        // Configure is the surface the tuning arrives through, so every case sets the offset
        // through it even while the skeleton ignores it; expectations stay contract-shaped.
        private LocalChaseCamera CreateCamera(Vector3 hostPosition)
        {
            _cameraHost.transform.position = hostPosition;
            var camera = _cameraHost.AddComponent<LocalChaseCamera>();
            camera.Configure(Offset, LookHeight, Sharpness);
            return camera;
        }

        // Bounded settle loop: leaves once the camera has arrived. The real horizon is the
        // simulated two seconds below; the frame cap exists only so a stopped clock cannot hang
        // the loop, and it has to sit far above what a batchmode frame needs. This runner
        // advances time at roughly 9e-5 s per frame -- some 200x less than a device frame -- so
        // a bound tuned on device frames cuts the wait off mid-approach and fails a correct
        // camera: the first GREEN attempt closed only ~41% of the gap in its 600 frames.
        private static IEnumerator WaitUntilWithinTolerance(Transform cameraTransform, Vector3 desiredPoint)
        {
            float elapsed = 0f;
            for (int frame = 0; frame < 50000 && elapsed < 2f; frame++)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (Vector3.Distance(cameraTransform.position, desiredPoint) <= SettledTolerance)
                    yield break;
            }
        }

        // Per-axis like the geometry fixture: no whole-Vector3 comparer dependency.
        private static void AssertPositionWithin(Transform cameraTransform, Vector3 expected, float tolerance, string message)
        {
            for (int axis = 0; axis < 3; axis++)
                Assert.That(cameraTransform.position[axis], Is.EqualTo(expected[axis]).Within(tolerance), message);
        }

        [UnityTest]
        public IEnumerator Follow_RejectsANullTarget_BeforeMutatingAnyState()
        {
            var camera = CreateCamera(new Vector3(0f, 0f, 8f));

            Assert.Throws<ArgumentNullException>(() => camera.Follow(null),
                "A null target must be rejected with ArgumentNullException before any camera state changes.");
            Assert.That(camera.IsFollowing, Is.False,
                "A rejected follow must leave the camera in its not-following state.");

            yield return null;
            Assert.That(camera.IsFollowing, Is.False,
                "A rejected follow must still be not-following after a real frame.");
        }

        [UnityTest]
        public IEnumerator Follow_SnapsToTheOffset_InsteadOfDriftingInFromTheOrigin()
        {
            Vector3 hostPosition = new Vector3(50f, 0f, 0f);
            var camera = CreateCamera(hostPosition);
            Vector3 expected = _target.transform.position + Offset;
            Assert.That(Vector3.Distance(hostPosition, expected), Is.GreaterThan(10f),
                "Setup proof: the host must start far from the framed point, or a snap could not be told from a drift.");

            camera.Follow(_target.transform);
            yield return null;

            AssertPositionWithin(camera.transform, expected, SnapTolerance,
                "The first observed frame must find the camera snapped to the world-space offset, not partway there.");
        }

        [UnityTest]
        public IEnumerator LateUpdate_ConvergesOnATargetThatMoves()
        {
            var camera = CreateCamera(new Vector3(50f, 0f, 0f));
            camera.Follow(_target.transform);
            yield return WaitUntilWithinTolerance(camera.transform, _target.transform.position + Offset);

            _target.transform.position += new Vector3(5f, 0f, 0f);
            Vector3 desired = _target.transform.position + Offset;
            float distanceBefore = Vector3.Distance(camera.transform.position, desired);
            Assert.That(distanceBefore, Is.GreaterThan(1f),
                "Setup proof: the teleport must actually move the desired point away from the camera, or the convergence proves nothing.");

            // Since a29066a3 the chase is ChaseFramingModel: a critically damped spring that measures each
            // step's error against where the target WAS when the previous step was solved, and feeds
            // forward only a velocity it can trust. This runner's frame is ~1e-4 s, so a 5 m jump reads
            // as tens of thousands of m/s, is discarded as a broken reading (MaxFeedForwardSpeed), and
            // reaches the spring as error one frame later. The old "strictly closer on the very first
            // frame" asserted the first-order follower's response, not a requirement: the contract is
            // never moving away, closing by the next frame, no overshoot, and settling on the point.
            yield return null;
            float teleportFrame = Vector3.Distance(camera.transform.position, desired);
            Assert.That(teleportFrame, Is.LessThanOrEqualTo(distanceBefore + SnapTolerance),
                "The frame the target teleports on must never move the camera away from the new point.");

            yield return null;
            float distanceAfter = Vector3.Distance(camera.transform.position, desired);
            Assert.That(distanceAfter, Is.LessThan(distanceBefore),
                "By the frame after the teleport the camera must be strictly closer: convergence, never a one-frame exact arrival.");
            Assert.That(distanceAfter, Is.GreaterThan(SettledTolerance),
                "A 5 m gap closed in two frames would be a snap, which only a gap past the snap distance earns.");

            // Critically damped from rest: the gap shrinks monotonically, it never rings past the point.
            float previous = distanceAfter;
            float elapsed = 0f;
            for (int frame = 0; frame < 50000 && elapsed < 2f && previous > SettledTolerance; frame++)
            {
                yield return null;
                elapsed += Time.deltaTime;
                float now = Vector3.Distance(camera.transform.position, desired);
                Assert.That(now, Is.LessThanOrEqualTo(previous + SnapTolerance),
                    "A critically damped chase must close the gap monotonically, never overshoot and come back.");
                previous = now;
            }

            Assert.That(Vector3.Distance(camera.transform.position, desired), Is.LessThanOrEqualTo(SettledTolerance),
                "The chase camera must converge onto the moved desired point within a bounded horizon.");
        }

        /// <summary>
        /// The a29066a3 contract that the first-order follower could not meet: a target at constant
        /// velocity is framed with zero steady-state lag. The old chase settled v/sharpness metres
        /// behind (1 m at 10 m/s and sharpness 10); the critically damped spring with the velocity fed
        /// forward settles on the target plus the look-ahead lead, which leads, never trails.
        /// </summary>
        [UnityTest]
        public IEnumerator LateUpdate_FramesATargetAtConstantVelocity_WithNoSteadyStateLag()
        {
            const float speed = 10f;
            const float leadPerSpeed = 0.08f;
            const float leadMax = 1.8f;
            var camera = CreateCamera(_target.transform.position + Offset);
            camera.ConfigureLookAhead(leadPerSpeed, leadMax, 5f, null);
            camera.Follow(_target.transform);
            yield return null;

            // A test coroutine resumes after Update and before LateUpdate, so the target moves with the
            // same frame's delta the camera's LateUpdate divides by: the position-delta velocity is then
            // exactly the speed, not a ratio of two frames. Simulated time bounds the loop, because this
            // runner's frames are ~1e-4 s.
            float elapsed = 0f;
            for (int frame = 0; frame < 200000 && elapsed < 3f; frame++)
            {
                yield return null;
                elapsed += Time.deltaTime;
                _target.transform.position += new Vector3(speed * Time.deltaTime, 0f, 0f);
            }

            Assert.That(elapsed, Is.GreaterThanOrEqualTo(3f), "Setup proof: the target must have moved for 3 s.");
            Vector3 framed = _target.transform.position + Offset;
            float lead = camera.transform.position.x - framed.x;
            // Bounded, not exact: in this runner's ~1e-4 s frames the eased lead read 0.63 of its 0.8 m
            // target after 3 s. The contract is the sign: the old follower settled v/sharpness = 1 m
            // BEHIND (lead -1), the spring with feed-forward sits on the point or ahead of it.
            Assert.That(lead, Is.InRange(-0.05f, Mathf.Min(speed * leadPerSpeed, leadMax) + 0.05f),
                "At constant velocity the camera must sit on the target plus at most its lead (zero steady-state lag), not behind it.");
            Assert.That(camera.transform.position.y, Is.EqualTo(framed.y).Within(0.05f));
            Assert.That(camera.transform.position.z, Is.EqualTo(framed.z).Within(0.05f));
        }

        [UnityTest]
        public IEnumerator LateUpdate_AimsAtTheLookPoint_RatherThanKeepingItsOwnRotation()
        {
            // Host starts exactly on the framed point, so this case isolates the rotation from
            // the position: only the aim can make it fail.
            var camera = CreateCamera(_target.transform.position + Offset);
            camera.Follow(_target.transform);
            yield return WaitUntilWithinTolerance(camera.transform, _target.transform.position + Offset);

            // Bearing comes from framing geometry, never from the camera's current follow lag.
            Vector3 toLook = Vector3.up * LookHeight - Offset;
            Assert.That(toLook, Is.Not.EqualTo(Vector3.zero),
                "Setup proof: the camera must not sit on the look point, or the aim check proves nothing.");
            float alignment = Vector3.Dot(camera.transform.forward, toLook.normalized);
            Assert.That(alignment, Is.GreaterThanOrEqualTo(0.999f),
                "The camera's forward must point at the look point instead of keeping its own rotation.");

            Assert.That(Quaternion.Angle(_target.transform.rotation, Quaternion.identity), Is.EqualTo(0f),
                "The camera must never write the target's rotation while aiming.");
        }

        [UnityTest]
        public IEnumerator Stop_LeavesTheCameraWhereItIs_AndIgnoresLaterTargetMovement()
        {
            var camera = CreateCamera(new Vector3(50f, 0f, 0f));
            camera.Follow(_target.transform);
            yield return WaitUntilWithinTolerance(camera.transform, _target.transform.position + Offset);

            Assert.DoesNotThrow(() => camera.Stop(),
                "Stop must end the following state without throwing.");
            Assert.DoesNotThrow(() => camera.Stop(),
                "A second Stop must be idempotent, never an exception.");
            Assert.That(camera.IsFollowing, Is.False,
                "After Stop the camera must report that it is no longer following.");

            Vector3 positionAtStop = camera.transform.position;
            _target.transform.position += new Vector3(25f, 0f, 0f);
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            AssertPositionWithin(camera.transform, positionAtStop, SnapTolerance,
                "After Stop, later target movement must leave the camera transform exactly where it was.");
        }

        [UnityTest]
        public IEnumerator Follow_WorldDiagonalFraming_IgnoresTargetYaw()
        {
            Vector3 worldOffset = new Vector3(-5f, 6f, -8f);
            _target.transform.SetPositionAndRotation(new Vector3(3f, 1f, 4f), Quaternion.Euler(0f, 90f, 0f));
            var camera = CreateCamera(new Vector3(50f, 0f, 0f));
            camera.Configure(worldOffset, LookHeight, Sharpness);
            Vector3 expected = _target.transform.position + worldOffset;
            Vector3 toLook = Vector3.up * LookHeight - worldOffset;
            for (int axis = 0; axis < 3; axis++)
                Assert.That(float.IsNaN(expected[axis]) || float.IsInfinity(expected[axis]) ||
                    float.IsNaN(toLook[axis]) || float.IsInfinity(toLook[axis]), Is.False, "Framing geometry must be finite.");
            Assert.That(toLook.sqrMagnitude, Is.GreaterThan(1f), "The aim must have a nondegenerate direction.");
            Assert.That(Vector3.Distance(expected, _target.transform.TransformPoint(worldOffset)), Is.GreaterThan(1f),
                "Initial yaw must distinguish world framing from the obsolete target-local framing.");
            Quaternion expectedRotation = Quaternion.LookRotation(toLook, Vector3.up);

            camera.Follow(_target.transform);
            AssertPositionWithin(camera.transform, expected, SnapTolerance, "Follow must snap to the world-diagonal offset.");
            Assert.That(Quaternion.Angle(camera.transform.rotation, expectedRotation), Is.LessThanOrEqualTo(0.01f));
            _target.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            yield return null;
            AssertPositionWithin(camera.transform, expected, SnapTolerance, "Player yaw must not orbit the camera.");
            Assert.That(Quaternion.Angle(camera.transform.rotation, expectedRotation), Is.LessThanOrEqualTo(0.01f));

            _target.transform.position += new Vector3(4f, 0f, 3f);
            expected = _target.transform.position + worldOffset;
            Assert.That(Vector3.Distance(camera.transform.position, expected), Is.GreaterThan(1f),
                "Translation must create a real follow gap before bounded convergence.");
            yield return WaitUntilWithinTolerance(camera.transform, expected);
            Assert.That(Vector3.Distance(camera.transform.position, expected), Is.LessThanOrEqualTo(SettledTolerance));
            Assert.That(Quaternion.Angle(camera.transform.rotation, expectedRotation), Is.LessThanOrEqualTo(0.5f),
                "Settled follow must retain the world bearing and elevated aim, independently of player yaw.");
        }

        [UnityTest]
        public IEnumerator LocalOffset_IsWorldSpace_SoTargetYawDoesNotOrbitCamera()
        {
            var camera = CreateCamera(new Vector3(50f, 0f, 0f));
            camera.Follow(_target.transform);
            yield return WaitUntilWithinTolerance(camera.transform, _target.transform.position + Offset);
            Vector3 positionBeforeYaw = camera.transform.position;

            _target.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Vector3 expected = _target.transform.position + Offset;
            Assert.That(Vector3.Distance(positionBeforeYaw, expected), Is.LessThanOrEqualTo(SnapTolerance),
                "Yaw alone must leave the world-space framed point unchanged.");

            yield return WaitUntilWithinTolerance(camera.transform, expected);
            AssertPositionWithin(camera.transform, expected, SettledTolerance,
                "After the target yaws, the camera must retain its world-space offset rather than orbit behind the car.");
        }

        private Transform CreateNestedRenderView(string name)
        {
            var rotation = new GameObject(name + " rotation").transform;
            rotation.SetParent(_cameraHost.transform, false);
            rotation.localPosition = new Vector3(0f, 0f, -30f);
            rotation.localRotation = Quaternion.Euler(33f, 0f, 0f);
            var view = new GameObject(name).transform;
            view.SetParent(rotation, false);
            view.localPosition = new Vector3(1f, 2f, -3f);
            view.localRotation = Quaternion.Euler(4f, 7f, 2f);
            return view;
        }

        [Test]
        public void FollowNestedRenderViewFramesActualViewIndependentlyOfTargetYaw()
        {
            var camera = CreateCamera(new Vector3(50f, 3f, 8f));
            camera.transform.rotation = Quaternion.Euler(7f, 25f, 3f);
            var view = CreateNestedRenderView("render view");
            Vector3 authoredLocalPosition = view.localPosition;
            Quaternion authoredLocalRotation = view.localRotation;
            Vector3 offset = new Vector3(-12f, 11f, -8f);
            camera.Configure(offset, LookHeight, Sharpness);
            _target.transform.position = new Vector3(3f, 1f, 4f);
            for (int yaw = 90; yaw <= 180; yaw += 90)
            {
                _target.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                camera.Follow(_target.transform, view);
                AssertPositionWithin(view, _target.transform.position + offset, FramingTolerance,
                    "The actual nested view's world pose must land on the configured framing, whatever the intermediate nodes.");
                Assert.That(Vector3.Dot(view.forward, new Vector3(12f, -9f, 8f).normalized), Is.GreaterThanOrEqualTo(0.999f));
                Assert.That(Vector3.Distance(view.localPosition, authoredLocalPosition), Is.LessThanOrEqualTo(SnapTolerance),
                    "The chase must never write the borrowed view's local position: that pose is the shaker's captured base.");
                Assert.That(Quaternion.Angle(view.localRotation, authoredLocalRotation), Is.LessThanOrEqualTo(0.01f),
                    "The chase must never write the borrowed view's local rotation either.");
            }
        }

        [UnityTest]
        public IEnumerator ShakeOnTheBorrowedView_StaysRelative_AndNeverMovesTheFramingRoot()
        {
            var camera = CreateCamera(new Vector3(50f, 3f, 8f));
            camera.transform.rotation = Quaternion.Euler(7f, 25f, 3f);
            var view = CreateNestedRenderView("shaken view");
            Vector3 authoredLocalPosition = view.localPosition;
            Quaternion authoredLocalRotation = view.localRotation;
            Vector3 offset = new Vector3(-12f, 11f, -8f);
            camera.Configure(offset, LookHeight, Sharpness);
            _target.transform.position = new Vector3(3f, 1f, 4f);
            camera.Follow(_target.transform, view);
            yield return null;

            AssertPositionWithin(view, _target.transform.position + offset, FramingTolerance,
                "One frame after Follow the borrowed view's world pose must still sit on the framing.");
            Assert.That(Vector3.Distance(view.localPosition, authoredLocalPosition), Is.LessThanOrEqualTo(FramingTolerance),
                "The chase must leave the view's local position alone so a shaker on the view keeps its startup base.");
            Assert.That(Quaternion.Angle(view.localRotation, authoredLocalRotation), Is.LessThanOrEqualTo(0.01f),
                "The chase must leave the view's local rotation alone too.");

            Vector3 rootPosition = camera.transform.position;
            Quaternion rootRotation = camera.transform.rotation;

            // One MMWiggle frame writes transform.localPosition = captured base + noise; the chase's
            // LateUpdate then runs and must derive the framing from its captured offset, not from
            // the shaking view, or the root would be dragged along and the shake would be erased.
            Vector3 noise = new Vector3(0.4f, -0.3f, 0.25f);
            view.localPosition = authoredLocalPosition + noise;
            yield return null;

            Assert.That(Vector3.Distance(view.localPosition, authoredLocalPosition + noise), Is.LessThanOrEqualTo(SnapTolerance),
                "Setup proof: the simulated shake must still be on the view after the frame, or nothing was shaken.");
            AssertPositionWithin(camera.transform, rootPosition, FramingTolerance,
                "A shake on the view must not move the framing root: that coupling is what pinned the camera before.");
            Assert.That(Quaternion.Angle(camera.transform.rotation, rootRotation), Is.LessThanOrEqualTo(0.01f),
                "A shake on the view must not rotate the framing root either.");
        }

        [Test]
        public void StopRestoresBorrowedViewRigRootIdempotentlyWithoutWritingTheView()
        {
            Vector3 offset = new Vector3(-12f, 11f, -8f);
            var camera = CreateCamera(_target.transform.position + offset);
            camera.Configure(offset, LookHeight, Sharpness);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(12f, -9f, 8f), Vector3.up);
            Vector3 rootPosition = camera.transform.position;
            Quaternion rootRotation = camera.transform.rotation;
            var view = CreateNestedRenderView("borrowed view");
            Vector3 localPosition = view.localPosition;
            Quaternion localRotation = view.localRotation;
            camera.Follow(_target.transform, view);
            Assert.That(Vector3.Distance(camera.transform.position, rootPosition), Is.GreaterThan(SnapTolerance),
                "Setup proof: framing a borrowed view must move the rig root, or the restore would prove nothing.");
            for (int stop = 0; stop < 2; stop++)
            {
                camera.Stop();
                Assert.That(Vector3.Distance(view.localPosition, localPosition), Is.LessThanOrEqualTo(SnapTolerance),
                    "Stop must not write the borrowed view's local pose; its shaker owns that base.");
                Assert.That(Quaternion.Angle(view.localRotation, localRotation), Is.LessThanOrEqualTo(0.01f));
                Assert.That(camera.IsFollowing, Is.False);
                AssertPositionWithin(camera.transform, rootPosition, SnapTolerance, "Stop must put the borrowed view's rig root back where Follow found it.");
                Assert.That(Quaternion.Angle(camera.transform.rotation, rootRotation), Is.LessThanOrEqualTo(0.01f));
            }
        }

        [Test]
        public void RetargetingRenderViewReframesTheNewViewWithoutTouchingEitherView()
        {
            var camera = CreateCamera(new Vector3(50f, 3f, 8f));
            var viewA = CreateNestedRenderView("view A");
            var viewB = CreateNestedRenderView("view B");
            Vector3 localPositionA = viewA.localPosition;
            Quaternion localRotationA = viewA.localRotation;
            Vector3 localPositionB = viewB.localPosition;
            Vector3 offset = new Vector3(-12f, 11f, -8f);
            camera.Configure(offset, LookHeight, Sharpness);
            _target.transform.position = new Vector3(3f, 1f, 4f);
            camera.Follow(_target.transform, viewA);
            camera.Follow(_target.transform, viewB);
            Assert.That(Vector3.Distance(viewA.localPosition, localPositionA), Is.LessThanOrEqualTo(SnapTolerance));
            Assert.That(Quaternion.Angle(viewA.localRotation, localRotationA), Is.LessThanOrEqualTo(0.01f));
            AssertPositionWithin(viewB, _target.transform.position + offset, FramingTolerance,
                "The newly borrowed actual view must receive the framing after the previous view is released.");
            Assert.That(Vector3.Dot(viewB.forward, new Vector3(12f, -9f, 8f).normalized), Is.GreaterThanOrEqualTo(0.999f));
            Assert.That(Vector3.Distance(viewB.localPosition, localPositionB), Is.LessThanOrEqualTo(SnapTolerance),
                "The newly borrowed view keeps its authored local pose as well.");
        }

        private sealed class FakeHeading : IChaseHeadingSource
        {
            public float Yaw;

            public bool TryGetHeading(Vector3 position, out float yawDegrees)
            {
                yawDegrees = Yaw;
                return true;
            }
        }

        [Test]
        public void TrackYaw_SwingsTheOffsetAroundTheCarByHowFarTheRoadTurned()
        {
            var camera = CreateCamera(Vector3.zero);
            _target.transform.position = new Vector3(3f, 0f, 4f);
            camera.Follow(_target.transform);
            var heading = new FakeHeading { Yaw = 30f };
            camera.ConfigureYaw(heading, true, 0f, 0f);

            heading.Yaw = 120f; // the road turned 90 degrees right since the start
            camera.Tick(0.1f);

            Vector3 expected = _target.transform.position + Quaternion.Euler(0f, 90f, 0f) * Offset;
            AssertPositionWithin(camera.transform, expected, FramingTolerance,
                "The camera keeps its height and distance and orbits by the road's turn.");
            Vector3 toLook = _target.transform.position + Vector3.up * LookHeight - camera.transform.position;
            Assert.That(Vector3.Dot(camera.transform.forward, toLook.normalized), Is.GreaterThanOrEqualTo(0.999f),
                "The camera keeps aiming at the car's look point while it orbits.");
        }

        [Test]
        public void TrackYaw_CapsTheSwingSpeedSoAHairpinPansInsteadOfWhipping()
        {
            var camera = CreateCamera(Vector3.zero);
            _target.transform.position = Vector3.zero;
            camera.Follow(_target.transform);
            var heading = new FakeHeading { Yaw = 0f };
            camera.ConfigureYaw(heading, true, 0f, 90f);

            heading.Yaw = 170f;
            camera.Tick(0.5f);

            Vector3 expected = Quaternion.Euler(0f, 45f, 0f) * Offset;
            AssertPositionWithin(camera.transform, expected, FramingTolerance,
                "Half a second at 90 deg/s is 45 degrees, however far the road turned.");
        }

        [Test]
        public void TrackYaw_NullSourceKeepsTheFixedWorldBearing()
        {
            var camera = CreateCamera(Vector3.zero);
            _target.transform.position = new Vector3(-2f, 0f, 6f);
            camera.Follow(_target.transform);
            camera.ConfigureYaw(null, true, 2.5f, 90f);

            camera.Tick(0.1f);

            AssertPositionWithin(camera.transform, _target.transform.position + Offset, FramingTolerance,
                "Without a heading source the offset stays in world space, exactly as before.");
        }

        [Test]
        public void ChaseYaw_SitsBehindTheCarAndAimsAtThePivotAhead()
        {
            var camera = CreateCamera(Vector3.zero);
            Vector3 behind = new Vector3(0f, 9f, -14f);
            const float pivotAhead = 6f;
            camera.Configure(behind, LookHeight, Sharpness);
            _target.transform.position = new Vector3(10f, 0f, -5f);
            camera.Follow(_target.transform);
            // The car travels toward +X (yaw 90), so "behind" is -X.
            camera.ConfigureYaw(new FakeHeading { Yaw = 90f }, false, 0f, 0f);
            camera.ConfigurePivotAhead(pivotAhead);

            camera.Tick(0.1f);

            Vector3 expected = _target.transform.position + Quaternion.Euler(0f, 90f, 0f) * behind;
            AssertPositionWithin(camera.transform, expected, FramingTolerance,
                "Chase mode treats the offset as car-local: -Z is behind the car's heading.");
            Vector3 pivot = _target.transform.position + Vector3.up * LookHeight + Vector3.right * pivotAhead;
            Vector3 toPivot = (pivot - camera.transform.position).normalized;
            Assert.That(Vector3.Dot(camera.transform.forward, toPivot), Is.GreaterThanOrEqualTo(0.999f),
                "The camera aims at the pivot ahead of the car, not at the car itself.");
        }

        [Test]
        public void ChaseYaw_LagsBehindATurnSoTheCameraSwingsWide()
        {
            var camera = CreateCamera(Vector3.zero);
            Vector3 behind = new Vector3(0f, 9f, -14f);
            camera.Configure(behind, LookHeight, Sharpness);
            _target.transform.position = Vector3.zero;
            camera.Follow(_target.transform);
            var heading = new FakeHeading { Yaw = 0f };
            camera.ConfigureYaw(heading, false, 3f, 0f);

            heading.Yaw = 90f; // the car turned right
            camera.Tick(0.1f);

            float swung = Vector3.SignedAngle(Vector3.back, new Vector3(camera.transform.position.x, 0f,
                camera.transform.position.z), Vector3.up);
            Assert.That(swung, Is.GreaterThan(5f).And.LessThan(85f),
                "One frame after a right turn the camera is part-way round, still outside the corner.");
        }
    }
}
