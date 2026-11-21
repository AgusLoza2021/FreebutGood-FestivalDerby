using System;
using UnityEngine;

namespace FestivalDerby.Driving.Runtime
{
    /// <summary>
    /// One cosmetic model plus the two authoring corrections that are never derivable from the
    /// physics box: how to turn the mesh onto the car's forward axis and how much longer or
    /// shorter than the box it should read.
    /// </summary>
    [Serializable]
    public sealed class CarVisualEntry
    {
        public string Id;
        public GameObject Model;
        /// <summary>False until the owner's Blender pass is done; the raw Meshy exports are not.</summary>
        public bool Approved;
        /// <summary>Applied before fitting, e.g. when the FBX forward axis is not +Z.</summary>
        public Vector3 EulerOffset;
        public float ScaleMultiplier = 1f;
        /// <summary>
        /// Flips this model's wheel roll, and only this model's. The rig derives the roll axis and its
        /// sign from the wheel mesh, so the correct value is 1 for every model whose wheels are round
        /// about the axle the exporter gave them; the sign is here because the owner judges the spin
        /// from a capture and a single model can need the opposite of its neighbours.
        /// </summary>
        public int WheelSpinSign = 1;
        /// <summary>
        /// Renderer or material names that are the body on a model the name rules cannot read. Only
        /// set this where the model really isolates the body from glass, lights and tyres: a
        /// single-renderer car has nothing to separate, and painting it would tint the windscreen,
        /// so it stays factory until the mask or the Blender pass gives us a body target.
        /// </summary>
        public string[] PaintTargets = new string[0];
        /// <summary>
        /// The same car split into detachable panels (<c>&lt;Id&gt;_Part_&lt;Kind&gt;</c> nodes). It rides on the
        /// entry instead of being an entry of its own because every catalog entry is a garage card: a split
        /// twin would list each car twice. Used only while DrivingFeatureConfig.CarPartDetachEnabled is on.
        /// </summary>
        public GameObject PartsModel;

        /// <summary>
        /// The entry the run spawns: this one, or a copy that swaps in <see cref="PartsModel"/> when part
        /// detach is on and the car has one. Allocates once per spawn, never per frame.
        /// </summary>
        public static CarVisualEntry WithPartsModel(CarVisualEntry entry, bool partDetachEnabled)
        {
            if (!partDetachEnabled || entry == null || entry.PartsModel == null)
                return entry;

            return new CarVisualEntry
            {
                Id = entry.Id,
                Model = entry.PartsModel,
                Approved = entry.Approved,
                EulerOffset = entry.EulerOffset,
                ScaleMultiplier = entry.ScaleMultiplier,
                WheelSpinSign = entry.WheelSpinSign,
                PaintTargets = entry.PaintTargets,
                PartsModel = entry.PartsModel,
            };
        }
    }

    /// <summary>
    /// The approved cosmetic models, keyed by a stable id. It exists so the spawn code can ask for
    /// "the model for this car" without knowing which FBX the owner has already cleaned up, and so
    /// the seven raw variants can ship in the project without ever appearing on screen: an
    /// unapproved model is used only when the owner says so, and a missing catalog leaves the
    /// gray box exactly as it was.
    /// </summary>
    [CreateAssetMenu(fileName = "CarVisualCatalog", menuName = "Config/CarVisualCatalog")]
    public sealed class CarVisualCatalog : ScriptableObject
    {
        public string DefaultId = "GreenVan";
        /// <summary>Off by default: imported-but-uncleaned models stay out of the game.</summary>
        public bool AllowUnapprovedModels;
        public CarVisualEntry[] Entries = new CarVisualEntry[0];

        /// <summary>
        /// The id's entry when it has a model and the owner allows it; otherwise the default entry
        /// when that has a model; otherwise nothing, which the caller reads as "keep the gray box".
        /// </summary>
        public bool TryResolve(string id, out CarVisualEntry entry)
        {
            entry = null;

            if (!string.IsNullOrEmpty(id))
            {
                var candidate = Find(id);
                if (candidate != null && candidate.Model != null &&
                    (candidate.Approved || AllowUnapprovedModels))
                {
                    entry = candidate;
                    return true;
                }
            }

            // The default is the owner's single confirmed model, so the approval flag — which gates
            // the imported variants — does not apply to the fallback itself.
            var fallback = Find(DefaultId);
            if (fallback == null || fallback.Model == null)
                return false;

            entry = fallback;
            return true;
        }

        /// <summary>Null-safe overload so a feature that was never configured cannot throw.</summary>
        public static bool TryResolve(CarVisualCatalog catalog, string id, out CarVisualEntry entry)
        {
            if (catalog == null)
            {
                entry = null;
                return false;
            }

            return catalog.TryResolve(id, out entry);
        }

        /// <summary>
        /// The entry's roll direction as a multiplier, with the stale-asset rule applied in one place:
        /// a null entry, a zeroed field or a value no author should have typed all read as "as the mesh
        /// asks". Callers never have to sanitize before handing it to the rig.
        /// </summary>
        public static float ResolveWheelSpinSign(CarVisualEntry entry)
        {
            return entry != null && entry.WheelSpinSign < 0 ? -1f : 1f;
        }

        private CarVisualEntry Find(string id)
        {
            if (Entries == null || string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < Entries.Length; i++)
            {
                var entry = Entries[i];
                if (entry != null && string.Equals(entry.Id, id, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        public void Sanitize()
        {
            if (Entries == null)
                Entries = new CarVisualEntry[0];

            for (int i = 0; i < Entries.Length; i++)
            {
                var entry = Entries[i];
                if (entry == null)
                    continue;

                // A zero, negative or non-finite multiplier would collapse or explode the fit.
                if (float.IsNaN(entry.ScaleMultiplier) || float.IsInfinity(entry.ScaleMultiplier) ||
                    entry.ScaleMultiplier <= 0f)
                    entry.ScaleMultiplier = 1f;

                // A zero here means "the asset predates the field", not "stand still": the rig needs a
                // direction and 1 is the one the mesh geometry already asks for.
                entry.WheelSpinSign = entry.WheelSpinSign < 0 ? -1 : 1;

                // A catalog built in code or loaded from an older asset can carry a null array.
                if (entry.PaintTargets == null)
                    entry.PaintTargets = new string[0];
            }
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
