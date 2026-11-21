using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// Keeps the swept road meshes alive exactly as long as the map root that renders them. A runtime
    /// Mesh is not destroyed with its GameObject, so without this component a torn-down map would leak
    /// one mesh per chunk. The owner is attached to the loader's root, and the root is the only thing
    /// the driving circuit takes over, so teardown reaches the meshes without the World assembly
    /// needing to name the circuit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoadMeshOwner : MonoBehaviour
    {
        private Mesh[] _meshes;

        /// <summary>Hands the meshes to this owner. Called once, right after the loader builds them.</summary>
        public void Own(Mesh[] meshes)
        {
            _meshes = meshes;
        }

        private void OnDestroy()
        {
            if (_meshes == null)
                return;

            for (int i = 0; i < _meshes.Length; i++)
                Release(_meshes[i]);
            _meshes = null;
        }

        /// <summary>
        /// A baked map stores its chunks as sub-assets of the MapAsset, so destroying one of those
        /// would delete a project asset; only the pre-sweep ribbon built at runtime belongs here.
        /// Unity releases the persistent meshes together with the map.
        /// </summary>
        private static void Release(Mesh mesh)
        {
            if (mesh == null)
                return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(mesh))
                return;
#endif
            Destroy(mesh);
        }
    }
}
