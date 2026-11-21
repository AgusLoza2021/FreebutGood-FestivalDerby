using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// Builds the on-screen controls in code: a stick on the left, BRAKE / NITRO / RESET on the right.
    /// Code instead of a prefab so the whole drive scene stays one readable script with no hidden
    /// Inspector wiring. Everything it creates lives under one canvas and is destroyed with it.
    /// </summary>
    public sealed class TouchHud
    {
        private static readonly Color FaceColor = new Color(0.10f, 0.12f, 0.16f, 0.70f);
        private static readonly Color AccentColor = new Color(0.95f, 0.55f, 0.10f, 0.85f);
        private static readonly Color TextColor = Color.white;

        public Canvas Canvas { get; private set; }
        public TouchStick Stick { get; private set; }
        public HoldButton Brake { get; private set; }
        public HoldButton Nitro { get; private set; }
        public HoldButton Reset { get; private set; }

        private Texture2D _discTexture;
        private Sprite _discSprite;

        public static TouchHud Build(Transform parent)
        {
            var hud = new TouchHud();
            hud.BuildCanvas(parent);
            hud.BuildStick();
            hud.Brake = hud.BuildButton("Brake", "BRAKE", FaceColor, new Vector2(-24f, 24f), new Vector2(180f, 132f));
            hud.Nitro = hud.BuildButton("Nitro", "NITRO", AccentColor, new Vector2(-24f, 168f), new Vector2(180f, 64f));
            hud.Reset = hud.BuildButton("Reset", "RESET", FaceColor, new Vector2(-24f, -24f), new Vector2(140f, 56f),
                top: true);
            return hud;
        }

        public void Destroy()
        {
            if (Canvas != null)
                Object.Destroy(Canvas.gameObject);
            if (_discSprite != null)
                Object.Destroy(_discSprite);
            if (_discTexture != null)
                Object.Destroy(_discTexture);
        }

        private void BuildCanvas(Transform parent)
        {
            var canvasObject = new GameObject("TouchHud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            Canvas = canvasObject.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            // uGUI needs exactly one EventSystem to deliver touches; reuse a scene one when present.
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(canvasObject.transform, false);
            }
        }

        private void BuildStick()
        {
            Image plate = Box(Canvas.transform, "StickArea", new Color(1f, 1f, 1f, 0.16f));
            plate.sprite = DiscSprite();
            plate.raycastTarget = true;
            Place(plate.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(184f, 184f),
                new Vector2(320f, 320f));

            Image handle = Box(plate.transform, "StickHandle", new Color(1f, 1f, 1f, 0.72f));
            handle.sprite = DiscSprite();
            Place(handle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(128f, 128f));

            Stick = plate.gameObject.AddComponent<TouchStick>();
            Stick.Area = plate.rectTransform;
            Stick.Handle = handle.rectTransform;
            Stick.Radius = 78f;
        }

        private HoldButton BuildButton(string name, string caption, Color face, Vector2 position, Vector2 size,
            bool top = false)
        {
            Image image = Box(Canvas.transform, name, face);
            image.raycastTarget = true;
            Vector2 corner = top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
            Place(image.rectTransform, corner, corner, position, size);

            var labelObject = new GameObject("Caption", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(image.transform, false);
            var label = labelObject.GetComponent<Text>();
            label.text = caption;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 28;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = TextColor;
            label.raycastTarget = false;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return image.gameObject.AddComponent<HoldButton>();
        }

        private static Image Box(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>A white disc with a soft dark rim, generated once instead of shipping a texture.</summary>
        private Sprite DiscSprite()
        {
            if (_discSprite != null)
                return _discSprite;

            const int pixels = 256;
            _discTexture = new Texture2D(pixels, pixels, TextureFormat.RGBA32, true)
            {
                name = "TouchHudDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float radius = pixels * 0.5f;
            float inner = radius * 0.88f;
            var buffer = new Color32[pixels * pixels];
            var fill = new Color(1f, 1f, 1f, 0.9f);
            var rim = new Color(0f, 0f, 0f, 0.5f);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < pixels; y++)
            {
                for (int x = 0; x < pixels; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    buffer[y * pixels + x] = distance > radius ? clear : distance > inner ? rim : fill;
                }
            }

            _discTexture.SetPixels32(buffer);
            _discTexture.Apply(true, false);
            _discSprite = Sprite.Create(_discTexture, new Rect(0f, 0f, pixels, pixels), new Vector2(0.5f, 0.5f),
                pixels, 0, SpriteMeshType.FullRect);
            _discSprite.name = "TouchHudDisc";
            return _discSprite;
        }
    }
}
