using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// Scale With Screen Size that picks its own match value: a screen wider than the reference matches
    /// the height (nothing grows past the top and bottom), a narrower one matches the width (nothing is
    /// cut at the sides). One reference resolution then reads right from a 4:3 tablet to a 21:9 phone.
    /// </summary>
    [AddComponentMenu("Festival Derby/Menu/Aspect Adaptive Canvas Scaler")]
    public sealed class AspectAdaptiveCanvasScaler : CanvasScaler
    {
        // CanvasScaler calls Handle before every canvas render, so a resized Game view or a rotation is
        // followed without a second per-frame hook of our own.
        protected override void Handle()
        {
            if (uiScaleMode == ScaleMode.ScaleWithScreenSize && Screen.height > 0 && referenceResolution.y > 0f)
            {
                float reference = referenceResolution.x / referenceResolution.y;
                float actual = (float)Screen.width / Screen.height;
                matchWidthOrHeight = actual > reference ? 1f : 0f;
            }

            base.Handle();
        }
    }
}
