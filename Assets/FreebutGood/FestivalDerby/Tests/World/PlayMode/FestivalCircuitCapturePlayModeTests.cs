using System.Collections;
using System.Collections.Generic;
using System.IO;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Writes the frames that judge the Festival Circuit: the whole layout from above, and the two
    /// corners the brief names (the hairpin and the chicane) from the camera the game drives with.
    ///
    /// The rig does not go through the Gameplay scene. That scene's boot belongs to another work stream
    /// and is currently broken - its smoke tests fail with "Active HUDs: ... GarageHud", which is why
    /// the HUD is not the menu a capture rig could press PLAY on - and a rig that cannot boot cannot
    /// produce evidence. So the frames are built from the shipped map itself: the road, its kerbs and
    /// its walls as MapLoader builds them, a real car geometry on the corner, and the map's own camera
    /// override. Everything in the frames is production data; only the light and the camera placement
    /// belong to the rig, and both are named in the log.
    ///
    /// Explicit because it exists to produce evidence, not to gate anything.
    /// </summary>
    [Explicit]
    public sealed class FestivalCircuitCapturePlayModeTests
    {
        private const string MapResource = "Maps/Map_FestivalCircuit";
        private const string CarResource = "CarDefinition";

        // Project-root relative: the Editor's working directory is the project root, and Temp is
        // outside Assets so these frames never enter the asset database. A copy also goes to the
        // machine's temp folder, because a batchmode Editor deletes the whole project Temp folder when
        // it quits: that copy is what survives the run that produced it.
        private const string OutputFolder = "Temp/agent-cs5";
        private const string DurableOutputFolder = "festivalderby-agent-cs5";
        private const int FrameWidth = 1600;
        private const int FrameHeight = 900;
        /// <summary>The race camera's field of view; the map override carries the offset, not the lens.</summary>
        private const float RaceFieldOfView = 30f;

        // Route metres of the two corners the brief names, read off the element walk that authored the
        // layout: the R20 hairpin runs 207.6 m -> 270.4 m, the -50/+100/-50 chicane 313.8 m -> 418.5 m.
        // Each shot is taken mid-corner, so the road ahead is the corner itself.
        private const float HairpinMeters = 240f;
        private const float ChicaneMeters = 366f;

        private GameObject _anchor;
        private LoadedMap _loaded;
        private MapAsset _map;
        private BakedPath _line;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Application.runInBackground = true;

            _map = Resources.Load<MapAsset>(MapResource);
            Assert.IsNotNull(_map, MapResource + " must ship in Resources");
            _line = _map.RacingLine != null && _map.RacingLine.IsUsable ? _map.RacingLine : _map.PrimaryRoad.Path;
            Assert.IsNotNull(_line, "the map must expose a racing line");
            Assert.IsTrue(_line.IsUsable, "the circuit's baked path must be usable");

            _anchor = new GameObject("circuit-capture-anchor");
            _loaded = MapLoader.Load(_map, false, _anchor.transform);

            // One light, so the asphalt is readable; an empty test scene has no sky and no ambient of
            // its own. It is the rig's, not the game's, and it is logged as such.
            var light = new GameObject("capture-light");
            light.transform.SetParent(_anchor.transform, false);
            var sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1f;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // First, before anything can take the map root down: the baked chunk meshes are project
            // assets and RoadMeshOwner would destroy whatever it still owns.
            RoadMeshOwner[] owners = _anchor != null
                ? _anchor.GetComponentsInChildren<RoadMeshOwner>(true)
                : new RoadMeshOwner[0];
            for (int i = 0; i < owners.Length; i++)
                owners[i].Own(null);

            if (_loaded != null)
            {
                _loaded.Dispose();
                _loaded = null;
            }

            if (_anchor != null)
                Object.DestroyImmediate(_anchor);
            _anchor = null;

            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureOverviewAndCorners()
        {
            Directory.CreateDirectory(Path.GetFullPath(OutputFolder));
            Directory.CreateDirectory(DurableFolder());
            yield return null;

            SaveOverview();
            yield return CaptureCorner("circuit-hairpin.png", HairpinMeters);
            yield return CaptureCorner("circuit-chicane.png", ChicaneMeters);

            // Two plan frames as well as the two chase frames: a chase frame shows the corner the way a
            // driver sees it, which is not the way a layout is judged.
            SavePlan("circuit-hairpin-plan.png", HairpinMeters, 90f);
            SavePlan("circuit-chicane-plan.png", ChicaneMeters, 90f);
        }

        /// <summary>
        /// A straight-down orthographic frame covering the road's own bounds, wide enough that no part
        /// of the circuit is cropped: the layout is what this frame is for.
        /// </summary>
        private void SaveOverview()
        {
            var bounds = new Bounds(PathConvert.ToVector3(_line.Positions[0]), Vector3.zero);
            for (int i = 0; i < _line.Positions.Length; i++)
                bounds.Encapsulate(PathConvert.ToVector3(_line.Positions[i]));

            float halfX = bounds.extents.x;
            float halfZ = bounds.extents.z;
            // The larger half-extent fits the rectangle whichever way the frame is wider than tall.
            float size = Mathf.Max(halfZ, halfX * FrameHeight / (float)FrameWidth) * 1.08f;

            var probe = new GameObject("capture-camera-overview");
            var camera = probe.AddComponent<Camera>();
            camera.cullingMask = ~(1 << 5);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.2f, 0.24f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = size;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 600f;
            camera.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.max.y + 300f, bounds.center.z),
                Quaternion.Euler(90f, 0f, 0f));

            SaveFrame(camera, "circuit-overview.png");
            Object.DestroyImmediate(probe);

            Debug.Log("[agent] overview: road bounds " + bounds.size.ToString("0.0") + " m centred "
                + bounds.center.ToString("0.0") + ", ortho size " + size.ToString("0.0")
                + " m, lap " + _line.TotalLength.ToString("0.0") + " m, " + _line.Positions.Length
                + " samples");
        }

        /// <summary>
        /// Puts a real car on the corner and shoots it from the map's own camera override: the offset
        /// and look height come from the shipped asset, so the frame is the pull-back the game uses.
        /// </summary>
        private IEnumerator CaptureCorner(string fileName, float meters)
        {
            int index = NearestIndex(meters);
            var position = PathConvert.ToVector3(_line.Positions[index]);
            Quaternion rotation = PathConvert.HeadingRotation(_line.Tangents[index]);
            var pose = new Pose(position + Vector3.up * _map.SpawnHeight, rotation);

            CarDefinition definition = Resources.Load<CarDefinition>(CarResource);
            Assert.IsNotNull(definition, CarResource + " must ship in Resources");
            OwnedCarGeometry car = CarGeometryFactory.CreateWithoutFloor(definition, pose,
                _anchor.transform);

            var probe = new GameObject("capture-camera-" + fileName);
            var camera = probe.AddComponent<Camera>();
            camera.cullingMask = ~(1 << 5);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.2f, 0.24f, 1f);
            camera.fieldOfView = RaceFieldOfView;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 900f;
            camera.transform.position = pose.position + pose.rotation * _map.CameraOffset;
            camera.transform.rotation = Quaternion.LookRotation(
                (pose.position + Vector3.up * _map.CameraLookHeight - camera.transform.position).normalized,
                Vector3.up);

            yield return null;
            yield return null;

            Debug.Log("[agent] " + fileName + ": car at " + meters.ToString("0") + " m, path sample "
                + index + " " + pose.position.ToString("0.0") + " heading "
                + pose.rotation.eulerAngles.y.ToString("0") + " deg, camera "
                + camera.transform.position.ToString("0.0") + " fov " + RaceFieldOfView.ToString("0")
                + " (map override " + _map.CameraOffset.ToString("0.0") + ")");
            SaveFrame(camera, fileName);

            Object.DestroyImmediate(probe);
            car.Dispose();
            yield return null;
        }

        /// <summary>
        /// A straight-down frame over one stretch of the route, so a corner's actual shape - the 180 of
        /// the hairpin, the S of the chicane - is visible instead of inferred.
        /// </summary>
        private void SavePlan(string fileName, float meters, float span)
        {
            int index = NearestIndex(meters);
            Vector3 centre = PathConvert.ToVector3(_line.Positions[index]);

            var probe = new GameObject("capture-camera-" + fileName);
            var camera = probe.AddComponent<Camera>();
            camera.cullingMask = ~(1 << 5);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.2f, 0.24f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = span * 0.5f;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 600f;
            camera.transform.SetPositionAndRotation(new Vector3(centre.x, centre.y + 200f, centre.z),
                Quaternion.Euler(90f, 0f, 0f));

            SaveFrame(camera, fileName);
            Object.DestroyImmediate(probe);

            Debug.Log("[agent] " + fileName + ": plan of " + span.ToString("0") + " m centred on route "
                + meters.ToString("0") + " m, path sample " + index + " " + centre.ToString("0.0"));
        }

        /// <summary>Frames the nearest path sample to the given route metres.</summary>
        private int NearestIndex(float meters)
        {
            int best = 0;
            float bestGap = float.MaxValue;
            for (int i = 0; i < _line.Positions.Length; i++)
            {
                float gap = Mathf.Abs(_line.Distances[i] - meters);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Renders through the given camera into a temp target and writes the PNG.</summary>
        private static void SaveFrame(Camera camera, string fileName)
        {
            RenderTexture previousTarget = camera.targetTexture;
            var target = RenderTexture.GetTemporary(FrameWidth, FrameHeight, 24);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);

            byte[] bytes = texture.EncodeToPNG();
            Object.Destroy(texture);

            string path = Path.Combine(Path.GetFullPath(OutputFolder), fileName);
            File.WriteAllBytes(path, bytes);

            // Second copy outside the project tree: a batchmode Editor deletes the project Temp when it
            // quits, and evidence that dies with its own run is not evidence.
            string durable = Path.Combine(DurableFolder(), fileName);
            File.WriteAllBytes(durable, bytes);

            Debug.Log("[agent] capture " + fileName + " " + FrameWidth + "x" + FrameHeight + " -> " + path
                + " (" + bytes.Length + " bytes, survives: " + durable + ")");
        }

        private static string DurableFolder() => Path.Combine(Path.GetTempPath(), DurableOutputFolder);
    }
}
