using System.Collections;
using FestivalDerby.Driving.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// The visual is cosmetic: it must match the box it replaces, add nothing physical, and leave
    /// the collider the simulation actually uses exactly as the factory built it.
    /// </summary>
    public sealed class CarVisualAttacherPlayModeTests
    {
        private static readonly Pose Spawn = new Pose(new Vector3(2f, 1f, -3f), Quaternion.identity);
        // The model is a 4x1x8 m slab (two 4x1x4 m cubes), twice the car's 3 m length.
        private const float ModelLength = 8f;

        private CarDefinition _definition;
        private OwnedCarGeometry _geometry;
        private GameObject _model;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<CarDefinition>();
            _definition.bodySize = new Vector3(1.6f, 0.7f, 3f);
            _geometry = CarGeometryFactory.Create(_definition, Spawn);
            // Freeze the body so a yielded frame cannot drift the bounds being asserted.
            _geometry.Body.isKinematic = true;
            _model = new GameObject("Car visual test model");
            // Away from the car so the two cube colliders cannot touch the run floor meanwhile.
            _model.transform.position = new Vector3(100f, 100f, 100f);
            AddCube(_model.transform, new Vector3(0f, 0f, -2f));
            AddCube(_model.transform, new Vector3(0f, 0f, 2f));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_geometry != null)
                _geometry.Dispose();
            if (_model != null)
                Object.Destroy(_model);
            if (_definition != null)
                Object.Destroy(_definition);
            yield return null;
        }

        private static void AddCube(Transform parent, Vector3 localPosition)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = new Vector3(4f, 1f, 4f);
        }

        private static CarVisualEntry Entry(GameObject model)
            => new CarVisualEntry { Id = "Test", Model = model, Approved = true };

        private static Bounds AggregateBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        [UnityTest]
        public IEnumerator Attach_FitsModelToTheBox_HidesBoxFaces_AndAddsNothingPhysical()
        {
            var chassisBounds = _geometry.Chassis.bounds;
            var chassisSize = _geometry.Chassis.size;

            var visual = CarVisualAttacher.Attach(_geometry.Body.transform, _geometry.Chassis, Entry(_model));

            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.name, Is.EqualTo(CarVisualAttacher.VisualName));
            // The clone hangs under an identity VisualPivot child of the body: the pivot is the feel
            // juice's handle on the whole model, and the fitted clone itself is never moved by it.
            Transform pivot = visual.transform.parent;
            Assert.That(pivot.name, Is.EqualTo(CarVisualAttacher.VisualPivotName));
            Assert.That(pivot.parent, Is.SameAs(_geometry.Body.transform));
            Assert.That(pivot.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(pivot.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(pivot.localScale, Is.EqualTo(Vector3.one));
            float scale = visual.transform.localScale.x;
            Assert.That(visual.transform.localScale, Is.EqualTo(Vector3.one * scale));
            Assert.That(scale, Is.EqualTo(chassisBounds.size.z / ModelLength).Within(0.001f));

            // The measured bbox is exact here: the body and the model are both axis-aligned.
            var visualBounds = AggregateBounds(visual.transform);
            Assert.That(visualBounds.size.z, Is.EqualTo(chassisBounds.size.z).Within(0.05f));
            Assert.That(visualBounds.center.x, Is.EqualTo(chassisBounds.center.x).Within(0.01f));
            Assert.That(visualBounds.center.z, Is.EqualTo(chassisBounds.center.z).Within(0.01f));
            Assert.That(visualBounds.min.y, Is.EqualTo(chassisBounds.min.y).Within(0.01f),
                "The model's lowest point rides on the box bottom.");

            var boxRenderers = _geometry.Body.GetComponentsInChildren<Renderer>(true);
            int hidden = 0;
            foreach (var renderer in boxRenderers)
            {
                if (renderer.transform == visual.transform || renderer.transform.IsChildOf(visual.transform))
                {
                    Assert.That(renderer.enabled, Is.True);
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                    Assert.That(renderer.receiveShadows, Is.False);
                    continue;
                }

                Assert.That(renderer.enabled, Is.False, "The factory's box faces are replaced, not kept.");
                hidden++;
            }
            Assert.That(hidden, Is.EqualTo(6), "Chassis, cabin and four wheels.");

            // Collider destruction is deferred; one frame is enough for it to be gone.
            yield return null;

            Assert.That(visual.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(visual.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(_geometry.Chassis.size, Is.EqualTo(chassisSize));
            Assert.That(_geometry.Chassis.enabled, Is.True);
            foreach (var collider in _geometry.Body.GetComponentsInChildren<Collider>(true))
                Assert.That(collider == _geometry.Chassis || !collider.enabled, Is.True);
            Assert.That(_geometry.Body.isKinematic, Is.True);
        }

        [Test]
        public void Attach_AppliesTheEulerOffsetAndTheScaleMultiplier()
        {
            var entry = Entry(_model);
            entry.EulerOffset = new Vector3(0f, 90f, 0f);
            entry.ScaleMultiplier = 2f;

            var visual = CarVisualAttacher.Attach(_geometry.Body.transform, _geometry.Chassis, entry);

            Assert.That(Quaternion.Angle(visual.transform.localRotation, Quaternion.Euler(entry.EulerOffset)),
                Is.LessThan(0.001f));
            // Rotated, the model's 8 m axis lies across the body, so the z fit uses its 4 m extent.
            Assert.That(visual.transform.localScale.x, Is.EqualTo(3f * 2f / 4f).Within(0.001f));
        }

        [Test]
        public void Attach_MissingEntryOrModel_ChangesNothing()
        {
            int before = _geometry.Body.transform.childCount;

            Assert.That(CarVisualAttacher.Attach(_geometry.Body.transform, _geometry.Chassis, null), Is.Null);
            Assert.That(CarVisualAttacher.Attach(_geometry.Body.transform, _geometry.Chassis,
                new CarVisualEntry { Id = "Empty" }), Is.Null);

            Assert.That(_geometry.Body.transform.childCount, Is.EqualTo(before));
            foreach (var renderer in _geometry.Body.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.enabled, Is.True, "No model means no box face is hidden.");
        }

        [UnityTest]
        public IEnumerator Attach_VisualIsOwnedByTheBody_AndDiesWithIt()
        {
            var definition = ScriptableObject.CreateInstance<CarDefinition>();
            definition.bodySize = new Vector3(1.6f, 0.7f, 3f);
            var geometry = CarGeometryFactory.Create(definition, Spawn);
            var visual = CarVisualAttacher.Attach(geometry.Body.transform, geometry.Chassis, Entry(_model));
            var root = geometry.Root;

            geometry.Dispose();

            Assert.That(root.activeSelf, Is.False, "Dispose stops the geometry synchronously.");
            yield return null;

            Assert.That(root == null, Is.True);
            Assert.That(visual == null, Is.True, "The visual is a child of the body, so nothing tracks it separately.");
            Assert.That(geometry.Material == null, Is.True);
            Object.Destroy(definition);
        }
    }
}
