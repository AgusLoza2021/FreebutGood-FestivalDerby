using System.IO;
using UnityEditor;
using UnityEngine;

namespace FestivalDerby.World.Editor
{
    /// <summary>
    /// Builds the road's surface art: one placeholder albedo per <see cref="SurfaceId"/>, the
    /// <see cref="Texture2DArray"/> the shader samples, and the material the bake stores on the map.
    ///
    /// It only ever creates art that is missing, so replacing a placeholder with real art and
    /// rebuilding keeps the real art. The array index is the SurfaceId byte itself — the bake writes
    /// that byte into the mesh's vertex colour and the shader reads it straight back — so this order
    /// is a wire format, not a preference: change it and every baked map points at the wrong albedo.
    /// </summary>
    public static class RoadSurfaceArt
    {
        public const string ShaderName = "FestivalDerby/RoadSurface";
        public const string ArtFolder = "Assets/FreebutGood/FestivalDerby/Art/World";
        public const string SurfacesFolder = ArtFolder + "/Surfaces";
        public const string ArrayPath = ArtFolder + "/RoadSurfaces.asset";
        public const string MaterialPath = ArtFolder + "/RoadSurface.mat";
        public const string AlbedoProperty = "_SurfaceAlbedos";
        public const string TilingProperty = "_Tiling";
        public const int TextureSize = 128;

        /// <summary>Every layer, in the array order the shader indexes by SurfaceId.</summary>
        public static readonly SurfaceId[] Layers =
        {
            SurfaceId.Street,
            SurfaceId.Dirt,
            SurfaceId.Snow,
            SurfaceId.Mud,
            SurfaceId.Gravel,
        };

        private static readonly Color[] BaseColors =
        {
            new Color(0.16f, 0.17f, 0.19f), // Street
            new Color(0.42f, 0.30f, 0.19f), // Dirt
            new Color(0.88f, 0.90f, 0.94f), // Snow
            new Color(0.28f, 0.20f, 0.13f), // Mud
            new Color(0.52f, 0.50f, 0.46f), // Gravel
        };

        private static readonly float[] GrainAmounts = { 0.10f, 0.22f, 0.06f, 0.18f, 0.30f };

        [MenuItem("Festival Derby/World/Build Road Surface Art", false, 20)]
        public static void BuildAll()
        {
            EnsureFolder(ArtFolder);
            EnsureFolder(SurfacesFolder);

            var slices = new Texture2D[Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
            {
                string path = SlicePath(Layers[i]);
                if (!File.Exists(path))
                    WritePlaceholder(path, BaseColors[i], GrainAmounts[i], i);

                ConfigureImporter(path);
                slices[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (slices[i] == null)
                    Debug.LogError($"[RoadSurfaceArt] Could not import the surface texture at {path}.");
            }

            Texture2DArray array = BuildArray(slices);
            if (array == null)
                return;

            // SaveAssetIfDirty, never SaveAssets: the global flush also persists whatever the owner left dirty,
            // and one bake wrote 70k lines of preview objects into Gameplay.unity that way.
            AssetDatabase.CreateAsset(array, ArrayPath);
            AssetDatabase.SaveAssetIfDirty(array);

            Material material = EnsureMaterial();
            if (material != null)
            {
                material.SetTexture(AlbedoProperty, array);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[RoadSurfaceArt] Built {Layers.Length} surface albedos into {ArrayPath}.");
        }

        /// <summary>
        /// The material the bake stores on the map. It is created once and reused, so a re-bake
        /// neither allocates a new material nor loses a manual tweak. Returns null when the shader is
        /// not imported yet, and the bake then leaves the map without a material rather than writing a
        /// broken reference.
        /// </summary>
        public static Material EnsureMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[RoadSurfaceArt] Shader '{ShaderName}' not found. " +
                    "The road will fall back to a plain URP material until it imports.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                EnsureFolder(ArtFolder);
                material = new Material(shader) { name = "RoadSurface" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(ArrayPath);
            if (array != null)
                material.SetTexture(AlbedoProperty, array);
            return material;
        }

        /// <summary>Sets the material's tiling from the bake resolution, so the art follows the mesh UVs.</summary>
        public static void ApplyTiling(Material material, RoadMeshSettings settings)
        {
            if (material == null)
                return;
            settings = settings.Sanitized();
            material.SetFloat(TilingProperty, 1f / settings.MetersPerTile);
            EditorUtility.SetDirty(material);
        }

        public static string SlicePath(SurfaceId id)
        {
            return $"{SurfacesFolder}/{id}.png";
        }

        // ------------------------------------------------------------------ building

        private static Texture2DArray BuildArray(Texture2D[] slices)
        {
            int depth = 0;
            for (int i = 0; i < Layers.Length; i++)
                depth = Mathf.Max(depth, (int)Layers[i] + 1);

            for (int i = 0; i < slices.Length; i++)
            {
                if (slices[i] == null)
                    return null;
                if (slices[i].width != TextureSize || slices[i].height != TextureSize)
                {
                    Debug.LogError($"[RoadSurfaceArt] {slices[i].name} must be {TextureSize}x{TextureSize}.");
                    return null;
                }
            }

            var array = new Texture2DArray(TextureSize, TextureSize, depth, TextureFormat.RGBA32, true, false)
            {
                name = "RoadSurfaces",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            for (int i = 0; i < Layers.Length; i++)
            {
                // Slice index is the SurfaceId byte: that correspondence is the shader contract.
                array.SetPixels(slices[i].GetPixels(), (int)Layers[i]);
            }
            array.Apply(true, false);
            return array;
        }

        private static void WritePlaceholder(string path, Color baseColor, float grain, int seed)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            var pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    // Two octaves of wrapping value noise give a surface some large-scale structure;
                    // the per-pixel hash keeps a fine grain that survives being stretched by a UV tile.
                    float coarse = ValueNoise(x / (float)TextureSize, y / (float)TextureSize, 8, seed);
                    float fine = ValueNoise(x / (float)TextureSize, y / (float)TextureSize, 32, seed + 7);
                    float shade = 1f + (coarse - 0.5f) * grain + (fine - 0.5f) * grain * 0.5f;
                    pixels[y * TextureSize + x] = new Color(
                        Mathf.Clamp01(baseColor.r * shade),
                        Mathf.Clamp01(baseColor.g * shade),
                        Mathf.Clamp01(baseColor.b * shade),
                        1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // Readable is required to copy the pixels into the array; the array is what ships.
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ noise

        private static float ValueNoise(float u, float v, int cells, int seed)
        {
            float x = u * cells;
            float y = v * cells;
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            // Smoothstep the interpolation so the cells read as blobs, not a grid.
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float a = Hash01(Wrap(x0, cells), Wrap(y0, cells), seed);
            float b = Hash01(Wrap(x0 + 1, cells), Wrap(y0, cells), seed);
            float c = Hash01(Wrap(x0, cells), Wrap(y0 + 1, cells), seed);
            float d = Hash01(Wrap(x0 + 1, cells), Wrap(y0 + 1, cells), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static int Wrap(int value, int size)
        {
            int wrapped = value % size;
            return wrapped < 0 ? wrapped + size : wrapped;
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0xFFFFFF;
            }
        }
    }
}
