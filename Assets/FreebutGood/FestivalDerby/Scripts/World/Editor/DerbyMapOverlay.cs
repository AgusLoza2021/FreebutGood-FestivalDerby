using FestivalDerby.World;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// The Scene View overlay that holds everything the tool does not: presets, the snap toggle, the
    /// symmetry block, validation with a jump-to-issue button and the bake. Keeping these out of the
    /// tool means the curve editing surface stays about the curve, and it is where an author looks for
    /// the bake because that is the step that produces a file.
    /// </summary>
    [Overlay(typeof(SceneView), "derby-map", "Derby Map", "DerbyMap")]
    internal sealed class DerbyMapOverlay : Overlay, ITransientOverlay
    {
        private const float MinPanelWidth = 280f;

        private VisualElement _root;
        private string _status;
        private float _lastStep = 1f;

        /// <summary>
        /// Shown only while something that can be authored is selected. A permanent panel would take
        /// Scene View space in every other task.
        /// </summary>
        public bool visible
        {
            get { return Current() != null; }
        }

        public override VisualElement CreatePanelContent()
        {
            _root = new VisualElement();
            _root.style.minWidth = MinPanelWidth;
            Refresh();
            return _root;
        }

        public override void OnCreated()
        {
            base.OnCreated();
            Selection.selectionChanged += RefreshSoon;
            displayedChanged += OnDisplayedChanged;
        }

        public override void OnWillBeDestroyed()
        {
            Selection.selectionChanged -= RefreshSoon;
            displayedChanged -= OnDisplayedChanged;
            base.OnWillBeDestroyed();
        }

        /// <summary>
        /// Rebuilds the panel from the current selection. Deferred through delayCall because most
        /// callbacks that ask for a refresh come from a control that the rebuild is about to remove.
        /// </summary>
        private void RefreshSoon()
        {
            EditorApplication.delayCall -= Refresh;
            EditorApplication.delayCall += Refresh;
        }

        private void OnDisplayedChanged(bool displayed)
        {
            if (displayed)
            {
                RefreshSoon();
            }
        }

        private static DerbyMapAuthoring Current()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponent<DerbyMapAuthoring>() : null;
        }

        private void Refresh()
        {
            if (_root == null)
            {
                return;
            }

            _root.Clear();
            DerbyMapAuthoring authoring = Current();
            if (authoring == null)
            {
                _root.Add(new Label("Select a Derby Map Authoring object."));
                return;
            }

            if (DerbyMapGeometry.ContainerOf(authoring) == null)
            {
                _root.Add(Header(authoring));
                _root.Add(new Label("Add a Spline Container to this object to author its curve."));
                return;
            }

            _root.Add(Header(authoring));
            _root.Add(PresetRow(authoring));
            _root.Add(SnapBlock(authoring));
            _root.Add(SymmetryBlock(authoring));
            _root.Add(ActionRow(authoring));
            _root.Add(IssueList(authoring));
            if (!string.IsNullOrEmpty(_status))
            {
                _root.Add(new Label(_status));
            }
        }

        private static Label Header(DerbyMapAuthoring authoring)
        {
            string kind = authoring.Kind == DerbyMapAuthoring.MapKind.Derby ? "DERBY" : "LAP";
            return new Label(kind + "  " + authoring.MapId + "  " + authoring.RoadWidth.ToString("0.#") + " m wide"
                + (authoring.ClosedLoop ? "  closed" : "  open"));
        }

        private VisualElement PresetRow(DerbyMapAuthoring authoring)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(PresetButton(authoring, DerbyMapPresets.Kind.Oval, "Oval"));
            row.Add(PresetButton(authoring, DerbyMapPresets.Kind.FigureEight, "Figure-8"));
            row.Add(PresetButton(authoring, DerbyMapPresets.Kind.Arena, "Arena"));
            return row;
        }

        private Button PresetButton(DerbyMapAuthoring authoring, DerbyMapPresets.Kind kind, string text)
        {
            return new Button(() =>
            {
                DerbyMapPresets.Apply(authoring, kind);
                _status = "Preset " + kind + " applied (" + DerbyMapPresets.DefaultKnotCount(kind) + " knots).";
                SceneView.RepaintAll();
                RefreshSoon();
            })
            { text = text };
        }

        private VisualElement SnapBlock(DerbyMapAuthoring authoring)
        {
            var block = new VisualElement();

            var toggle = new Toggle("Snap") { value = authoring.GridStep > 0f };
            toggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(authoring, "Set Derby grid snap");
                authoring.GridStep = evt.newValue ? Mathf.Max(_lastStep, 0.5f) : 0f;
                EditorUtility.SetDirty(authoring);
                SceneView.RepaintAll();
            });

            var step = new FloatField("Step") { value = authoring.GridStep > 0f ? authoring.GridStep : _lastStep };
            step.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(authoring, "Set Derby grid step");
                _lastStep = Mathf.Max(0.25f, evt.newValue);
                authoring.GridStep = _lastStep;
                EditorUtility.SetDirty(authoring);
                SceneView.RepaintAll();
            });

            block.Add(toggle);
            block.Add(step);
            return block;
        }

        private VisualElement SymmetryBlock(DerbyMapAuthoring authoring)
        {
            var block = new VisualElement();

            var mode = new EnumField("Symmetry", DerbyMapAuthoring.SymmetryMode.None)
            {
                value = authoring.Symmetry.Mode,
            };
            mode.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(authoring, "Set Derby symmetry");
                authoring.Symmetry.Mode = (DerbyMapAuthoring.SymmetryMode)evt.newValue;
                authoring.Symmetry.Linked = authoring.Symmetry.Mode != DerbyMapAuthoring.SymmetryMode.None;
                EditorUtility.SetDirty(authoring);
                SceneView.RepaintAll();
                RefreshSoon();
            });

            var pivot = new Vector3Field("Pivot") { value = authoring.Symmetry.Pivot };
            pivot.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(authoring, "Set Derby symmetry pivot");
                authoring.Symmetry.Pivot = evt.newValue;
                EditorUtility.SetDirty(authoring);
                SceneView.RepaintAll();
            });

            var sectors = new IntegerField("Sectors") { value = authoring.Symmetry.RotationalCount };
            sectors.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(authoring, "Set Derby rotational sectors");
                authoring.Symmetry.RotationalCount = Mathf.Clamp(evt.newValue, 2, 12);
                EditorUtility.SetDirty(authoring);
                SceneView.RepaintAll();
            });

            var linked = new Toggle("Linked (live ghost)") { value = authoring.Symmetry.Linked };
            linked.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(authoring, "Set Derby symmetry link");
                authoring.Symmetry.Linked = evt.newValue;
                EditorUtility.SetDirty(authoring);
                SceneView.RepaintAll();
            });

            block.Add(mode);
            block.Add(pivot);
            block.Add(sectors);
            block.Add(linked);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            var apply = new Button(() => ApplySymmetry(authoring))
            {
                text = "Apply symmetry",
                tooltip = "Commit the ghost into the knots. The half you drew becomes the whole loop.",
            };
            apply.SetEnabled(CanApplySymmetry(authoring));

            var unlink = new Button(() =>
            {
                Undo.RecordObject(authoring, "Unlink Derby symmetry");
                authoring.Symmetry.Linked = false;
                EditorUtility.SetDirty(authoring);
                SceneView.RepaintAll();
                RefreshSoon();
            })
            { text = "Unlink" };
            unlink.SetEnabled(authoring.Symmetry.Linked);

            row.Add(apply);
            row.Add(unlink);
            block.Add(row);
            return block;
        }

        private static bool CanApplySymmetry(DerbyMapAuthoring authoring)
        {
            return authoring.Symmetry.Mode != DerbyMapAuthoring.SymmetryMode.None;
        }

        /// <summary>
        /// Commits the completed loop and clears the link in the same step. Keeping the link set would
        /// mirror the already-mirrored knots on the next edit, which reads as the road doubling itself.
        /// </summary>
        private void ApplySymmetry(DerbyMapAuthoring authoring)
        {
            SplineContainer container = DerbyMapGeometry.ContainerOf(authoring);
            if (container == null || container.Splines.Count == 0)
            {
                return;
            }

            Vector3[] authored = DerbyMapGeometry.ReadWorldKnots(container);
            Vector3[] full = DerbyMapGeometry.BuildSymmetry(authoring, authored);
            if (full == null || full.Length < 2)
            {
                _status = "Symmetry produced no usable loop.";
                RefreshSoon();
                return;
            }

            Undo.RecordObject(authoring, "Apply Derby symmetry");
            authoring.Symmetry.Linked = false;
            EditorUtility.SetDirty(authoring);
            DerbyMapGeometry.WriteWorldKnots(container, authoring, full, "Apply Derby symmetry");
            _status = "Symmetry applied: " + full.Length + " knots.";
            SceneView.RepaintAll();
            RefreshSoon();
        }

        private VisualElement ActionRow(DerbyMapAuthoring authoring)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            row.Add(new Button(() =>
            {
                int count = DerbyMapValidation.Validate(authoring);
                _status = count == 0
                    ? "Validation: no issues (" + DerbyMapValidation.LastLength.ToString("0.0") + " m)."
                    : "Validation: " + count + " issue(s).";
                SceneView.RepaintAll();
                RefreshSoon();
            })
            { text = "Validate" });

            row.Add(new Button(() =>
            {
                _status = DerbyMapBake.BakeAndWrite(authoring);
                RefreshSoon();
            })
            {
                text = "Bake",
                tooltip = "Writes the target MapAsset, road, edges and surfaces. Mesh sweep is a later step.",
            });

            return row;
        }

        private VisualElement IssueList(DerbyMapAuthoring authoring)
        {
            var list = new VisualElement();
            MapIssue[] issues = DerbyMapValidation.LastIssues;
            if (issues == null || issues.Length == 0)
            {
                return list;
            }

            var scroll = new ScrollView { style = { maxHeight = 160f } };
            for (int i = 0; i < issues.Length; i++)
            {
                MapIssue issue = issues[i];
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;

                var label = new Label(DerbyMapValidation.Describe(issue)) { style = { flexGrow = 1f } };
                if (issue.Severity == MapIssueSeverity.Error)
                {
                    label.style.color = new Color(1f, 0.55f, 0.45f);
                }

                row.Add(label);
                row.Add(new Button(() =>
                {
                    if (DerbyMapValidation.Frame(issue, authoring))
                    {
                        SceneView.RepaintAll();
                    }
                })
                { text = "select" });

                scroll.Add(row);
            }

            list.Add(scroll);
            return list;
        }
    }
}
