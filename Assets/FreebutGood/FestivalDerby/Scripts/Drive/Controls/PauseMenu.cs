using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FestivalDerby.Drive
{
    /// <summary>
    /// The in-game way out: a pause button in the top-left corner that stops the game and offers Resume,
    /// Restart and Main Menu. Escape toggles it too, which is also the Android back button. Built in code
    /// like the touch controls, so the gameplay scene needs nothing authored for it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenu : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.12f, 0.78f);
        private static readonly Color ButtonColor = new Color(0.13f, 0.19f, 0.30f, 0.95f);
        private static readonly Color AccentColor = new Color(0.20f, 0.42f, 0.86f, 1f);

        private string _menuScene;
        private GameObject _overlay;
        private float _timeScaleBeforePause = 1f;

        public bool IsOpen { get; private set; }

        /// <summary>The pause button and its (hidden) menu, under <paramref name="parent"/>.</summary>
        public static PauseMenu Build(Transform parent, string menuScene)
        {
            var canvasObject = new GameObject("PauseMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the touch controls and the match HUD: an open menu must take every touch.
            canvas.sortingOrder = 200;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // uGUI needs exactly one EventSystem to deliver clicks; reuse a scene one when present.
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(parent, false);
            }

            PauseMenu menu = canvasObject.AddComponent<PauseMenu>();
            menu._menuScene = menuScene;
            menu.BuildUi(canvas.transform);
            return menu;
        }

        public void Open()
        {
            if (IsOpen)
                return;

            IsOpen = true;
            _timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            _overlay.SetActive(true);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            Time.timeScale = _timeScaleBeforePause;
            _overlay.SetActive(false);
        }

        public void Restart()
        {
            Close();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void GoToMainMenu()
        {
            Close();
            SceneManager.LoadScene(_menuScene);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (IsOpen)
                    Close();
                else
                    Open();
            }
        }

        // A paused game that is unloaded (scene change, test teardown) must not leave time stopped.
        private void OnDestroy()
        {
            if (IsOpen)
                Time.timeScale = _timeScaleBeforePause;
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi(Transform canvas)
        {
            Button pause = MakeButton(canvas, "PauseButton", "II", ButtonColor, 40);
            Place(pause.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(110f, 80f));
            pause.onClick.AddListener(Open);

            _overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            _overlay.transform.SetParent(canvas, false);
            var overlayRect = _overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            _overlay.GetComponent<Image>().color = PanelColor;

            Text title = MakeText(_overlay.transform, "Title", "PAUSED", 96);
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(900f, 140f));

            Button resume = MakeButton(_overlay.transform, "Resume", "RESUME", AccentColor, 52);
            Place(resume.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(620f, 130f));
            resume.onClick.AddListener(Close);

            Button restart = MakeButton(_overlay.transform, "Restart", "RESTART", ButtonColor, 52);
            Place(restart.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(620f, 130f));
            restart.onClick.AddListener(Restart);

            Button menu = MakeButton(_overlay.transform, "MainMenu", "MAIN MENU", ButtonColor, 52);
            Place(menu.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(620f, 130f));
            menu.onClick.AddListener(GoToMainMenu);

            _overlay.SetActive(false);
        }

        private static Button MakeButton(Transform parent, string name, string caption, Color color, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            Text label = MakeText(go.transform, "Caption", caption, fontSize);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static Text MakeText(Transform parent, string name, string caption, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = caption;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
