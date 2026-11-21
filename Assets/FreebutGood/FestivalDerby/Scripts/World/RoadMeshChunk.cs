using System;
using UnityEngine;

namespace FestivalDerby.World
{
    /// <summary>
    /// One swept piece of road: the mesh plus the arc-length window it covers. The window is stored
    /// beside the mesh so a future consumer (LOD selection, streaming, per-chunk physics) can pick a
    /// chunk without walking its vertices, and so two chunks that share a boundary can be compared.
    /// </summary>
    [Serializable]
    public struct RoadMeshChunk
    {
        /// <summary>The chunk's triangles. Owned by whoever built it until a mesh owner takes over.</summary>
        public Mesh Mesh;

        /// <summary>Arc length at the first ring, inclusive.</summary>
        public float StartDistance;

        /// <summary>Arc length at the last ring, inclusive. Equals the next chunk's StartDistance.</summary>
        public float EndDistance;
    }
}
