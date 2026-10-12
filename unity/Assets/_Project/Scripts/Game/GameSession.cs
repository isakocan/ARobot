using System;
using ARaki.Dev;
using ARaki.Robot;
using ARaki.Sensing;
using ARaki.Theming;
using ARaki.Video;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ARaki.Game
{
    public enum GameState { Menu, Playing, Paused, Lab, Ending }

    /// <summary>
    /// Oyun akışı: menü → serbest gezi ⇄ duraklat / laboratuvar → (bölümler bitince) final ekranı → gezmeye devam.
    /// Tema değişince sahne yeniden yüklenir (arayüz ve duvarlar seçili temayla kurulur).
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        [SerializeField] MockRobotLink robot;
        [SerializeField] KeyboardDriveInput driveInput;
        [SerializeField] Expedition expedition;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] ExplorationMap map;
        [SerializeField] bool startInMenu = true;

        public event Action<GameState> StateChanged;

        public GameState State { get; private set; } = GameState.Menu;
        public Expedition Expedition => expedition;

        /// <summary>Kapalıysa oyun sürüş girişini açıp kapatmaz (testler robotu kendileri sürer).</summary>
        public bool ControlDriveInput { get; set; } = true;

        void Start()
        {
            expedition.Finished += OnFinished;
            if (startInMenu) ShowMenu();
        }

        void OnDestroy()
        {
            if (expedition != null) expedition.Finished -= OnFinished;
        }

        // ------------------------------------------------------------ akış

        public void ShowMenu()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            expedition.Running = false;
            map.Running = false;
            if (ControlDriveInput) driveInput.enabled = false;
            robot.SendDrive(DriveCommand.Stop);
            robot.SetLed(false);
            viewer.AutoRotate = true;
            SetState(GameState.Menu);
        }

        /// <summary>Kayıtlı ilerlemeyle devam eder; kayıt yoksa yeni oyun.</summary>
        public void Continue() => StartGame(!expedition.HasSave);

        public void NewGame() => StartGame(true);

        public void StartGame(bool fresh)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            viewer.AutoRotate = false;
            expedition.Begin(fresh);
            map.Running = true;
            if (ControlDriveInput) driveInput.enabled = true;
            SetState(GameState.Playing);
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            Freeze(true);
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State != GameState.Paused && State != GameState.Lab && State != GameState.Ending) return;
            Freeze(false);
            SetState(GameState.Playing);
        }

        public void ToMenu()
        {
            expedition.Save();
            ShowMenu();
        }

        public bool OpenLab()
        {
            if (State != GameState.Playing || !expedition.InBase) return false;
            Freeze(true);
            SetState(GameState.Lab);
            return true;
        }

        public void CloseLab() => Resume();

        public bool Buy(UpgradeId id) => State == GameState.Lab && expedition.Buy(id);

        /// <summary>Tema seç: kaydedilir ve sahne yeniden yüklenir.</summary>
        public void SetTheme(ThemeId id)
        {
            if (ThemeCatalog.Current == id) return;
            expedition.Save();
            ThemeCatalog.Set(id);
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Freeze(bool frozen)
        {
            Time.timeScale = frozen ? 0f : 1f;
            AudioListener.pause = frozen;
            if (frozen) robot.SendDrive(DriveCommand.Stop);
        }

        void OnFinished()
        {
            Freeze(true);
            SetState(GameState.Ending);
        }

        // ------------------------------------------------------------ her kare

        void Update()
        {
            var kb = Keyboard.current;
            bool Pressed(Func<Keyboard, UnityEngine.InputSystem.Controls.KeyControl> key) => kb != null && key(kb).wasPressedThisFrame;

            switch (State)
            {
                case GameState.Menu:
                    if (Pressed(k => k.enterKey) || Pressed(k => k.spaceKey)) Continue();
                    else if (Pressed(k => k.nKey)) NewGame();
                    else if (Pressed(k => k.digit1Key)) SetTheme(ThemeId.Home);
                    else if (Pressed(k => k.digit2Key)) SetTheme(ThemeId.Safari);
                    else if (Pressed(k => k.digit3Key)) SetTheme(ThemeId.Space);
                    else if (Pressed(k => k.digit4Key)) SetTheme(ThemeId.Mystery);
                    else if (Pressed(k => k.escapeKey)) Quit();
                    break;

                case GameState.Playing:
                    if (Pressed(k => k.escapeKey)) Pause();
                    else if (Pressed(k => k.uKey)) OpenLab();
                    break;

                case GameState.Paused:
                    if (Pressed(k => k.escapeKey)) Resume();
                    else if (Pressed(k => k.mKey)) ToMenu();
                    break;

                case GameState.Lab:
                    if (Pressed(k => k.escapeKey) || Pressed(k => k.uKey)) CloseLab();
                    else if (kb != null)
                    {
                        var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key };
                        for (int i = 0; i < digits.Length && i < UpgradeCatalog.All.Length; i++)
                            if (digits[i].wasPressedThisFrame) Buy((UpgradeId)i);
                    }
                    break;

                case GameState.Ending:
                    if (Pressed(k => k.enterKey) || Pressed(k => k.spaceKey) || Pressed(k => k.escapeKey)) Resume();
                    break;
            }
        }

        void SetState(GameState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void QuitGame() => Quit();

        /// <summary>Testler ve ekran görüntüleri için: durumu doğrudan ayarlar.</summary>
        public void ForceState(GameState state) => SetState(state);
    }
}
