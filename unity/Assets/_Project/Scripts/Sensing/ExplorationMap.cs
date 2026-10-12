using System;
using UnityEngine;

namespace ARaki.Sensing
{
    public enum CellState : byte { Unknown, Free, Wall }

    /// <summary>
    /// Mesafe sensörü simülasyonu ve keşfedilen alanın haritası. Robotun çevresine 360° ışın atar (gerçekte: dönen bir
    /// lidar ya da servo üstünde ToF sensörü), ışın boyunca olan hücreleri "boş zemin", çarptığı hücreyi "engel" işaretler.
    /// Harita robotun gezdiği yerleri hatırlar; görev "şu kadar alan haritala" diyebilir, çıkan nesneler yalnızca
    /// robotun gerçekten görüp boş olduğunu bildiği yerlere konabilir.
    /// Gerçek robotta ışınlar yerine sensör ölçümleri gelir; harita ve geri kalan her şey aynı kalır.
    /// </summary>
    public class ExplorationMap : MonoBehaviour
    {
        [SerializeField] Transform robot;
        [SerializeField] Vector2 origin = new Vector2(-3.1f, -2.6f);   // haritanın sol-alt köşesi (x, z)
        [SerializeField] Vector2 size = new Vector2(6.2f, 5.2f);
        [SerializeField] float cellSize = 0.1f;
        [SerializeField] float range = 3f;           // sensör menzili, m
        [SerializeField] int rays = 96;
        [SerializeField] float scanInterval = 0.1f;
        [SerializeField] float rangeNoise = 0.015f;  // ölçüm gürültüsü, m
        [SerializeField] float sensorHeight = 0.15f;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public float Range { get => range; set => range = value; }
        public float CellSize => cellSize;
        public Vector2 Origin => origin;
        public Vector2 Size => size;
        public Texture2D Texture { get; private set; }
        public float ExploredArea => freeCount * cellSize * cellSize;
        public int ScanCount { get; private set; }

        /// <summary>Kapalıyken sensör çalışmaz (menü, duraklatma, yarış).</summary>
        public bool Running { get; set; }

        CellState[] cells;
        Color32[] pixels;
        int freeCount;
        bool dirty;
        float timer, textureTimer;

        static readonly Color32 Clear32 = new Color32(0, 0, 0, 0);
        static readonly Color32 FreeColor = new Color32(95, 227, 255, 62);
        static readonly Color32 WallColor = new Color32(190, 248, 255, 240);

        void Awake() => Init();

        public void Init()
        {
            if (cells != null) return;
            Width = Mathf.CeilToInt(size.x / cellSize);
            Height = Mathf.CeilToInt(size.y / cellSize);
            cells = new CellState[Width * Height];
            pixels = new Color32[Width * Height];
            Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "ExplorationMap",
            };
            Clear();
        }

        void OnDestroy()
        {
            if (Texture != null) { if (Application.isPlaying) Destroy(Texture); else DestroyImmediate(Texture); }
        }

        /// <summary>Haritayı sıfırlar (yeni görev).</summary>
        public void Clear()
        {
            Init();
            Array.Clear(cells, 0, cells.Length);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Clear32;
            freeCount = 0;
            ScanCount = 0;
            dirty = true;
            ApplyTexture();
        }

        void Update()
        {
            if (!Running || robot == null) return;
            timer += Time.deltaTime;
            if (timer >= scanInterval)
            {
                timer = 0f;
                Scan();
            }
            textureTimer += Time.deltaTime;
            if (dirty && textureTimer > 0.1f) ApplyTexture();
        }

        void ApplyTexture()
        {
            textureTimer = 0f;
            if (!dirty || Texture == null) return;
            Texture.SetPixels32(pixels);
            Texture.Apply(false);
            dirty = false;
        }

        // ------------------------------------------------------------------ sensör

        /// <summary>Tek bir 360° süpürme: ışınları atar, haritayı günceller.</summary>
        public void Scan()
        {
            Init();
            Vector3 center = robot.position;
            center.y = sensorHeight;
            SetCell(center, CellState.Free);       // robotun bulunduğu hücre de boş zemindir
            float offset = ScanCount * 0.71f;
            for (int i = 0; i < rays; i++)
            {
                float a = (offset + i * 360f / rays) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float hitDistance = range;
                bool hit = false;
                // robotun kendi gövdesini atlamak için ışını biraz dışarıdan başlat
                if (Physics.Raycast(center + dir * 0.2f, dir, out var info, range - 0.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    hitDistance = info.distance + 0.2f + (UnityEngine.Random.value - 0.5f) * 2f * rangeNoise;
                    hit = true;
                }
                MarkRay(center, dir, hitDistance, hit);
            }
            ScanCount++;
        }

        void MarkRay(Vector3 from, Vector3 dir, float distance, bool hit)
        {
            float freeUntil = hit ? distance - cellSize * 0.5f : distance;
            for (float d = 0.15f; d <= freeUntil; d += cellSize * 0.5f)
                SetCell(from + dir * d, CellState.Free);
            if (hit) SetCell(from + dir * distance, CellState.Wall);
        }

        void SetCell(Vector3 world, CellState state)
        {
            int x = Mathf.FloorToInt((world.x - origin.x) / cellSize);
            int z = Mathf.FloorToInt((world.z - origin.y) / cellSize);
            if (x < 0 || z < 0 || x >= Width || z >= Height) return;
            int i = z * Width + x;
            var old = cells[i];
            if (old == state || (state == CellState.Free && old == CellState.Wall)) return;   // engel boş zemine geri dönmez
            if (old == CellState.Free) freeCount--;
            if (state == CellState.Free) freeCount++;
            cells[i] = state;
            pixels[i] = state == CellState.Wall ? WallColor : state == CellState.Free ? FreeColor : Clear32;
            dirty = true;
        }

        // ------------------------------------------------------------------ sorgular

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Height;

        public CellState At(int x, int z) => InBounds(x, z) ? cells[z * Width + x] : CellState.Unknown;

        public CellState At(Vector3 world) =>
            At(Mathf.FloorToInt((world.x - origin.x) / cellSize), Mathf.FloorToInt((world.z - origin.y) / cellSize));

        public Vector3 CellCenter(int x, int z) => new Vector3(origin.x + (x + 0.5f) * cellSize, 0f, origin.y + (z + 0.5f) * cellSize);

        public Vector2Int ToCell(Vector3 world) =>
            new Vector2Int(Mathf.FloorToInt((world.x - origin.x) / cellSize), Mathf.FloorToInt((world.z - origin.y) / cellSize));

        /// <summary>İki nokta arasında görüş hattı var mı? Hedefin kendi gövdesi (ör. 3D baskı maketi) engel sayılmaz.</summary>
        public bool LineOfSight(Vector3 from, Vector3 to, float targetSlack = 0.3f)
        {
            from.y = to.y = sensorHeight;
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.05f) return true;
            return !Physics.Raycast(from, d / dist, dist - targetSlack, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Bu noktaya yakın bir yerde robotun gördüğü boş zemin var mı?</summary>
        /// <summary>Yakında (hücre sayısı içinde) bilinen bir engel var mı? Eşyanın kuytusunu bulmak için.</summary>
        public bool HasWallNear(Vector3 world, int radiusCells)
        {
            var c = ToCell(world);
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
                for (int dx = -radiusCells; dx <= radiusCells; dx++)
                    if (At(c.x + dx, c.y + dz) == CellState.Wall) return true;
            return false;
        }

        public bool IsKnownFree(Vector3 world, int radiusCells = 1)
        {
            var c = ToCell(world);
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
                for (int dx = -radiusCells; dx <= radiusCells; dx++)
                    if (At(c.x + dx, c.y + dz) != CellState.Free) return false;
            return true;
        }

        /// <summary>Testler ve ekran görüntüleri için: robotu verilen noktalara götürüp her birinde sensör çalıştırır.</summary>
        public void SurveyFrom(Vector3[] spots, int sweepsPerSpot = 3)
        {
            Init();
            var original = robot.position;
            foreach (var s in spots)
            {
                robot.position = new Vector3(s.x, original.y, s.z);
                Physics.SyncTransforms();
                for (int i = 0; i < sweepsPerSpot; i++) Scan();
            }
            robot.position = original;
            Physics.SyncTransforms();
            ApplyTexture();
        }

        public void FlushTexture() { dirty = true; ApplyTexture(); }
    }
}
