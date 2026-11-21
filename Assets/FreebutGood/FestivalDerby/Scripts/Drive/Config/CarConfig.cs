using FestivalDerby.Driving.Runtime;
using UnityEngine;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The player's car. The heavy tuning (car definition, handling model and preset, model catalog, feel
    /// switches, respawn) lives in the <see cref="DrivingFeatureConfig"/> asset so it can be shared and
    /// versioned; this component picks that asset and adds the few per-scene choices on top.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarConfig : MonoBehaviour
    {
        [Tooltip("Car definition, handling model and preset, model catalog and feel switches. The bots drive " +
            "this same car.")]
        [SerializeField] private DrivingFeatureConfig _driving;

        [Tooltip("Swap the handling preset (Balanced, Drifty, Grippy) without editing the shared asset. " +
            "Empty uses the preset inside Driving. Applies to the bots too: everybody drives the same car.")]
        [SerializeField] private HandlingProfile _handlingOverride;

        [Tooltip("Toy model id from the catalog (ToySedan, ToyTaxi, ToyPolice...). Empty uses the id inside Driving.")]
        [SerializeField] private string _modelId = "";

        [Tooltip("Paint the body panels with Paint Color. Off keeps the model's factory colours.")]
        [SerializeField] private bool _repaint;

        [Tooltip("Body colour used when Repaint is on.")]
        [SerializeField] private Color _paintColor = new Color(1f, 0.85f, 0.1f, 1f);

        public DrivingFeatureConfig Driving
        {
            get { return _driving; }
            set { _driving = value; }
        }

        public HandlingProfile HandlingOverride
        {
            get { return _handlingOverride; }
            set { _handlingOverride = value; }
        }

        /// <summary>The model id the player resolves to: this component's, else the asset's.</summary>
        public string ModelId
        {
            get
            {
                return string.IsNullOrEmpty(_modelId)
                    ? DrivingFeatureConfig.ResolvePlayerVisualId(_driving)
                    : _modelId;
            }
        }

        /// <summary>Picks the car for this run (the main menu's choice), over the Inspector value.</summary>
        public void UseModel(string id)
        {
            _modelId = id != null ? id.Trim() : "";
        }

        public bool Repaint
        {
            get { return _repaint; }
        }

        public Color PaintColor
        {
            get { return _paintColor; }
        }

        /// <summary>
        /// The motor every car of the run is bound with. The override only means something for the V2
        /// model, which is the one that reads a HandlingProfile.
        /// </summary>
        public MotorSetup ResolveMotor(JoystickDriveMode driveMode, Transform camera)
        {
            MotorSetup setup = MotorSetupFactory.ResolveWithProfile(_driving, driveMode, camera);
            if (_handlingOverride == null || setup.Model != HandlingModel.V2)
                return setup;

            return MotorSetup.ForProfile(_handlingOverride, setup.DriveMode, setup.Camera, setup.StickDeadZone);
        }

        private void OnValidate()
        {
            _modelId = _modelId != null ? _modelId.Trim() : "";
            _paintColor.a = 1f;
        }
    }
}
