using System;
using System.Collections.Generic;
using ARaki.Dev;
using ARaki.Robot;
using ARaki.Video;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARaki.Game
{
    public enum GameState { Menu, Playing, Paused, Won, Lost, Upgrade }

    public struct MissionResult
    {
        public float Seconds;
        public float Fuel;
        public int ObjectivePoints, TimeBonus, FuelBonus, Total, Best;
        public int Stars;
        public bool NewBest;
    }

    /// <summary>
    /// Oyun akışı: menü → görev → kazan/kaybet → yükseltme → sıradaki görev. Görevleri üretir, robotu ve
    /// düşmanları hazırlar, puanı hesaplar. Ekranlar bu sınıfın durumunu dinleyip çizilir.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        [SerializeField] MockRobotLink robot;
        [SerializeField] KeyboardDriveInput driveInput;
        [SerializeField] MissionController mission;
        [SerializeField] MissionGenerator generator;
        [SerializeField] PanoramaViewer viewer;
        [SerializeField] bool startInMenu = true;

        public event Action<GameState> StateChanged;

        public GameState State { get; private set; } = GameState.Menu;
        public int Level { get; private set; } = 1;
        public MissionSpec Spec { get; private set; }
        public PlayerProfile Profile { get; } = new PlayerProfile();
        public float Elapsed { get; private set; }
        public MissionResult Result { get; private set; }
        public IReadOnlyList<UpgradeId> Offers => offers;
        public int BestScore => PlayerPrefs.GetInt("ARaki.BestScore", 0);
        public float ParSeconds => 40f * (Spec.TowerCount + 1) + 30f;
        public MissionController Mission => mission;

        readonly List<UpgradeId> offers = new List<UpgradeId>();
        int seedCounter;

        void Start()
        {
            seedCounter = Environment.TickCount;
            if (startInMenu) ShowMenu();
        }

        // ------------------------------------------------------------ akış

        public void ShowMenu()
        {
            Level = 1;
            Profile.Reset();
            PrepareMission(PickSeed());   // arka planda canlı bir sahne dursun
            mission.Running = false;
            SetPlayable(false, true);
            SetState(GameState.Menu);
        }

        public void StartGame()
        {
            Level = 1;
            Profile.Reset();
            StartMission();
        }

        public void StartMission(int seed = int.MinValue)
        {
            PrepareMission(seed == int.MinValue ? PickSeed() : seed);
            Elapsed = 0f;
            SetPlayable(true, false);
            mission.Begin(Spec.Title);
            SetState(GameState.Playing);
        }

        public void Retry() => StartMission();

        public void Pause()
        {
            if (State != GameState.Playing) return;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SetState(GameState.Playing);
        }

        public void ToMenu()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            ShowMenu();
        }

        public void ShowUpgrades()
        {
            if (State != GameState.Won) return;
            offers.Clear();
            var pool = new List<UpgradeId>();
            foreach (var def in UpgradeCatalog.All)
                if (Profile.CanUpgrade(def.Id)) pool.Add(def.Id);
            var rng = new System.Random(PickSeed());
            while (offers.Count < 3 && pool.Count > 0)
            {
                int i = rng.Next(pool.Count);
                offers.Add(pool[i]);
                pool.RemoveAt(i);
            }
            if (offers.Count == 0) { NextMission(); return; }
            SetState(GameState.Upgrade);
        }

        public void ChooseUpgrade(int index)
        {
            if (State != GameState.Upgrade || index < 0 || index >= offers.Count) return;
            Profile.Add(offers[index]);
            NextMission();
        }

        void NextMission()
        {
            Level++;
            StartMission();
        }

        // ------------------------------------------------------------ görev kurulumu

        void PrepareMission(int seed)
        {
            Spec = generator.Generate(Level, seed);

            MissionPoint baseP = null, fuelP = null;
            var towers = new List<MissionPoint>();
            foreach (var p in mission.Points)
            {
                if (p.Kind == PointKind.Base) baseP = p;
                else if (p.Kind == PointKind.Fuel) fuelP = p;
                else towers.Add(p);
            }

            baseP.SetInMission(true);
            baseP.MoveTo(Spec.BasePosition);
            for (int i = 0; i < towers.Count; i++)
            {
                bool use = i < Spec.TowerCount;
                towers[i].SetInMission(use);
                if (use) towers[i].MoveTo(Spec.TowerPositions[i]);
            }
            fuelP.SetInMission(Spec.HasFuel);
            if (Spec.HasFuel) fuelP.MoveTo(Spec.FuelPosition);

            var threats = mission.Threats;
            for (int i = 0; i < threats.Count; i++)
            {
                bool use = i < Spec.ThreatCount;
                threats[i].gameObject.SetActive(use);
                if (!use) continue;
                threats[i].SetDifficulty(Spec.ThreatSpeed);
                threats[i].Respawn(Spec.ThreatPositions[i]);
            }

            robot.MaxPwmLimit = Profile.MaxPwm;
            robot.PwmLimit = Mathf.Min(0.6f, Profile.MaxPwm);
            robot.LedDrainMultiplier = Profile.LedDrainMultiplier;
            robot.SetBattery(Profile.StartBattery);
            robot.SetLed(false);
            mission.ScanTimeMultiplier = Profile.ScanTimeMultiplier;

            Vector3 start = Spec.BasePosition + new Vector3(Mathf.Sin(Spec.BaseYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(Spec.BaseYaw * Mathf.Deg2Rad)) * 0.0f;
            robot.Teleport(start, Spec.BaseYaw);
            foreach (var p in mission.Points) if (p.gameObject.activeSelf) p.ResetState();
        }

        void SetPlayable(bool playing, bool attract)
        {
            driveInput.enabled = playing;
            mission.Running = playing;
            viewer.AutoRotate = attract;
            if (!playing)
            {
                robot.SendDrive(DriveCommand.Stop);
                robot.SetLed(false);
            }
            foreach (var t in mission.Threats) t.DrainEnabled = playing;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        // ------------------------------------------------------------ her kare

        void Update()
        {
            var kb = Keyboard.current;
            bool Pressed(System.Func<Keyboard, UnityEngine.InputSystem.Controls.KeyControl> key) => kb != null && key(kb).wasPressedThisFrame;

            switch (State)
            {
                case GameState.Menu:
                    if (Pressed(k => k.enterKey) || Pressed(k => k.spaceKey)) StartGame();
                    else if (Pressed(k => k.escapeKey)) Quit();
                    break;

                case GameState.Playing:
                    Elapsed += Time.deltaTime;
                    if (Pressed(k => k.escapeKey)) { Pause(); break; }
                    if (mission.Complete) Win();
                    else if (robot.Telemetry.Battery01 <= 0.0005f) Lose();
                    break;

                case GameState.Paused:
                    if (Pressed(k => k.escapeKey)) Resume();
                    else if (Pressed(k => k.mKey)) ToMenu();
                    break;

                case GameState.Won:
                    if (Pressed(k => k.enterKey) || Pressed(k => k.spaceKey)) ShowUpgrades();
                    else if (Pressed(k => k.mKey)) ToMenu();
                    break;

                case GameState.Lost:
                    if (Pressed(k => k.enterKey) || Pressed(k => k.spaceKey)) Retry();
                    else if (Pressed(k => k.mKey)) ToMenu();
                    break;

                case GameState.Upgrade:
                    if (Pressed(k => k.digit1Key)) ChooseUpgrade(0);
                    else if (Pressed(k => k.digit2Key)) ChooseUpgrade(1);
                    else if (Pressed(k => k.digit3Key)) ChooseUpgrade(2);
                    break;
            }
        }

        void Win()
        {
            SetPlayable(false, false);
            float fuel = robot.Telemetry.Battery01;

            int objective = 0;
            foreach (var p in mission.Points)
            {
                if (!p.gameObject.activeSelf || !p.Done) continue;
                objective += p.Kind == PointKind.Tower ? 100 : p.Kind == PointKind.Fuel ? 60 : 150;
            }
            int timeBonus = Mathf.Max(0, Mathf.RoundToInt((ParSeconds - Elapsed) * 2f));
            int fuelBonus = Mathf.RoundToInt(fuel * 300f);
            int total = objective + timeBonus + fuelBonus;
            int best = BestScore;
            bool newBest = total > best;
            if (newBest) { PlayerPrefs.SetInt("ARaki.BestScore", total); PlayerPrefs.Save(); best = total; }

            Result = new MissionResult
            {
                Seconds = Elapsed,
                Fuel = fuel,
                ObjectivePoints = objective,
                TimeBonus = timeBonus,
                FuelBonus = fuelBonus,
                Total = total,
                Best = best,
                NewBest = newBest,
                Stars = 1 + (fuel >= 0.3f ? 1 : 0) + (Elapsed <= ParSeconds ? 1 : 0),
            };
            SetState(GameState.Won);
        }

        void Lose()
        {
            SetPlayable(false, false);
            SetState(GameState.Lost);
        }

        void SetState(GameState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        int PickSeed() => unchecked(seedCounter++ * 7919 + 13);

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Testler ve ekran görüntüleri için: durumu doğrudan ayarlar.</summary>
        public void ForceState(GameState state) => SetState(state);

        public void QuitGame() => Quit();

        /// <summary>Testler ve ekran görüntüleri için: görevi bitmiş sayıp sonuç ekranına geçer.</summary>
        public void ForceWin()
        {
            foreach (var p in mission.Points) if (p.gameObject.activeSelf) p.Done = true;
            Win();
        }

        public void ForceLose() => Lose();

        public void SetLevel(int level) => Level = Mathf.Max(1, level);
    }
}
