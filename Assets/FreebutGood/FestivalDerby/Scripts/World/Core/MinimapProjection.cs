using System;

namespace FestivalDerby.World
{
    public struct MinimapPoint
    {
        public float X;
        public float Y;
        public MinimapPoint(float x, float y) { X = x; Y = y; }
    }

    /// <summary>Cached XZ bounds transform and path polyline for engine-free minimap drawing.</summary>
    public sealed class MinimapProjection
    {
        private readonly MinimapPoint[] _polyline;
        private readonly float _centerX, _centerZ, _cos, _sin, _scaleX, _scaleY;
        public int PolylineCount { get { return _polyline.Length; } }
        public MinimapPoint PolylineAt(int index) { return _polyline[index]; }

        public MinimapProjection(BakedPath path, float rectWidth, float rectHeight, float padding, bool rotateToFit)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Count < 2 || rectWidth <= 0f || rectHeight <= 0f || padding < 0f || padding * 2f >= Math.Min(rectWidth, rectHeight))
                throw new ArgumentOutOfRangeException();
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < path.Count; i++)
            {
                PathVector3 p = path.Positions[i];
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
            }
            _centerX = (minX + maxX) * 0.5f; _centerZ = (minZ + maxZ) * 0.5f;
            _cos = 1f; _sin = 0f;
            float width = Math.Max(maxX - minX, 1e-5f), height = Math.Max(maxZ - minZ, 1e-5f);
            if (rotateToFit && width > height && rectHeight > rectWidth) { _cos = 0f; _sin = 1f; float swap = width; width = height; height = swap; }
            else if (rotateToFit && height > width && rectWidth > rectHeight) { _cos = 0f; _sin = -1f; float swap = width; width = height; height = swap; }
            float availableW = rectWidth - 2f * padding, availableH = rectHeight - 2f * padding;
            float scale = Math.Min(availableW / width, availableH / height);
            _scaleX = scale / rectWidth; _scaleY = scale / rectHeight;
            _polyline = new MinimapPoint[path.Count];
            for (int i = 0; i < path.Count; i++) _polyline[i] = Project(path.Positions[i].x, path.Positions[i].z);
        }

        public MinimapPoint Project(float worldX, float worldZ)
        {
            float dx = worldX - _centerX, dz = worldZ - _centerZ;
            float x = dx * _cos - dz * _sin, z = dx * _sin + dz * _cos;
            return new MinimapPoint(0.5f + x * _scaleX, 0.5f + z * _scaleY);
        }
    }
}
