using NUnit.Framework;
using UnityEngine;

namespace FestivalDerby.Look.Tests
{
    /// <summary>
    /// Pins the two contracts the applier and the camera wiring rely on: a disabled or missing
    /// profile resolves to a complete disabled value, and every broken field is repaired to a value
    /// a human tuned rather than to whatever survived the serialization.
    /// </summary>
    public sealed class LookProfileEditModeTests
    {
        // The profile the bootstraps resolve by name. It is loaded through Resources so this fixture
        // fails if the asset is moved, renamed or added to a bundle instead of staying loadable.
        private const string ShippedProfilePath = "Look/LookProfile_CircuitSuperstars";

        private LookProfile _profile;

        [SetUp]
        public void SetUp()
        {
            // CreateInstance runs the field initializers, so these tests double as the pin on the
            // shipped code defaults: they are what a config authored before this asset existed gets.
            _profile = ScriptableObject.CreateInstance<LookProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_profile);
        }

        [Test]
        public void ShippedProfile_CarriesTheCameraGroupTheCodeDefaultsDescribe()
        {
            // The offline harness measures the framing as arithmetic (ChaseFramingModelTests) but
            // cannot read a Unity asset, so this is the bridge: the shipped asset and the code
            // defaults are one tuning, or a lab run and a build frame the same car differently.
            LookProfile shipped = Resources.Load<LookProfile>(ShippedProfilePath);

            Assert.IsNotNull(shipped, "The shipped look profile must stay loadable by path.");
            Assert.IsTrue(shipped.LookProfileEnabled, "A disabled profile leaves the inherited camera in place.");
            Assert.AreEqual(LookDefaults.CameraOffset, shipped.CameraOffset);
            Assert.AreEqual(LookDefaults.CameraLookHeight, shipped.CameraLookHeight, 0.0001f);
            Assert.AreEqual(LookDefaults.CameraFov, shipped.CameraFov, 0.0001f);
            Assert.AreEqual(LookDefaults.CameraFollowSharpness, shipped.CameraFollowSharpness, 0.0001f);
            Assert.AreEqual(LookDefaults.LookAheadMetresPerSpeed, shipped.LookAheadMetresPerSpeed, 0.0001f);
            Assert.AreEqual(LookDefaults.LookAheadMaxMetres, shipped.LookAheadMaxMetres, 0.0001f);
            Assert.AreEqual(LookDefaults.LookAheadSharpness, shipped.LookAheadSharpness, 0.0001f);
            Assert.AreEqual(LookDefaults.SpeedFovWidenDegrees, shipped.SpeedFovWidenDegrees, 0.0001f);
            Assert.AreEqual(LookDefaults.SpeedFovAtMetresPerSecond, shipped.SpeedFovAtMetresPerSecond, 0.0001f);
            Assert.AreEqual(LookDefaults.SpeedFovSharpness, shipped.SpeedFovSharpness, 0.0001f);
        }

        [Test]
        public void ShippedProfile_KeepsTheLeadInsideTheCentredEnvelope()
        {
            // CAM1, and the reason this test exists at all. The harness projects the framing onto a
            // 16:9 screen: 0.08 m per m/s capped at 1.8 m keeps the car within 0.047 of half the
            // screen's width and 0.067 of half its height of the no-lead framing (measured at 30 m/s).
            // CS1's 0.5 m per m/s capped at 14 m measured 0.37 and 0.67 there -- the car parked near
            // the frame edge, which is the reported defect. These are the shape of the fix, not a
            // re-tune: a wider envelope has to be a deliberate edit.
            LookProfile shipped = Resources.Load<LookProfile>(ShippedProfilePath);

            Assert.IsNotNull(shipped);
            Assert.That(shipped.LookAheadMetresPerSpeed, Is.LessThanOrEqualTo(0.1f),
                "A tenth of a metre per m/s is already a fifth of a car length at top speed.");
            Assert.That(shipped.LookAheadMaxMetres, Is.LessThanOrEqualTo(2f),
                "A cap measured in metres pushes the subject out of the middle of the frame.");
            Assert.That(shipped.SpeedFovWidenDegrees, Is.LessThanOrEqualTo(2f),
                "More than two extra degrees at speed reads as a zoom rather than as depth.");
        }

        [Test]
        public void Resolve_NullProfile_IsDisabledAndCarriesTheTunedDefaults()
        {
            LookSettings settings = LookProfile.Resolve(null);

            Assert.IsFalse(settings.Enabled, "A missing profile must not apply anything.");
            Assert.AreEqual(LookDefaults.CameraOffset, settings.CameraOffset);
            Assert.AreEqual(LookDefaults.CameraLookHeight, settings.CameraLookHeight);
            Assert.AreEqual(LookDefaults.CameraFov, settings.CameraFov);
            Assert.AreEqual(0f, settings.LookAheadMetresPerSpeed, "A disabled profile must not lead the car.");
            Assert.AreEqual(0f, settings.LookAheadMaxMetres);
            Assert.IsFalse(settings.PaletteOverrideEnabled, "A disabled profile must not repaint anything.");
            Assert.IsFalse(settings.GroundFillEnabled);
        }

        [Test]
        public void Resolve_CodeDefaultAsset_IsOff()
        {
            // The kill switch's C# default has to be false: an asset authored before this field
            // existed deserializes the constructor's value, and true there would silently turn the
            // look on for every project that never opened the asset.
            Assert.IsFalse(_profile.LookProfileEnabled);
            Assert.IsFalse(LookProfile.Resolve(_profile).Enabled);
        }

        [Test]
        public void Resolve_EnabledProfile_UsesItsAuthoredValues()
        {
            _profile.LookProfileEnabled = true;

            LookSettings settings = LookProfile.Resolve(_profile);

            Assert.IsTrue(settings.Enabled);
            Assert.AreEqual(LookDefaults.CameraOffset, settings.CameraOffset);
            Assert.AreEqual(LookDefaults.LookAheadMetresPerSpeed, settings.LookAheadMetresPerSpeed);
            Assert.AreEqual(LookDefaults.AsphaltColor, settings.AsphaltColor);
            Assert.AreEqual(LookDefaults.CameraBackground, settings.CameraBackground);
            Assert.IsTrue(settings.AmbientTrilight);
            Assert.IsTrue(settings.GroundFillEnabled);
        }

        [Test]
        public void Resolve_NonFiniteFields_FallBackToTheTunedDefaults()
        {
            _profile.LookProfileEnabled = true;
            _profile.CameraFov = float.NaN;
            _profile.CameraOffset = new Vector3(1f, float.PositiveInfinity, 3f);
            _profile.SunIntensity = float.NegativeInfinity;
            _profile.AsphaltColor = new Color(float.NaN, 0.5f, 0.5f, 1f);
            _profile.GroundFillMargin = float.PositiveInfinity;

            LookSettings settings = LookProfile.Resolve(_profile);

            Assert.AreEqual(LookDefaults.CameraFov, settings.CameraFov);
            Assert.AreEqual(LookDefaults.CameraOffset, settings.CameraOffset);
            Assert.AreEqual(LookDefaults.SunIntensity, settings.SunIntensity);
            Assert.AreEqual(LookDefaults.AsphaltColor, settings.AsphaltColor);
            Assert.AreEqual(LookDefaults.GroundFillMargin, settings.GroundFillMargin);
        }

        [Test]
        public void Resolve_OutOfRangeFields_FallBackInsteadOfBeingClamped()
        {
            _profile.LookProfileEnabled = true;
            _profile.CameraFov = 400f;
            _profile.SunShadowStrength = 5f;
            _profile.LookAheadMetresPerSpeed = -2f;
            _profile.LookAheadMaxMetres = -1f;

            LookSettings settings = LookProfile.Resolve(_profile);

            Assert.AreEqual(LookDefaults.CameraFov, settings.CameraFov, "A 400-degree FOV is a typo, not a value.");
            Assert.AreEqual(LookDefaults.SunShadowStrength, settings.SunShadowStrength);
            Assert.AreEqual(0f, settings.LookAheadMetresPerSpeed, "A negative lead means no lead.");
            Assert.AreEqual(0f, settings.LookAheadMaxMetres);
        }

        [Test]
        public void Resolve_ZeroLookAhead_IsKeptAsTheAuthorsOffSwitch()
        {
            // Zero is the one look-ahead value that must survive: it is how an author turns the lead
            // off while keeping the rest of the look on.
            _profile.LookProfileEnabled = true;
            _profile.LookAheadMetresPerSpeed = 0f;
            _profile.LookAheadMaxMetres = 0f;

            LookSettings settings = LookProfile.Resolve(_profile);

            Assert.IsTrue(settings.Enabled);
            Assert.AreEqual(0f, settings.LookAheadMetresPerSpeed);
            Assert.AreEqual(0f, settings.LookAheadMaxMetres);
            Assert.AreEqual(LookDefaults.LookAheadSharpness, settings.LookAheadSharpness);
        }

        [Test]
        public void Sanitize_RewritesEveryBrokenFieldOnTheAsset()
        {
            _profile.CameraFov = float.NaN;
            _profile.CameraOffset = new Vector3(0f, 0f, float.NaN);
            _profile.CameraFollowSharpness = -1f;
            _profile.LookAheadSharpness = float.PositiveInfinity;
            _profile.SunColor = new Color(1f, float.NaN, 1f, 1f);
            _profile.SunEulerAngles = new Vector3(float.NaN, 0f, 0f);
            _profile.AmbientSky = new Color(float.NaN, 0f, 0f, 1f);
            _profile.WallColor = new Color(0f, 0f, float.NaN, 1f);
            _profile.KerbColor = new Color(0f, float.NaN, 0f, 1f);
            _profile.GroundColor = new Color(float.NaN, 0f, 0f, 1f);
            _profile.CameraBackground = new Color(0f, float.NaN, 0f, 1f);
            _profile.GroundFillDepth = -4f;

            _profile.Sanitize();

            Assert.AreEqual(LookDefaults.CameraFov, _profile.CameraFov);
            Assert.AreEqual(LookDefaults.CameraOffset, _profile.CameraOffset);
            Assert.AreEqual(LookDefaults.CameraFollowSharpness, _profile.CameraFollowSharpness);
            Assert.AreEqual(LookDefaults.LookAheadSharpness, _profile.LookAheadSharpness);
            Assert.AreEqual(LookDefaults.SunColor, _profile.SunColor);
            Assert.AreEqual(LookDefaults.SunEulerAngles, _profile.SunEulerAngles);
            Assert.AreEqual(LookDefaults.AmbientSky, _profile.AmbientSky);
            Assert.AreEqual(LookDefaults.WallColor, _profile.WallColor);
            Assert.AreEqual(LookDefaults.KerbColor, _profile.KerbColor);
            Assert.AreEqual(LookDefaults.GroundColor, _profile.GroundColor);
            Assert.AreEqual(LookDefaults.CameraBackground, _profile.CameraBackground);
            Assert.AreEqual(LookDefaults.GroundFillDepth, _profile.GroundFillDepth);
        }

        [Test]
        public void Sanitize_LeavesFiniteAuthoredValuesAlone()
        {
            _profile.CameraFov = 42f;
            _profile.CameraOffset = new Vector3(-18f, 26f, -12f);
            _profile.AsphaltColor = new Color(0.5f, 0.51f, 0.52f, 1f);
            _profile.GroundFillMargin = 55f;

            _profile.Sanitize();

            Assert.AreEqual(42f, _profile.CameraFov);
            Assert.AreEqual(new Vector3(-18f, 26f, -12f), _profile.CameraOffset);
            Assert.AreEqual(new Color(0.5f, 0.51f, 0.52f, 1f), _profile.AsphaltColor);
            Assert.AreEqual(55f, _profile.GroundFillMargin);
        }
    }
}
