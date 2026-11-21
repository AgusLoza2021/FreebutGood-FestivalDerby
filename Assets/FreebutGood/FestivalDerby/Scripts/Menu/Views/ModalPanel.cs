using UnityEngine;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// Opens and closes a dialog. This component sits on an object that stays active; <c>_content</c> is
    /// the child that holds the backdrop and the window and starts hidden.
    /// </summary>
    public sealed class ModalPanel : MonoBehaviour
    {
        [SerializeField] private GameObject _content;
        [Tooltip("Any of these closes the dialog: the Close button, the dimmed backdrop.")]
        [SerializeField] private Button[] _closeButtons = new Button[0];

        public bool IsOpen
        {
            get { return _content != null && _content.activeSelf; }
        }

        public void Open()
        {
            if (_content != null)
                _content.SetActive(true);
        }

        public void Close()
        {
            if (_content != null)
                _content.SetActive(false);
        }

        private void OnEnable()
        {
            for (int i = 0; i < _closeButtons.Length; i++)
                if (_closeButtons[i] != null)
                    _closeButtons[i].onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            for (int i = 0; i < _closeButtons.Length; i++)
                if (_closeButtons[i] != null)
                    _closeButtons[i].onClick.RemoveListener(Close);

            // A dialog left open when the screen goes away would reopen with it.
            Close();
        }
    }
}
