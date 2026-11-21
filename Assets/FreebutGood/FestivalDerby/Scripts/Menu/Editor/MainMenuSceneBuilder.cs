using System;
using System.Collections.Generic;
using System.IO;
using FestivalDerby.Driving.Runtime;
using FestivalDerby.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FestivalDerby.Menu.Editor
{
    /// <summary>
    /// Builds the main menu scene (Scenes/MainMenu.unity) and the string table it reads.
    ///
    /// DOES: create every node that is missing, looking each one up by name in its direct parent first;
    /// give a new node its look; wire serialized references that are still empty; fill an option list
    /// that is still empty from the project's maps and car catalog; add the strings that are missing.
    ///
    /// DOES NOT: touch a node that already exists (move it, restyle it, rename it, replace it), overwrite
    /// a reference or an option list you edited, change Build Settings, or switch anything on: the scene
    /// is not in the build until you add it, so Play only logs a warning until then. Use
    /// "Rebuild Main Menu From Scratch" (which backs the scene up first) to regenerate it.
    ///
    /// Positions are written in the 1920x1080 reference space the canvas scales from, anchored to the
    /// edge they hug, so the same numbers read right at any aspect.
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        public const string ScenePath = "Assets/FreebutGood/FestivalDerby/Scenes/MainMenu.unity";
        private const string StringsPath = "Assets/FreebutGood/FestivalDerby/Resources/Menu/MenuStrings.asset";
        private const string DrivingPath = "Assets/FreebutGood/FestivalDerby/Resources/DrivingFeatureConfig.asset";
        private const string MapsFolder = "Assets/FreebutGood/FestivalDerby/Resources/Maps";

        // Reference space and palette.
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        private static readonly Color BackgroundColor = new Color(0.07f, 0.11f, 0.20f, 1f);
        private static readonly Color PanelColor = new Color(0.10f, 0.15f, 0.26f, 0.90f);
        private static readonly Color CardColor = new Color(0.13f, 0.19f, 0.30f, 0.92f);
        private static readonly Color AccentColor = new Color(1f, 0.80f, 0.10f, 1f);
        private static readonly Color AccentTextColor = new Color(0.25f, 0.16f, 0.02f, 1f);
        private static readonly Color TextLight = new Color(0.95f, 0.97f, 1f, 1f);
        private static readonly Color TextMuted = new Color(0.70f, 0.78f, 0.92f, 1f);

        private static readonly Vector2 TL = new Vector2(0f, 1f);
        private static readonly Vector2 TR = new Vector2(1f, 1f);
        private static readonly Vector2 BL = new Vector2(0f, 0f);
        private static readonly Vector2 BR = new Vector2(1f, 0f);
        private static readonly Vector2 Mid = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 TopMid = new Vector2(0.5f, 1f);

        // key, English, Spanish. Neutral Spanish on purpose: this is UI copy, not chat.
        private static readonly string[][] BaseStrings =
        {
            new[] { "menu.title", "Festival Derby", "Festival Derby" },
            new[] { "menu.play", "PLAY", "JUGAR" },
            new[] { "menu.loading", "Loading...", "Cargando..." },
            new[] { "menu.settings", "Settings", "Configuración" },
            new[] { "menu.close", "Close", "Cerrar" },
            new[] { "menu.quit", "Quit", "Salir" },
            new[] { "menu.locked", "Locked", "Bloqueado" },
            new[] { "menu.mode.title", "Game mode", "Modo de juego" },
            new[] { "mode.race", "Race", "Carrera" },
            new[] { "mode.race.desc", "Race against bots over several laps.", "Compite contra bots durante varias vueltas." },
            new[] { "mode.truck", "Truck Chase", "Persecución del camión" },
            new[] { "mode.truck.desc", "Be the first to ride the truck for 3 seconds. The last car is knocked out.", "Sé el primero en subirte al camión 3 segundos. El último auto queda eliminado." },
            new[] { "mode.freedrive", "Free Drive", "Conducción libre" },
            new[] { "mode.freedrive.desc", "Explore the circuit with no pressure.", "Recorre el circuito sin presión." },
            new[] { "mode.timetrial", "Time Trial", "Contrarreloj" },
            new[] { "mode.timetrial.desc", "Beat your best lap.", "Supera tu mejor vuelta." },
            new[] { "menu.garage", "Garage", "Garaje" },
            new[] { "menu.setup.map", "Map", "Mapa" },
            new[] { "menu.setup.laps", "Laps", "Vueltas" },
            new[] { "menu.setup.bots", "Bots", "Bots" },
            new[] { "menu.laps.count", "Number of laps", "Número de vueltas" },
            new[] { "menu.bots.count", "Number of bots", "Número de bots" },
            new[] { "menu.bots.level", "Difficulty", "Dificultad" },
            new[] { "bot.easy", "Easy", "Fácil" },
            new[] { "bot.medium", "Medium", "Media" },
            new[] { "bot.hard", "Hard", "Difícil" },
            new[] { "menu.summary", "{0} · {1} laps · {2} bots", "{0} · {1} vueltas · {2} bots" },
            new[] { "menu.settings.volume", "Volume", "Volumen" },
            new[] { "menu.settings.language", "Language", "Idioma" },
            new[] { "lang.en", "English", "English" },
            new[] { "lang.es", "Español", "Español" },
        };

        // State of one run. Cleared at the start of Build so a second run never sees the first one's nodes.
        private static LocalizationTable _table;
        private static readonly HashSet<int> MadeNow = new HashSet<int>();
        private static readonly Dictionary<ChoiceList, RectTransform> CardParents = new Dictionary<ChoiceList, RectTransform>();

        [MenuItem("Festival Derby/Menu/Build Main Menu")]
        public static void Build()
        {
            MadeNow.Clear();
            CardParents.Clear();

            List<MenuOption> maps = CollectMaps();
            List<MenuOption> cars = CollectCars();
            _table = EnsureStrings(maps, cars);
            Scene scene = OpenOrCreate();

            Transform root = EnsureRoot(scene, "MainMenu");
            var controller = Ensure<MainMenuController>(root.gameObject);

            RectTransform canvas = BuildCanvas(root);
            RectTransform safe = BuildSafeArea(canvas);

            Button settingsButton = BuildTopBar(safe);
            ChoiceList modes = BuildModes(safe);

            TabGroup setup;
            ChoiceList mapList, botLevels;
            Stepper laps, bots;
            BuildSetup(safe, out setup, out mapList, out laps, out bots, out botLevels);

            RawImage heroView;
            Text carName;
            Button previous, next;
            BuildHero(safe, out heroView, out carName, out previous, out next);
            ChoiceList carList = BuildSkins(safe);

            Button play;
            Text playLabel, summary;
            BuildPlay(safe, out play, out playLabel, out summary);

            ModalPanel settings;
            Slider volume;
            Button english, spanish, quit;
            BuildSettings(canvas, out settings, out volume, out english, out spanish, out quit);

            CarPreview preview = BuildPreviewRig(scene, heroView);
            BuildCameraAndEvents(scene);

            FillOptions(modes, ModeOptions());
            FillOptions(mapList, maps);
            FillOptions(botLevels, BotLevelOptions());
            FillOptions(carList, cars);
            FillCards(modes, 96f, 34, false);
            FillCards(mapList, 110f, 32, false);
            FillCards(botLevels, 96f, 28, true);
            FillCards(carList, 170f, 26, true);

            SetIfNull(controller, "_modes", modes);
            SetIfNull(controller, "_maps", mapList);
            SetIfNull(controller, "_cars", carList);
            SetIfNull(controller, "_botLevels", botLevels);
            SetIfNull(controller, "_laps", laps);
            SetIfNull(controller, "_bots", bots);
            SetIfNull(controller, "_setupTabs", setup);
            SetIfNull(controller, "_preview", preview);
            SetIfNull(controller, "_carName", carName.GetComponent<LocalizedText>());
            SetIfNull(controller, "_previousCar", previous);
            SetIfNull(controller, "_nextCar", next);
            SetIfNull(controller, "_playButton", play);
            SetIfNull(controller, "_playLabel", playLabel.GetComponent<LocalizedText>());
            SetIfNull(controller, "_summary", summary);
            SetIfNull(controller, "_settingsButton", settingsButton);
            SetIfNull(controller, "_settings", settings);
            SetIfNull(controller, "_volume", volume);
            SetIfNull(controller, "_languageEnglish", english);
            SetIfNull(controller, "_languageSpanish", spanish);
            SetIfNull(controller, "_quit", quit);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save " + ScenePath);

            AssetDatabase.SaveAssets();
            Debug.Log("[menu] wrote " + ScenePath + " (Build Settings untouched)");
        }

        [MenuItem("Festival Derby/Menu/Rebuild Main Menu From Scratch")]
        public static void Rebuild()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Rebuild Main Menu",
                "This discards every manual change in MainMenu.unity and builds it again. A backup copy is " +
                "kept next to it.", "Rebuild", "Cancel"))
                return;

            if (File.Exists(ScenePath))
            {
                string backup = AssetDatabase.GenerateUniqueAssetPath(
                    "Assets/FreebutGood/FestivalDerby/Scenes/MainMenu_Backup.unity");
                if (!AssetDatabase.CopyAsset(ScenePath, backup))
                    throw new InvalidOperationException("Could not back up " + ScenePath);
                AssetDatabase.DeleteAsset(ScenePath);
            }

            Build();
        }

        /// <summary>
        /// The one deliberate Build Settings change: MainMenu becomes the first scene (the one a build
        /// opens), and every other scene stays where it was. Run it yourself when you want a build to
        /// start at the menu; Build Main Menu never does it for you.
        /// </summary>
        [MenuItem("Festival Derby/Menu/Make Main Menu The First Build Scene")]
        public static void MakeFirstBuildScene()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[menu] " + ScenePath + " does not exist yet. Run Build Main Menu first.");
                return;
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[menu] MainMenu is now scene 0 in Build Settings (" + scenes.Count + " scenes).");
        }

        // ============================================================================ data

        private static List<MenuOption> CollectMaps()
        {
            var list = new List<MenuOption>();
            string[] guids = AssetDatabase.FindAssets("t:MapAsset", new[] { MapsFolder });
            Array.Sort(guids, (a, b) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

            foreach (string guid in guids)
            {
                var map = AssetDatabase.LoadAssetAtPath<MapAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (map == null)
                    continue;

                string id = string.IsNullOrEmpty(map.MapId) ? map.name : map.MapId;
                list.Add(new MenuOption { Id = id, TitleKey = "map." + id });
            }

            return list;
        }

        private static List<MenuOption> CollectCars()
        {
            var list = new List<MenuOption>();
            var driving = AssetDatabase.LoadAssetAtPath<DrivingFeatureConfig>(DrivingPath);
            if (driving == null || driving.Visuals == null || driving.Visuals.Entries == null)
                return list;

            foreach (CarVisualEntry entry in driving.Visuals.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id))
                    continue;

                // The catalog's own flag: a model whose Blender pass is not done is shown but not pickable.
                list.Add(new MenuOption
                {
                    Id = entry.Id,
                    TitleKey = "car." + entry.Id,
                    Model = entry.Model,
                    Locked = !entry.Approved || entry.Model == null,
                });
            }

            return list;
        }

        private static List<MenuOption> ModeOptions()
        {
            return new List<MenuOption>
            {
                new MenuOption { Id = MenuSelection.ModeRace, TitleKey = "mode.race", SubtitleKey = "mode.race.desc" },
                new MenuOption { Id = MenuSelection.ModeTruckChase, TitleKey = "mode.truck", SubtitleKey = "mode.truck.desc" },
                new MenuOption { Id = MenuSelection.ModeFreeDrive, TitleKey = "mode.freedrive", SubtitleKey = "mode.freedrive.desc" },
                new MenuOption { Id = MenuSelection.ModeTimeTrial, TitleKey = "mode.timetrial", SubtitleKey = "mode.timetrial.desc", Locked = true },
            };
        }

        private static List<MenuOption> BotLevelOptions()
        {
            return new List<MenuOption>
            {
                new MenuOption { Id = "Easy", TitleKey = "bot.easy" },
                new MenuOption { Id = "Medium", TitleKey = "bot.medium" },
                new MenuOption { Id = "Hard", TitleKey = "bot.hard" },
            };
        }

        private static LocalizationTable EnsureStrings(List<MenuOption> maps, List<MenuOption> cars)
        {
            var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(StringsPath);
            if (table == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StringsPath));
                table = ScriptableObject.CreateInstance<LocalizationTable>();
                AssetDatabase.CreateAsset(table, StringsPath);
            }

            var wanted = new List<string[]>(BaseStrings);
            foreach (MenuOption map in maps)
                wanted.Add(new[] { map.TitleKey, Pretty(map.Id), Pretty(map.Id) });
            foreach (MenuOption car in cars)
                wanted.Add(new[] { car.TitleKey, Pretty(car.Id), Pretty(car.Id) });

            var so = new SerializedObject(table);
            SerializedProperty entries = so.FindProperty("_entries");
            var present = new HashSet<string>();
            for (int i = 0; i < entries.arraySize; i++)
                present.Add(entries.GetArrayElementAtIndex(i).FindPropertyRelative("Key").stringValue);

            foreach (string[] row in wanted)
            {
                // A key already in the table is never rewritten: its text is yours to edit.
                if (!present.Add(row[0]))
                    continue;

                int slot = entries.arraySize;
                entries.arraySize = slot + 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(slot);
                entry.FindPropertyRelative("Key").stringValue = row[0];
                entry.FindPropertyRelative("English").stringValue = row[1];
                entry.FindPropertyRelative("Spanish").stringValue = row[2];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
            return table;
        }

        private static string Pretty(string id)
        {
            var text = new System.Text.StringBuilder(id.Length + 4);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '_')
                    c = ' ';
                else if (i > 0 && char.IsUpper(c) && char.IsLower(id[i - 1]))
                    text.Append(' ');
                text.Append(c);
            }

            return text.ToString();
        }

        private static string English(string key)
        {
            return string.IsNullOrEmpty(key) ? "" : Loc.Resolve(_table, key, Language.English);
        }

        // ============================================================================ scene

        private static Scene OpenOrCreate()
        {
            if (File.Exists(ScenePath))
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    throw new OperationCanceledException("Cancelled: the open scene has unsaved changes.");
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static Transform EnsureRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == name)
                    return go.transform;

            var created = new GameObject(name);
            if (created.scene != scene)
                SceneManager.MoveGameObjectToScene(created, scene);
            return created.transform;
        }

        private static RectTransform BuildCanvas(Transform root)
        {
            RectTransform canvas;
            if (Make(root, "Canvas", out canvas, typeof(Canvas), typeof(GraphicRaycaster)))
            {
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvas.gameObject.AddComponent<AspectAdaptiveCanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = ReferenceResolution;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
            }

            RectTransform background;
            if (Make(canvas, "Background", out background, typeof(Image)))
            {
                Stretch(background, 0, 0, 0, 0);
                var image = background.GetComponent<Image>();
                image.color = BackgroundColor;
                image.raycastTarget = false;
            }

            return canvas;
        }

        private static RectTransform BuildSafeArea(RectTransform canvas)
        {
            RectTransform safe;
            if (Make(canvas, "SafeArea", out safe))
                Stretch(safe, 0, 0, 0, 0);
            Ensure<SafeArea>(safe.gameObject);
            return safe;
        }

        // ---------------------------------------------------------------- top bar

        private static Button BuildTopBar(RectTransform safe)
        {
            RectTransform bar;
            if (Make(safe, "TopBar", out bar))
            {
                bar.anchorMin = new Vector2(0f, 1f);
                bar.anchorMax = new Vector2(1f, 1f);
                bar.pivot = TopMid;
                bar.anchoredPosition = Vector2.zero;
                bar.sizeDelta = new Vector2(0f, 96f);
            }

            RectTransform title = MakeText(bar, "Title", "menu.title", 54, TextLight, TextAnchor.MiddleLeft, FontStyle.BoldAndItalic);
            if (IsNew(title)) Place(title, TL, new Vector2(0f, 0.5f), 40f, -48f, 800f, 80f);

            RectTransform settings = MakeButton(bar, "SettingsButton", "menu.settings", CardColor, TextLight, 28);
            if (IsNew(settings)) Place(settings, TR, new Vector2(1f, 0.5f), -40f, -48f, 240f, 64f);
            return settings.GetComponent<Button>();
        }

        // ---------------------------------------------------------------- modes

        private static ChoiceList BuildModes(RectTransform safe)
        {
            RectTransform panel;
            if (Make(safe, "ModesPanel", out panel, typeof(CanvasGroup)))
            {
                Place(panel, TL, TL, 40f, -112f, 540f, 392f);
                AddEntrance(panel.gameObject, 0f);
            }

            RectTransform header = MakeText(panel, "Header", "menu.mode.title", 28, TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (IsNew(header)) Place(header, TL, TL, 4f, 0f, 520f, 44f);

            return MakeList(panel, "Modes", 0f, -52f, 540f, 340f, false, false, TL, TL);
        }

        // ---------------------------------------------------------------- setup (map / laps / bots)

        private static void BuildSetup(RectTransform safe, out TabGroup tabs, out ChoiceList maps, out Stepper laps,
            out Stepper bots, out ChoiceList botLevels)
        {
            RectTransform panel;
            if (Make(safe, "SetupPanel", out panel, typeof(CanvasGroup)))
            {
                Place(panel, TL, TL, 40f, -520f, 540f, 500f);
                AddEntrance(panel.gameObject, 0.06f);
            }

            RectTransform tabRow;
            if (Make(panel, "Tabs", out tabRow, typeof(HorizontalLayoutGroup)))
            {
                Place(tabRow, TL, TL, 0f, 0f, 540f, 64f);
                var layout = tabRow.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 8f;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            }

            RectTransform tabMap = MakeButton(tabRow, "Tab_Map", "menu.setup.map", CardColor, TextLight, 26);
            RectTransform tabLaps = MakeButton(tabRow, "Tab_Laps", "menu.setup.laps", CardColor, TextLight, 26);
            RectTransform tabBots = MakeButton(tabRow, "Tab_Bots", "menu.setup.bots", CardColor, TextLight, 26);

            RectTransform content;
            if (Make(panel, "Content", out content, typeof(Image)))
            {
                Place(content, TL, TL, 0f, -76f, 540f, 424f);
                StylePanel(content.GetComponent<Image>(), PanelColor);
            }

            // Map tab: a scrolling list, so adding maps never overflows the panel.
            RectTransform mapPanel;
            if (Make(content, "MapPanel", out mapPanel))
                Stretch(mapPanel, 16, 16, 16, 16);
            maps = MakeList(mapPanel, "Maps", 0f, 0f, 508f, 392f, false, true, TL, TL);

            // Laps tab.
            RectTransform lapsPanel;
            if (Make(content, "LapsPanel", out lapsPanel))
                Stretch(lapsPanel, 16, 16, 16, 16);
            RectTransform lapsLabel = MakeText(lapsPanel, "Label", "menu.laps.count", 28, TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (IsNew(lapsLabel)) Place(lapsLabel, TopMid, TopMid, 0f, -40f, 500f, 48f);
            laps = MakeStepper(lapsPanel, "LapsStepper", 0f, -20f, "3");

            // Bots tab: how many, and how hard.
            RectTransform botsPanel;
            if (Make(content, "BotsPanel", out botsPanel))
                Stretch(botsPanel, 16, 16, 16, 16);
            RectTransform countLabel = MakeText(botsPanel, "CountLabel", "menu.bots.count", 28, TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (IsNew(countLabel)) Place(countLabel, TopMid, TopMid, 0f, -20f, 500f, 44f);
            bots = MakeStepper(botsPanel, "BotsStepper", 0f, 70f, "5");
            RectTransform levelLabel = MakeText(botsPanel, "LevelLabel", "menu.bots.level", 28, TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (IsNew(levelLabel)) Place(levelLabel, Mid, Mid, 0f, -50f, 500f, 44f);
            botLevels = MakeList(botsPanel, "BotLevels", 0f, 0f, 508f, 110f, true, false, BL, BL);

            tabs = Ensure<TabGroup>(panel.gameObject);
            SetArrayIfEmpty(tabs, "_tabs", new Object[] { tabMap.GetComponent<Button>(), tabLaps.GetComponent<Button>(), tabBots.GetComponent<Button>() });
            SetArrayIfEmpty(tabs, "_panels", new Object[] { mapPanel.gameObject, lapsPanel.gameObject, botsPanel.gameObject });
        }

        // ---------------------------------------------------------------- hero (garage preview)

        private static void BuildHero(RectTransform safe, out RawImage view, out Text carName, out Button previous,
            out Button next)
        {
            RectTransform hero;
            if (Make(safe, "Hero", out hero, typeof(CanvasGroup)))
            {
                Place(hero, TR, TR, -40f, -112f, 1160f, 640f);
                AddEntrance(hero.gameObject, 0.12f);
            }

            RectTransform preview;
            if (Make(hero, "Preview", out preview, typeof(RawImage)))
            {
                Stretch(preview, 0, 70, 0, 0);
                preview.GetComponent<RawImage>().raycastTarget = false;
            }

            RectTransform nameLabel = MakeText(hero, "CarName", "", 44, TextLight, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            if (IsNew(nameLabel)) Place(nameLabel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), 0f, 0f, 760f, 60f);

            RectTransform prev = MakeButton(hero, "Previous", null, CardColor, TextLight, 44, "<");
            if (IsNew(prev)) Place(prev, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), 8f, 35f, 80f, 120f);
            RectTransform nxt = MakeButton(hero, "Next", null, CardColor, TextLight, 44, ">");
            if (IsNew(nxt)) Place(nxt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), -8f, 35f, 80f, 120f);

            view = preview.GetComponent<RawImage>();
            carName = nameLabel.GetComponent<Text>();
            previous = prev.GetComponent<Button>();
            next = nxt.GetComponent<Button>();
        }

        // ---------------------------------------------------------------- skins strip

        private static ChoiceList BuildSkins(RectTransform safe)
        {
            RectTransform strip;
            if (Make(safe, "SkinsStrip", out strip, typeof(CanvasGroup)))
            {
                Place(strip, BL, BL, 640f, 40f, 760f, 240f);
                AddEntrance(strip.gameObject, 0.18f);
            }

            RectTransform label = MakeText(strip, "Header", "menu.garage", 28, TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (IsNew(label)) Place(label, TL, TL, 4f, 0f, 500f, 44f);

            return MakeList(strip, "Skins", 0f, -52f, 760f, 188f, true, true, TL, TL);
        }

        // ---------------------------------------------------------------- play

        private static void BuildPlay(RectTransform safe, out Button play, out Text playLabel, out Text summary)
        {
            RectTransform cluster;
            if (Make(safe, "PlayCluster", out cluster, typeof(CanvasGroup)))
            {
                Place(cluster, BR, BR, -40f, 40f, 460f, 250f);
                AddEntrance(cluster.gameObject, 0.24f);
            }

            // No key: the controller writes this recap, so no LocalizedText competes for the string.
            RectTransform recap = MakeText(cluster, "Summary", null, 26, TextMuted, TextAnchor.LowerCenter, FontStyle.Normal,
                string.Format(English("menu.summary"), "-", 3, 5));
            if (IsNew(recap)) Place(recap, TR, TR, 0f, 0f, 460f, 90f);

            RectTransform button = MakeButton(cluster, "PlayButton", "menu.play", AccentColor, AccentTextColor, 60);
            if (IsNew(button))
            {
                Place(button, BR, BR, 0f, 0f, 460f, 140f);
                SetJuice(button.gameObject, false, 0f, true, true);
            }

            summary = recap.GetComponent<Text>();
            play = button.GetComponent<Button>();
            playLabel = button.Find("Label").GetComponent<Text>();
        }

        // ---------------------------------------------------------------- settings modal

        private static void BuildSettings(RectTransform canvas, out ModalPanel modal, out Slider volume,
            out Button english, out Button spanish, out Button quit)
        {
            RectTransform host;
            if (Make(canvas, "SettingsModal", out host))
                Stretch(host, 0, 0, 0, 0);

            RectTransform content;
            if (Make(host, "Content", out content))
            {
                Stretch(content, 0, 0, 0, 0);
                content.gameObject.SetActive(false);
            }

            RectTransform backdrop;
            if (Make(content, "Backdrop", out backdrop, typeof(Image), typeof(Button)))
            {
                Stretch(backdrop, 0, 0, 0, 0);
                var image = backdrop.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.62f);
                var button = backdrop.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
            }

            // The window is a sibling of the backdrop, not a child: a click on the window must not
            // bubble up to the backdrop's button and close the dialog.
            RectTransform window;
            if (Make(content, "Window", out window, typeof(Image), typeof(CanvasGroup)))
            {
                Place(window, Mid, Mid, 0f, 0f, 760f, 600f);
                StylePanel(window.GetComponent<Image>(), PanelColor);
                SetJuice(window.gameObject, true, 0f, false, false);
            }

            RectTransform title = MakeText(window, "Title", "menu.settings", 40, TextLight, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            if (IsNew(title)) Place(title, TL, TL, 0f, -24f, 760f, 64f);

            RectTransform volumeLabel = MakeText(window, "VolumeLabel", "menu.settings.volume", 28, TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (IsNew(volumeLabel)) Place(volumeLabel, TL, TL, 48f, -120f, 660f, 40f);
            volume = MakeSlider(window, "VolumeSlider", 48f, -170f, 664f, 40f);

            RectTransform languageLabel = MakeText(window, "LanguageLabel", "menu.settings.language", 28, TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
            if (IsNew(languageLabel)) Place(languageLabel, TL, TL, 48f, -250f, 660f, 40f);
            RectTransform en = MakeButton(window, "English", "lang.en", CardColor, TextLight, 28);
            if (IsNew(en)) Place(en, TL, TL, 48f, -300f, 316f, 70f);
            RectTransform es = MakeButton(window, "Spanish", "lang.es", CardColor, TextLight, 28);
            if (IsNew(es)) Place(es, TL, TL, 396f, -300f, 316f, 70f);

            RectTransform quitButton = MakeButton(window, "Quit", "menu.quit", CardColor, TextLight, 28);
            if (IsNew(quitButton)) Place(quitButton, BL, BL, 48f, 40f, 316f, 76f);
            RectTransform close = MakeButton(window, "Close", "menu.close", AccentColor, AccentTextColor, 32);
            if (IsNew(close)) Place(close, BR, BR, -48f, 40f, 316f, 76f);

            modal = Ensure<ModalPanel>(host.gameObject);
            SetIfNull(modal, "_content", content.gameObject);
            SetArrayIfEmpty(modal, "_closeButtons", new Object[] { close.GetComponent<Button>(), backdrop.GetComponent<Button>() });

            english = en.GetComponent<Button>();
            spanish = es.GetComponent<Button>();
            quit = quitButton.GetComponent<Button>();
        }

        // ---------------------------------------------------------------- rig, camera, events

        private static CarPreview BuildPreviewRig(Scene scene, RawImage heroView)
        {
            Transform rig = EnsureRoot(scene, "MenuPreviewRig");
            bool fresh = rig.childCount == 0;
            if (fresh)
                rig.position = new Vector3(1000f, 0f, 0f);

            bool created;
            Transform cameraNode = FindOrCreateChild(rig, "Camera", out created);
            if (created)
            {
                var camera = cameraNode.gameObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.fieldOfView = 30f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30f;
                // CarPreview turns it on while the menu is showing.
                camera.enabled = false;
                cameraNode.localPosition = new Vector3(0f, 1.4f, -7.5f);
                cameraNode.localRotation = Quaternion.Euler(9f, 0f, 0f);
            }

            Transform anchor = FindOrCreateChild(rig, "Turntable", out created);

            Transform lightNode = FindOrCreateChild(rig, "Light", out created);
            if (created)
            {
                var light = lightNode.gameObject.AddComponent<UnityEngine.Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                lightNode.localRotation = Quaternion.Euler(40f, -35f, 0f);
            }

            var preview = Ensure<CarPreview>(rig.gameObject);
            SetIfNull(preview, "_camera", cameraNode.GetComponent<Camera>());
            SetIfNull(preview, "_anchor", anchor);
            SetIfNull(preview, "_target", heroView);
            return preview;
        }

        private static void BuildCameraAndEvents(Scene scene)
        {
            Transform cam = EnsureRoot(scene, "MenuCamera");
            if (cam.GetComponent<Camera>() == null)
            {
                var camera = cam.gameObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = BackgroundColor;
                // It draws nothing (the Canvas is an overlay). It exists so the screen is cleared and the
                // "no camera rendering" warning stays away.
                camera.cullingMask = 0;
                camera.depth = -10f;
                cam.gameObject.AddComponent<AudioListener>();
            }

            Transform events = EnsureRoot(scene, "EventSystem");
            if (events.GetComponent<EventSystem>() == null)
            {
                events.gameObject.AddComponent<EventSystem>();
                events.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        // ============================================================================ widgets

        private static ChoiceList MakeList(RectTransform parent, string name, float x, float y, float w, float h,
            bool horizontal, bool scroll, Vector2 anchor, Vector2 pivot)
        {
            RectTransform root;
            if (Make(parent, name, out root))
                Place(root, anchor, pivot, x, y, w, h);

            RectTransform cardsParent = root;
            if (scroll)
            {
                RectTransform viewport;
                if (Make(root, "Viewport", out viewport, typeof(RectMask2D)))
                    Stretch(viewport, 0, 0, 0, 0);

                RectTransform content;
                if (Make(viewport, "Content", out content, horizontal ? typeof(HorizontalLayoutGroup) : typeof(VerticalLayoutGroup),
                    typeof(ContentSizeFitter)))
                {
                    if (horizontal)
                    {
                        content.anchorMin = new Vector2(0f, 0f);
                        content.anchorMax = new Vector2(0f, 1f);
                        content.pivot = new Vector2(0f, 0.5f);
                    }
                    else
                    {
                        content.anchorMin = new Vector2(0f, 1f);
                        content.anchorMax = new Vector2(1f, 1f);
                        content.pivot = TopMid;
                    }

                    content.offsetMin = content.offsetMax = Vector2.zero;
                    ConfigureLayout(content, horizontal);
                    var fitter = content.GetComponent<ContentSizeFitter>();
                    if (horizontal) fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    else fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }

                if (root.GetComponent<ScrollRect>() == null)
                {
                    var scrollRect = root.gameObject.AddComponent<ScrollRect>();
                    scrollRect.viewport = viewport;
                    scrollRect.content = content;
                    scrollRect.horizontal = horizontal;
                    scrollRect.vertical = !horizontal;
                    scrollRect.movementType = ScrollRect.MovementType.Clamped;
                }

                cardsParent = content;
            }
            else if (IsNew(root))
            {
                root.gameObject.AddComponent(horizontal ? typeof(HorizontalLayoutGroup) : typeof(VerticalLayoutGroup));
                ConfigureLayout(root, horizontal);
            }

            var list = Ensure<ChoiceList>(root.gameObject);
            CardParents[list] = cardsParent;
            return list;
        }

        private static void ConfigureLayout(RectTransform rt, bool horizontal)
        {
            var group = rt.GetComponent<HorizontalOrVerticalLayoutGroup>();
            group.spacing = horizontal ? 12f : 10f;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = !horizontal;
            group.childForceExpandHeight = horizontal;
            group.padding = new RectOffset(2, 2, 2, 2);
        }

        private static void FillOptions(ChoiceList list, List<MenuOption> options)
        {
            var so = new SerializedObject(list);
            SerializedProperty array = so.FindProperty("_options");
            // An option list you already have is yours; the builder only seeds an empty one.
            if (array.arraySize > 0 || options.Count == 0)
                return;

            array.arraySize = options.Count;
            for (int i = 0; i < options.Count; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Id").stringValue = options[i].Id;
                element.FindPropertyRelative("TitleKey").stringValue = options[i].TitleKey;
                element.FindPropertyRelative("SubtitleKey").stringValue = options[i].SubtitleKey ?? "";
                element.FindPropertyRelative("Icon").objectReferenceValue = options[i].Icon;
                element.FindPropertyRelative("Model").objectReferenceValue = options[i].Model;
                element.FindPropertyRelative("Locked").boolValue = options[i].Locked;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FillCards(ChoiceList list, float height, int titleSize, bool compact)
        {
            var so = new SerializedObject(list);
            SerializedProperty cards = so.FindProperty("_cards");
            SerializedProperty options = so.FindProperty("_options");
            if (cards.arraySize > 0 || options.arraySize == 0)
                return;

            RectTransform parent = CardParents[list];
            int count = options.arraySize;
            cards.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                SelectableCard card = MakeCard(parent, "Card_" + i, height, titleSize, compact);
                cards.GetArrayElementAtIndex(i).objectReferenceValue = card;

                // The Editor view shows each option's real text; at runtime ChoiceList rebinds it anyway.
                SerializedProperty option = options.GetArrayElementAtIndex(i);
                PreviewCard(card, option.FindPropertyRelative("TitleKey").stringValue,
                    option.FindPropertyRelative("SubtitleKey").stringValue,
                    option.FindPropertyRelative("Locked").boolValue);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SelectableCard MakeCard(RectTransform parent, string name, float height, int titleSize, bool compact)
        {
            RectTransform rt;
            Make(parent, name, out rt, typeof(Image), typeof(Button), typeof(LayoutElement));

            var image = rt.GetComponent<Image>();
            StylePanel(image, CardColor);
            var button = rt.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;

            var element = rt.GetComponent<LayoutElement>();
            element.preferredHeight = height;
            element.preferredWidth = compact ? 170f : -1f;
            SetJuice(rt.gameObject, false, 0f, false, true);

            RectTransform title = MakeText(rt, "Title", "", titleSize, TextLight,
                compact ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, FontStyle.Bold);
            if (compact)
                Stretch(title, 10, 10, 10, 10);
            else
                Stretch(title, 18, 28, 18, 8);

            // Anchored to the bottom edge: the second line of a card with a description.
            RectTransform subtitle = MakeText(rt, "Subtitle", "", 22, TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal);
            subtitle.anchorMin = Vector2.zero;
            subtitle.anchorMax = new Vector2(1f, 0f);
            subtitle.pivot = new Vector2(0.5f, 0f);
            subtitle.offsetMin = new Vector2(18f, 8f);
            subtitle.offsetMax = new Vector2(-18f, 34f);

            RectTransform badge = MakeText(rt, "LockBadge", "menu.locked", 20, new Color(1f, 0.82f, 0.3f, 1f),
                TextAnchor.UpperRight, FontStyle.Bold);
            Place(badge, TR, TR, -12f, -8f, 150f, 30f);
            badge.gameObject.SetActive(false);

            var card = Ensure<SelectableCard>(rt.gameObject);
            SetIfNull(card, "_background", image);
            SetIfNull(card, "_title", title.GetComponent<LocalizedText>());
            SetIfNull(card, "_subtitle", subtitle.GetComponent<LocalizedText>());
            SetIfNull(card, "_lockBadge", badge.gameObject);
            return card;
        }

        private static void PreviewCard(SelectableCard card, string titleKey, string subtitleKey, bool locked)
        {
            Transform title = card.transform.Find("Title");
            title.GetComponent<Text>().text = English(titleKey);
            SetString(title.GetComponent<LocalizedText>(), "_key", titleKey);

            Transform subtitle = card.transform.Find("Subtitle");
            subtitle.GetComponent<Text>().text = English(subtitleKey);
            SetString(subtitle.GetComponent<LocalizedText>(), "_key", subtitleKey);
            subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitleKey));

            card.transform.Find("LockBadge").gameObject.SetActive(locked);
        }

        private static Stepper MakeStepper(RectTransform parent, string name, float x, float y, string sample)
        {
            RectTransform root;
            if (Make(parent, name, out root))
                Place(root, Mid, Mid, x, y, 400f, 90f);

            RectTransform minus = MakeButton(root, "Minus", null, CardColor, TextLight, 48, "-");
            if (IsNew(minus)) Place(minus, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), 0f, 0f, 96f, 90f);

            // No key: the stepper writes the number, so no LocalizedText competes for the string.
            RectTransform value = MakeText(root, "Value", null, 56, TextLight, TextAnchor.MiddleCenter, FontStyle.Bold, sample);
            if (IsNew(value)) Place(value, Mid, Mid, 0f, 0f, 180f, 90f);

            RectTransform plus = MakeButton(root, "Plus", null, CardColor, TextLight, 48, "+");
            if (IsNew(plus)) Place(plus, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), 0f, 0f, 96f, 90f);

            var stepper = Ensure<Stepper>(root.gameObject);
            SetIfNull(stepper, "_minus", minus.GetComponent<Button>());
            SetIfNull(stepper, "_plus", plus.GetComponent<Button>());
            SetIfNull(stepper, "_valueLabel", value.GetComponent<Text>());
            return stepper;
        }

        private static Slider MakeSlider(RectTransform parent, string name, float x, float y, float w, float h)
        {
            RectTransform root;
            bool isNew = Make(parent, name, out root, typeof(Slider));
            if (isNew)
                Place(root, TL, TL, x, y, w, h);

            RectTransform track;
            if (Make(root, "Background", out track, typeof(Image)))
            {
                track.anchorMin = new Vector2(0f, 0.5f);
                track.anchorMax = new Vector2(1f, 0.5f);
                track.sizeDelta = new Vector2(0f, 14f);
                StylePanel(track.GetComponent<Image>(), CardColor);
            }

            RectTransform fillArea;
            if (Make(root, "Fill Area", out fillArea))
            {
                fillArea.anchorMin = new Vector2(0f, 0.5f);
                fillArea.anchorMax = new Vector2(1f, 0.5f);
                fillArea.offsetMin = new Vector2(8f, -7f);
                fillArea.offsetMax = new Vector2(-8f, 7f);
            }

            RectTransform fill;
            if (Make(fillArea, "Fill", out fill, typeof(Image)))
            {
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = new Vector2(0f, 1f);
                fill.offsetMin = fill.offsetMax = Vector2.zero;
                fill.sizeDelta = new Vector2(10f, 0f);
                StylePanel(fill.GetComponent<Image>(), AccentColor);
            }

            RectTransform handleArea;
            if (Make(root, "Handle Slide Area", out handleArea))
                Stretch(handleArea, 16, 0, 16, 0);

            RectTransform handle;
            if (Make(handleArea, "Handle", out handle, typeof(Image)))
            {
                handle.sizeDelta = new Vector2(34f, 34f);
                StylePanel(handle.GetComponent<Image>(), TextLight);
            }

            var slider = root.GetComponent<Slider>();
            if (isNew)
            {
                slider.fillRect = fill;
                slider.handleRect = handle;
                slider.targetGraphic = handle.GetComponent<Image>();
                slider.direction = Slider.Direction.LeftToRight;
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.value = 1f;
            }

            return slider;
        }

        // ============================================================================ low-level helpers

        private static bool IsNew(RectTransform rt)
        {
            return rt != null && MadeNow.Contains(rt.gameObject.GetInstanceID());
        }

        /// <summary>
        /// Finds the child by name in its DIRECT parent. Returns true only when it had to create it, so
        /// callers style a node exactly once and never touch one that was already there.
        /// </summary>
        private static bool Make(Transform parent, string name, out RectTransform rt, params Type[] components)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                rt = (RectTransform)existing;
                return false;
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (Type type in components)
                if (go.GetComponent(type) == null)
                    go.AddComponent(type);

            rt = (RectTransform)go.transform;
            MadeNow.Add(go.GetInstanceID());
            return true;
        }

        private static Transform FindOrCreateChild(Transform parent, string name, out bool created)
        {
            Transform child = parent.Find(name);
            created = child == null;
            if (created)
            {
                child = new GameObject(name).transform;
                child.SetParent(parent, false);
            }

            return child;
        }

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void StylePanel(Image image, Color color)
        {
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = color;
        }

        /// <summary>
        /// A label. A non-null key adds a LocalizedText (an empty key leaves it for code to set, as card
        /// titles are). A null key means a controller or a fixed glyph owns the string: no LocalizedText.
        /// </summary>
        private static RectTransform MakeText(Transform parent, string name, string key, int size, Color color,
            TextAnchor anchor, FontStyle style, string literal = null)
        {
            RectTransform rt;
            if (Make(parent, name, out rt, typeof(Text)))
            {
                var text = rt.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = size;
                text.color = color;
                text.alignment = anchor;
                text.fontStyle = style;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.raycastTarget = false;
                text.text = literal ?? English(key);

                if (key != null)
                {
                    var localized = rt.gameObject.AddComponent<LocalizedText>();
                    SetString(localized, "_key", key);
                }
            }

            return rt;
        }

        private static RectTransform MakeButton(Transform parent, string name, string key, Color fill, Color textColor,
            int size, string glyph = null)
        {
            RectTransform rt;
            if (Make(parent, name, out rt, typeof(Image), typeof(Button)))
            {
                var image = rt.GetComponent<Image>();
                StylePanel(image, fill);
                var button = rt.GetComponent<Button>();
                button.targetGraphic = image;
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
                colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                button.colors = colors;

                RectTransform label = MakeText(rt, "Label", key, size, textColor, TextAnchor.MiddleCenter,
                    FontStyle.Bold, glyph);
                Stretch(label, 8, 4, 8, 4);
                SetJuice(rt.gameObject, false, 0f, false, true);
            }

            return rt;
        }

        // ---------------------------------------------------------------- serialized helpers

        private static void AddEntrance(GameObject go, float delay)
        {
            SetJuice(go, true, delay, false, false);
        }

        private static void SetJuice(GameObject go, bool entrance, float delay, bool pulse, bool press)
        {
            var juice = Ensure<MenuJuice>(go);
            var so = new SerializedObject(juice);
            so.FindProperty("_entrance").boolValue = entrance;
            so.FindProperty("_entranceDelay").floatValue = delay;
            so.FindProperty("_pulse").boolValue = pulse;
            so.FindProperty("_press").boolValue = press;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Assigns the reference only while it is still empty, so a hand-wired one survives.</summary>
        private static void SetIfNull(Object target, string property, Object value)
        {
            if (target == null || value == null)
                return;

            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogError("[menu] missing field " + property + " on " + target.GetType().Name);
                return;
            }

            if (p.objectReferenceValue != null)
                return;

            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArrayIfEmpty(Object target, string property, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogError("[menu] missing field " + property + " on " + target.GetType().Name);
                return;
            }

            if (p.arraySize > 0)
                return;

            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string property, string value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
                return;

            p.stringValue = value ?? "";
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
