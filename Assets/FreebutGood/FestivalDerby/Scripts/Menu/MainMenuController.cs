using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FestivalDerby.Menu
{
    /// <summary>
    /// Wires the main menu together: the lists and steppers write into <see cref="MenuSession"/>, the Play
    /// button saves it and loads the Gameplay scene. It holds no layout and no tuning, so everything it
    /// points at can be restyled, moved or replaced in the scene. Every reference is optional.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string VolumePref = "fd.menu.volume";

        [Header("Choices")]
        [SerializeField] private ChoiceList _modes;
        [SerializeField] private ChoiceList _maps;
        [SerializeField] private ChoiceList _cars;
        [SerializeField] private ChoiceList _botLevels;
        [SerializeField] private Stepper _laps;
        [SerializeField] private Stepper _bots;
        [SerializeField] private TabGroup _setupTabs;

        [Header("Garage")]
        [SerializeField] private CarPreview _preview;
        [SerializeField] private LocalizedText _carName;
        [SerializeField] private Button _previousCar;
        [SerializeField] private Button _nextCar;

        [Header("Play")]
        [SerializeField] private Button _playButton;
        [SerializeField] private LocalizedText _playLabel;
        [Tooltip("One-line recap above Play. Written only by this controller.")]
        [SerializeField] private Text _summary;
        [Tooltip("Scene Play loads. It must be in Build Settings; until then Play only logs a warning.")]
        [SerializeField] private string _gameplaySceneName = "Gameplay";

        [Header("Settings")]
        [SerializeField] private Button _settingsButton;
        [SerializeField] private ModalPanel _settings;
        [SerializeField] private Slider _volume;
        [SerializeField] private Button _languageEnglish;
        [SerializeField] private Button _languageSpanish;
        [SerializeField] private Button _quit;

        private MenuSelection _selection;
        private bool _loading;

        private void OnEnable()
        {
            _selection = MenuSession.Current;

            Hook();
            ApplySelection();
            ApplySettings();
            Loc.Changed += RefreshSummary;
        }

        private void OnDisable()
        {
            Loc.Changed -= RefreshSummary;
            Unhook();

            // A scene load that never finished must not leave Play stuck on "Loading".
            _loading = false;
            RefreshPlay();
        }

        // ------------------------------------------------------------------ applying state

        private void ApplySelection()
        {
            if (_modes != null)
                _modes.SelectById(_selection.ModeId, false);
            if (_maps != null)
                _maps.SelectById(_selection.MapId, false);
            if (_cars != null)
                _cars.SelectById(_selection.CarId, false);
            if (_botLevels != null)
                _botLevels.Select((int)_selection.BotLevel, false);

            if (_laps != null)
            {
                _laps.SetRange(MenuSelection.MinLaps, MenuSelection.MaxLaps);
                _laps.SetValue(_selection.Laps, false);
            }

            if (_bots != null)
            {
                _bots.SetRange(0, MenuSelection.MaxBots);
                _bots.SetValue(_selection.BotCount, false);
            }

            // The saved ids may have been replaced by a fallback; store what is actually on screen.
            CommitChoices();
            ShowCar();
            RefreshSummary();
            RefreshPlay();
        }

        private void ApplySettings()
        {
            float volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePref, 1f));
            AudioListener.volume = volume;
            if (_volume != null)
            {
                _volume.minValue = 0f;
                _volume.maxValue = 1f;
                _volume.SetValueWithoutNotify(volume);
            }

            // Quitting from inside a phone app is not something the platform wants.
            if (_quit != null)
                _quit.gameObject.SetActive(!Application.isMobilePlatform);
        }

        private void CommitChoices()
        {
            if (_modes != null && _modes.SelectedOption != null)
                _selection.ModeId = _modes.SelectedOption.Id;
            if (_maps != null && _maps.SelectedOption != null)
                _selection.MapId = _maps.SelectedOption.Id;
            if (_cars != null && _cars.SelectedOption != null)
                _selection.CarId = _cars.SelectedOption.Id;
            if (_botLevels != null && _botLevels.SelectedIndex >= 0)
                _selection.BotLevel = (BotLevel)_botLevels.SelectedIndex;
            if (_laps != null)
                _selection.Laps = _laps.Value;
            if (_bots != null)
                _selection.BotCount = _bots.Value;

            _selection.Clamp();
        }

        private void ShowCar()
        {
            MenuOption car = _cars != null ? _cars.SelectedOption : null;
            if (_preview != null)
                _preview.Show(car != null ? car.Model : null);
            if (_carName != null && car != null)
                _carName.SetKey(car.TitleKey);
        }

        private void RefreshSummary()
        {
            if (_summary == null)
                return;

            MenuOption map = _maps != null ? _maps.SelectedOption : null;
            string mapName = map != null ? Loc.Get(map.TitleKey) : "-";
            _summary.text = Loc.Get("menu.summary", mapName, _selection.Laps, _selection.BotCount);
        }

        private bool CanPlay()
        {
            MenuOption car = _cars != null ? _cars.SelectedOption : null;
            MenuOption map = _maps != null ? _maps.SelectedOption : null;
            bool carOk = _cars == null || (car != null && !car.Locked);
            bool mapOk = _maps == null || (map != null && !map.Locked);
            return carOk && mapOk;
        }

        private void RefreshPlay()
        {
            if (_playButton != null)
                _playButton.interactable = !_loading && CanPlay();
            if (_playLabel != null)
                _playLabel.SetKey(_loading ? "menu.loading" : "menu.play");
        }

        // ------------------------------------------------------------------ events

        private void Hook()
        {
            if (_modes != null) _modes.Selected += OnChoice;
            if (_maps != null) _maps.Selected += OnChoice;
            if (_cars != null) _cars.Selected += OnChoice;
            if (_botLevels != null) _botLevels.Selected += OnChoice;
            if (_laps != null) _laps.Changed += OnNumber;
            if (_bots != null) _bots.Changed += OnNumber;

            if (_previousCar != null) _previousCar.onClick.AddListener(OnPreviousCar);
            if (_nextCar != null) _nextCar.onClick.AddListener(OnNextCar);
            if (_playButton != null) _playButton.onClick.AddListener(OnPlay);
            if (_settingsButton != null) _settingsButton.onClick.AddListener(OnOpenSettings);
            if (_volume != null) _volume.onValueChanged.AddListener(OnVolume);
            if (_languageEnglish != null) _languageEnglish.onClick.AddListener(OnEnglish);
            if (_languageSpanish != null) _languageSpanish.onClick.AddListener(OnSpanish);
            if (_quit != null) _quit.onClick.AddListener(OnQuit);
        }

        private void Unhook()
        {
            if (_modes != null) _modes.Selected -= OnChoice;
            if (_maps != null) _maps.Selected -= OnChoice;
            if (_cars != null) _cars.Selected -= OnChoice;
            if (_botLevels != null) _botLevels.Selected -= OnChoice;
            if (_laps != null) _laps.Changed -= OnNumber;
            if (_bots != null) _bots.Changed -= OnNumber;

            if (_previousCar != null) _previousCar.onClick.RemoveListener(OnPreviousCar);
            if (_nextCar != null) _nextCar.onClick.RemoveListener(OnNextCar);
            if (_playButton != null) _playButton.onClick.RemoveListener(OnPlay);
            if (_settingsButton != null) _settingsButton.onClick.RemoveListener(OnOpenSettings);
            if (_volume != null) _volume.onValueChanged.RemoveListener(OnVolume);
            if (_languageEnglish != null) _languageEnglish.onClick.RemoveListener(OnEnglish);
            if (_languageSpanish != null) _languageSpanish.onClick.RemoveListener(OnSpanish);
            if (_quit != null) _quit.onClick.RemoveListener(OnQuit);
        }

        private void OnChoice(int index, MenuOption option)
        {
            CommitChoices();
            MenuSession.Save();
            ShowCar();
            RefreshSummary();
            RefreshPlay();
        }

        private void OnNumber(int value)
        {
            CommitChoices();
            MenuSession.Save();
            RefreshSummary();
        }

        private void OnPreviousCar()
        {
            if (_cars != null)
                _cars.SelectRelative(-1, true);
        }

        private void OnNextCar()
        {
            if (_cars != null)
                _cars.SelectRelative(1, true);
        }

        private void OnOpenSettings()
        {
            if (_settings != null)
                _settings.Open();
        }

        private void OnVolume(float value)
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(VolumePref, value);
        }

        private void OnEnglish()
        {
            Loc.SetLanguage(Language.English);
        }

        private void OnSpanish()
        {
            Loc.SetLanguage(Language.Spanish);
        }

        private void OnQuit()
        {
            Application.Quit();
        }

        private void OnPlay()
        {
            if (_loading || !CanPlay())
                return;

            CommitChoices();
            MenuSession.Save();

            if (!Application.CanStreamedLevelBeLoaded(_gameplaySceneName))
            {
                Debug.LogWarning("[menu] Scene '" + _gameplaySceneName + "' is not in Build Settings, so Play " +
                    "cannot load it. Add it there when you want Play to start a race.", this);
                return;
            }

            _loading = true;
            MenuSession.MarkLaunched();
            RefreshPlay();
            SceneManager.LoadSceneAsync(_gameplaySceneName);
        }
    }
}
