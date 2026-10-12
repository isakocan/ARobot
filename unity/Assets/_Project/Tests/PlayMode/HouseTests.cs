using System.Collections;
using System.Collections.Generic;
using ARaki.Game;
using ARaki.Robot;
using ARaki.Theming;
using ARaki.Video;
using ARaki.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ARaki.Tests
{
    /// <summary>Ev planı testleri: odalar birbirine bağlı mı, kapılar açık mı, üs boş mu, tema yüzeyleri uygulanıyor mu.</summary>
    public class HouseTests
    {
        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            Time.timeScale = 1f;
            ThemeCatalog.Set(ThemeId.Home, false);
            yield return SceneManager.LoadSceneAsync("DriveSim");
            yield return null;
            Physics.SyncTransforms();
        }

        const float Cell = 0.1f;
        const float RobotClearance = 0.2f;

        bool Free(Vector3 p) => !Physics.CheckSphere(new Vector3(p.x, 0.12f, p.z), RobotClearance, ~0, QueryTriggerInteraction.Ignore);

        /// <summary>Üsten başlayarak robotun gidebileceği tüm hücreler.</summary>
        HashSet<Vector2Int> Reachable()
        {
            var b = HouseLayout.Bounds;
            var seen = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            Vector2Int ToCell(Vector3 p) => new Vector2Int(Mathf.RoundToInt((p.x - b.xMin) / Cell), Mathf.RoundToInt((p.z - b.yMin) / Cell));
            Vector3 ToWorld(Vector2Int c) => new Vector3(b.xMin + c.x * Cell, 0f, b.yMin + c.y * Cell);

            var start = ToCell(HouseLayout.BasePosition);
            queue.Enqueue(start);
            seen.Add(start);
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (seen.Contains(n)) continue;
                    var w = ToWorld(n);
                    if (w.x < b.xMin || w.x > b.xMax || w.z < b.yMin || w.z > b.yMax) continue;
                    if (!Free(w)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        [UnityTest]
        public IEnumerator EveryRoomIsReachableFromBase()
        {
            var cells = Reachable();
            var b = HouseLayout.Bounds;
            foreach (var room in HouseLayout.Rooms)
            {
                int count = 0;
                foreach (var c in cells)
                {
                    var p = new Vector3(b.xMin + c.x * Cell, 0f, b.yMin + c.y * Cell);
                    if (room.Contains(p)) count++;
                }
                float area = count * Cell * Cell;
                Debug.Log($"[House] {room.Name}: ulaşılabilir {area:0.0} m² / {room.Area:0.0} m²");
                Assert.Greater(area, 2.0f, room.Name + " odasına üsten gidilemiyor ya da neredeyse tamamen dolu");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DoorsAreWideEnoughForTheRobot()
        {
            foreach (var d in HouseLayout.Doors)
            {
                Assert.IsTrue(Free(d.Center), "kapı açıklığı boş olmalı: " + d.Center);
                Vector3 along = d.AlongX ? Vector3.right : Vector3.forward;
                Assert.IsTrue(Free(d.Center + along * (d.Width * 0.5f - 0.3f)), "kapının kenarı robota yetmiyor: " + d.Center);
                Assert.IsTrue(Free(d.Center - along * (d.Width * 0.5f - 0.3f)), "kapının kenarı robota yetmiyor: " + d.Center);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BaseAreaIsClear()
        {
            for (float a = 0f; a < 360f; a += 45f)
            {
                var p = HouseLayout.BasePosition + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.7f;
                Assert.IsTrue(Free(p), "üssün çevresi boş olmalı, açı " + a);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryRoomHasEnoughReachableFloorForFinds()
        {
            var cells = Reachable();
            var b = HouseLayout.Bounds;
            foreach (var room in HouseLayout.Rooms)
            {
                int count = 0;
                foreach (var c in cells)
                {
                    var p = new Vector3(b.xMin + c.x * Cell, 0f, b.yMin + c.y * Cell);
                    if (room.Contains(p, 0.4f) && !HouseLayout.NearDoor(p, 0.9f)) count++;
                }
                Assert.Greater(count * Cell * Cell, 1.2f, room.Name + ": bulgu çıkacak boş zemin az");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FurnitureStaysInsideItsRoom()
        {
            var bad = new List<string>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c.name == "Wall" || c.GetComponentInParent<MockRobotLink>() != null) continue;
                var b = c.bounds;
                var room = HouseLayout.RoomAt(b.center);
                // eşyanın tamamı, merkezinin bulunduğu odanın içinde (duvar kalınlığı payıyla) olmalı
                const float slack = 0.08f;
                if (b.min.x < room.Rect.xMin - slack || b.max.x > room.Rect.xMax + slack || b.min.z < room.Rect.yMin - slack || b.max.z > room.Rect.yMax + slack)
                    bad.Add($"{c.name} ({room.Name}) x[{b.min.x:0.00},{b.max.x:0.00}] z[{b.min.z:0.00},{b.max.z:0.00}]");
            }
            if (bad.Count > 0) Debug.Log("[House] odadan taşan eşyalar:\n" + string.Join("\n", bad));
            Assert.AreEqual(0, bad.Count, "odadan taşan eşya var: " + string.Join("; ", bad));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RobotStartsInsideLivingRoom()
        {
            yield return null;
            Assert.AreEqual(RoomKind.Living, HouseLayout.RoomAt(HouseLayout.BasePosition).Kind);
        }

        [UnityTest]
        public IEnumerator ThemeSurfacesFollowTheSelectedTheme()
        {
            var skin = Object.FindFirstObjectByType<ThemeSkin>();
            foreach (ThemeId id in new[] { ThemeId.Space, ThemeId.Mystery, ThemeId.Safari, ThemeId.Home })
            {
                skin.Apply(id);
                int walls = 0;
                foreach (var s in Object.FindObjectsByType<ThemeSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (s.Role != SurfaceRole.Wall) continue;
                    walls++;
                    Assert.AreSame(s.ThemeMaterials[(int)id], s.GetComponent<Renderer>().sharedMaterial);
                }
                Assert.Greater(walls, 20, "duvar yüzeyleri tema sistemine bağlı olmalı");
            }
            yield return null;
        }

        [Test]
        public void EachThemeHasSixChaptersAndFiveFindNames()
        {
            foreach (var def in ThemeCatalog.All)
            {
                Assert.AreEqual(6, def.Chapters.Length, def.Name);
                Assert.AreEqual(5, def.Finds.Length, def.Name);
                foreach (var c in def.Chapters) Assert.IsNotEmpty(c.Intro, def.Name + " " + c.Title);
            }
            var names = new HashSet<string>();
            foreach (var def in ThemeCatalog.All)
                foreach (var f in def.Finds) Assert.IsTrue(names.Add(f.Name), "ad tekrarı: " + f.Name);
        }

        [UnityTest]
        public IEnumerator FrameTimeStaysSmallInTheBigHouse()
        {
            var session = Object.FindFirstObjectByType<GameSession>();
            session.ControlDriveInput = false;
            session.StartGame(true);
            for (int i = 0; i < 40; i++) yield return null;
            float start = Time.realtimeSinceStartup;
            const int frames = 120;
            for (int i = 0; i < frames; i++) yield return null;
            float ms = (Time.realtimeSinceStartup - start) / frames * 1000f;
            Debug.Log($"[Perf] büyük ev: ortalama kare süresi {ms:0.0} ms ({1000f / ms:0} fps)");
            Assert.Less(ms, 60f);
        }

        [UnityTest]
        public IEnumerator VideoCaptureIsRateLimited()
        {
            var video = Object.FindFirstObjectByType<Sim360Camera>();
            for (int i = 0; i < 20; i++) yield return null;
            int before = video.CaptureCount;
            float start = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - start < 1f) { frames++; yield return null; }
            int captures = video.CaptureCount - before;
            Debug.Log($"[Perf] 1 sn: {frames} kare, {captures} 360° yakalama");
            Assert.LessOrEqual(captures, 36);
            Assert.GreaterOrEqual(captures, 12);
        }
    }
}
