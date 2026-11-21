using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Match
{
    /// <summary>The small pieces every match HUD shares: a canvas, a text label and the restart tap.</summary>
    public static class MatchUi
    {
        public static Canvas CreateCanvas(Transform parent, string name)
        {
            var canvasObject = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static Text CreateLabel(Canvas canvas, string name, int size, TextAnchor alignment,
            Vector2 anchorPoint, Vector2 position, Vector2 extent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(canvas.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorPoint;
            rect.anchorMax = anchorPoint;
            rect.pivot = anchorPoint;
            rect.anchoredPosition = position;
            rect.sizeDelta = extent;

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.raycastTarget = false;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(3f, -3f);
            return text;
        }

        /// <summary>A key press or a tap: the "play again" gesture on a keyboard and on a phone.</summary>
        public static bool RestartRequested()
        {
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)
                || Input.touchCount > 0;
        }
    }
}
