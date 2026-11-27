// Наглядный предпросмотр и тестовый редактор проекта в Unity (нажать Play). Рисует комнату, потолок, стены, проёмы,
// предметы и светильники простыми цветными блоками и даёт меню для проверки команд, материалов, undo/redo и сохранения.
// Это инструмент проверки, не часть приложения: готовый интерфейс сделает роль C. Работает в отдельной сцене.
//
// Меню действует на ВЫБРАННЫЙ элемент (предмет, стена, пол или потолок), выбранное отмечено жёлтым маркером.
// Все действия выполняют EditorTools и команды (undo/redo); здесь правил размещения нет.
// Кнопки не меняют проект прямо внутри OnGUI: нажатие запоминается и выполняется в Update (так Unity не путает
// элементы меню между отрисовками).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace XrInterior.ProjectData.UnityIntegration
{
    public class ProjectPreview : MonoBehaviour
    {
        public enum PreviewSource { NewTemplateRoom, ControlProject }

        [Header("Что показать")]
        public PreviewSource source = PreviewSource.NewTemplateRoom;

        [Header("Помещение (для NewTemplateRoom), размеры в метрах. Меняются и в меню «Комната».")]
        public RoomTemplate template = RoomTemplate.Rectangular;
        public float rectWidth = 8f;
        public float rectDepth = 6f;
        public float lWidth = 10f;
        public float lDepth = 8f;
        public float lCutWidth = 4f;
        public float lCutDepth = 3f;
        public float corridorLength = 12f;
        public float corridorWidth = 2.5f;

        [Header("Потолок (его можно скрыть кнопкой в меню «Комната»)")]
        public bool showCeiling = true;

        // Условные цвета материалов: настоящие текстуры сделает C, здесь важно лишь видеть, что материал сменился.
        private static readonly Dictionary<string, Color> MaterialColors = new Dictionary<string, Color>
        {
            { "mat_wood_oak_01", new Color(0.78f, 0.58f, 0.32f) },
            { "mat_wood_walnut_01", new Color(0.36f, 0.22f, 0.12f) },
            { "mat_laminate_grey_01", new Color(0.62f, 0.62f, 0.60f) },
            { "mat_tile_white_01", new Color(0.93f, 0.95f, 0.96f) },
            { "mat_tile_dark_01", new Color(0.18f, 0.20f, 0.24f) },
            { "mat_carpet_grey_01", new Color(0.45f, 0.45f, 0.50f) },
            { "mat_concrete_01", new Color(0.55f, 0.56f, 0.57f) },
            { "mat_paint_white_01", new Color(0.97f, 0.97f, 0.95f) },
            { "mat_paint_beige_01", new Color(0.90f, 0.82f, 0.66f) },
            { "mat_paint_grey_01", new Color(0.66f, 0.68f, 0.70f) },
            { "mat_paint_blue_01", new Color(0.30f, 0.52f, 0.85f) },
            { "mat_brick_01", new Color(0.70f, 0.30f, 0.22f) },
            { "mat_plaster_01", new Color(0.88f, 0.86f, 0.82f) },
            { "mat_ceiling_white_01", new Color(1f, 1f, 1f) },
            { "mat_fabric_grey_01", new Color(0.55f, 0.58f, 0.62f) },
            { "mat_leather_black_01", new Color(0.10f, 0.09f, 0.09f) },
            { "mat_metal_01", new Color(0.75f, 0.77f, 0.80f) },
            { "mat_glass_01", new Color(0.65f, 0.85f, 0.95f) },
            { "mat_wood_door_01", new Color(0.50f, 0.34f, 0.18f) }
        };

        private static Color MaterialColor(string materialId)
        {
            Color c;
            if (materialId != null && MaterialColors.TryGetValue(materialId, out c)) return c;
            return Color.magenta;   // материал, которого нет в таблице цветов
        }
        // Имитация результата сканирования: один «реальный стол» посреди помещения. Настоящую проверку даст сервис A.
        private sealed class FakeReality : IRealityCollisionChecker
        {
            public Vector3 Center = new Vector3(2.5f, 0.4f, 1.5f);
            public Vector3 Size = new Vector3(1.2f, 0.8f, 0.7f);

            public bool IsBlocked(float centerX, float centerY, float centerZ,
                float sizeX, float sizeY, float sizeZ, float yawDegrees, out string reason)
            {
                float yaw = yawDegrees * Mathf.Deg2Rad;
                float hx = Mathf.Abs(Mathf.Cos(yaw)) * sizeX / 2f + Mathf.Abs(Mathf.Sin(yaw)) * sizeZ / 2f;
                float hz = Mathf.Abs(Mathf.Sin(yaw)) * sizeX / 2f + Mathf.Abs(Mathf.Cos(yaw)) * sizeZ / 2f;
                bool overlap = Mathf.Abs(centerX - Center.x) < hx + Size.x / 2f
                               && Mathf.Abs(centerZ - Center.z) < hz + Size.z / 2f
                               && Mathf.Abs(centerY - Center.y) < sizeY / 2f + Size.y / 2f;
                reason = overlap ? "Пересекается с реальным столом (имитация сканирования)." : null;
                return overlap;
            }
        }

        // ---- Состояние ----

        private string _storageDir;
        private ProjectStorage _storage;
        private ProjectService _projects;
        private CommandService _commands;
        private CatalogService _catalog;
        private EditorTools _tools;
        private readonly EditorSelection _selection = new EditorSelection();
        private bool _fakeReality;
        private readonly FakeReality _reality = new FakeReality();
        private GameObject _viewRoot;
        private bool _framed;
        private string _status = "";
        private bool _dirty;
        private readonly Dictionary<string, GameObject> _markers = new Dictionary<string, GameObject>();

        // ---- Доступ для надстроек (например, XR-слоя XrEditorBridge). Здесь нет зависимостей от XR-пакетов. ----

        /// <summary>false - не двигать камеру сцены (нужно, когда в сцене есть XR-риг).</summary>
        public bool frameCamera = true;

        /// <summary>Откуда брать место для новых предметов (например, камера игрока). null - центр помещения.</summary>
        public Transform spawnAnchor;

        /// <summary>Пока значение больше нуля, сцена не перестраивается (например, предмет в руке).</summary>
        public int RebuildHolds { get; set; }

        public ProjectService Projects { get { return _projects; } }
        public CommandService Commands { get { return _commands; } }
        public CatalogService Catalog { get { return _catalog; } }
        public EditorTools Tools { get { return _tools; } }
        public EditorSelection Selection { get { return _selection; } }
        public Transform ViewRoot { get { return _viewRoot != null ? _viewRoot.transform : null; } }
        public string Status { get { return _status; } set { _status = value; } }

        /// <summary>Вызывается для каждого нарисованного предмета: корень стоит в точке привязки, форма и коллайдер внутри.</summary>
        public event Action<GameObject, PlacedObject> ObjectViewBuilt;

        /// <summary>Вызывается для каждой нарисованной стены: корень в середине стены, форма и коллайдер внутри.</summary>
        public event Action<GameObject, Wall> WallViewBuilt;

        /// <summary>Вызывается после каждой перерисовки сцены.</summary>
        public event Action ViewRebuilt;

        /// <summary>Вызывается, когда проект создан или открыт заново (новая комната, повторное открытие).</summary>
        public event Action ProjectReopened;

        /// <summary>Просит перерисовать сцену при первой возможности (когда нет удержаний).</summary>
        public void RequestRebuild() { _dirty = true; }

        public float RoomElevation
        {
            get
            {
                Project p = _projects != null ? _projects.Current : null;
                return p != null && p.Rooms.Count > 0 ? p.Rooms[0].Elevation : 0f;
            }
        }

        public bool TryGetRoomBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = 0f; maxX = 4f; minZ = 0f; maxZ = 3f;
            Project p = _projects != null ? _projects.Current : null;
            if (p == null || p.Rooms.Count == 0) return false;
            PlanGeometry.Bounds(p.Rooms[0].Boundary, out minX, out maxX, out minZ, out maxZ);
            return true;
        }

        /// <summary>Точка плана, где стоит пользователь (чуть впереди него). false, если якорь не задан.</summary>
        public bool TryGetSpawnPoint(out PlanPoint point)
        {
            point = default(PlanPoint);
            if (spawnAnchor == null || _viewRoot == null) return false;
            Vector3 forward = spawnAnchor.forward;
            forward.y = 0f;
            Vector3 world = spawnAnchor.position;
            if (forward.sqrMagnitude > 0.01f) world += forward.normalized * 1.2f;
            Vector3 local = _viewRoot.transform.InverseTransformPoint(world);
            point = new PlanPoint(local.x, local.z);
            return true;
        }

        public void ApplyReality() { if (_tools != null) _tools.Reality = _fakeReality ? _reality : null; }

        // ---------- запуск ----------

        private void Awake()
        {
            _selection.SelectionChanged += OnSelectionChanged;
        }

        private void Start()
        {
            try { BuildProject(); }
            catch (Exception ex)
            {
                _status = "Ошибка: " + ex.Message;
                Debug.LogError(ex);
            }
        }

        private void Update()
        {
            if (_pending != null)
            {
                Action action = _pending;
                _pending = null;
                Run(action);
            }
            if (_dirty && RebuildHolds <= 0 && _projects != null && _projects.Current != null)
            {
                _dirty = false;
                Rebuild();
            }
            RefreshUi();
        }

        private void OnDestroy()
        {
            CleanStorage();
        }

        private void CleanStorage()
        {
            try { if (!string.IsNullOrEmpty(_storageDir) && Directory.Exists(_storageDir)) Directory.Delete(_storageDir, true); }
            catch (IOException) { }
        }

        private void OnSelectionChanged(string key)
        {
            foreach (KeyValuePair<string, GameObject> m in _markers)
                if (m.Value != null) m.Value.SetActive(m.Key == key);
        }

        // ---------- проект ----------

        private void BuildProject()
        {
            try
            {
                _catalog = CatalogService.FromFile(
                    Path.Combine(Application.dataPath, "_Project/ProjectData/Samples/catalog_sample.json"));
            }
            catch (Exception ex)
            {
                _catalog = null;
                Debug.LogWarning("Каталог не загружен: " + ex.Message);
            }
            BuildCategories();

            if (_projects != null) _projects.ProjectChanged -= Rebuild;
            CleanStorage();
            _storageDir = Path.Combine(Path.GetTempPath(), "xri_preview_" + Guid.NewGuid().ToString("N"));
            _storage = new ProjectStorage(_storageDir);
            _projects = new ProjectService(_storage);

            if (source == PreviewSource.ControlProject)
            {
                string path = Path.Combine(Application.dataPath, "_Project/ProjectData/Samples/control_project.json");
                if (!File.Exists(path)) { _status = "Не найден файл: " + path; return; }
                _projects.Import(path);
                _status = "Открыт контрольный проект из файла.";
            }
            else
            {
                var spec = new RoomSpec { Name = "Комната", Template = template, Params = ParamsFor(template) };
                try
                {
                    TemplateProjectBuilder.CreateFromTemplate(_projects, "Предпросмотр", spec);
                    _status = "Создана комната: " + TemplateName(template) + ".";
                }
                catch (ArgumentException ex)
                {
                    _status = ex.Message;
                    return;
                }
            }

            AttachCommands();
            _framed = false;
            PlaceFakeTable();
            Rebuild();
            if (ProjectReopened != null) ProjectReopened();
        }

        private void AttachCommands()
        {
            _commands = new CommandService(_projects);
            _selection.Clear();
            _tools = _catalog != null ? new EditorTools(_projects, _commands, _catalog) : null;
            ApplyReality();
            _projects.ProjectChanged += Rebuild;
        }

        // «Реальный стол» ставим внутрь помещения, чтобы его можно было найти в любой комнате.
        private void PlaceFakeTable()
        {
            Project p = _projects != null ? _projects.Current : null;
            if (p == null || p.Rooms.Count == 0) return;
            PlanPoint c = PlanGeometry.InteriorPoint(p.Rooms[0].Boundary);
            _reality.Center = new Vector3(c.X + 1.5f, p.Rooms[0].Elevation + _reality.Size.y / 2f, c.Z);
        }

        private Dictionary<string, float> ParamsFor(RoomTemplate t)
        {
            var p = new Dictionary<string, float>();
            switch (t)
            {
                case RoomTemplate.Corridor:
                    p[TemplateParam.Length] = corridorLength;
                    p[TemplateParam.Width] = corridorWidth;
                    break;
                case RoomTemplate.LShaped:
                    p[TemplateParam.Width] = lWidth;
                    p[TemplateParam.Depth] = lDepth;
                    p[TemplateParam.CutWidth] = lCutWidth;
                    p[TemplateParam.CutDepth] = lCutDepth;
                    break;
                default:
                    p[TemplateParam.Width] = rectWidth;
                    p[TemplateParam.Depth] = rectDepth;
                    break;
            }
            return p;
        }

        private static string TemplateName(RoomTemplate t)
        {
            switch (t)
            {
                case RoomTemplate.Rectangular: return "прямоугольная";
                case RoomTemplate.LShaped: return "Г-образная";
                case RoomTemplate.Corridor: return "коридор";
                default: return t.ToString();
            }
        }

        // ---------- рисование ----------

        private void Rebuild()
        {
            if (RebuildHolds > 0) { _dirty = true; return; }
            _markers.Clear();
            if (_viewRoot != null) Destroy(_viewRoot);
            _viewRoot = new GameObject("Предпросмотр");
            _viewRoot.transform.SetParent(transform, false);

            Project p = _projects.Current;
            if (p == null) return;
            _selection.Refresh(p);

            var rooms = new Dictionary<string, Room>();
            foreach (Room r in p.Rooms) rooms[r.Id] = r;
            var walls = new Dictionary<string, Wall>();
            foreach (Wall w in p.Walls) walls[w.Id] = w;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (Room r in p.Rooms)
            {
                DrawFloor(r);
                if (showCeiling) DrawCeiling(r);
                PlanPoint c = PlanGeometry.InteriorPoint(r.Boundary);
                MakeMarker(r.Id + "#" + EditorSelection.PartFloor, _viewRoot.transform,
                    new Vector3(c.X, r.Elevation + 0.35f, c.Z), 0.25f);
                MakeMarker(r.Id + "#" + EditorSelection.PartCeiling, _viewRoot.transform,
                    new Vector3(c.X, r.Elevation + r.Height - 0.35f, c.Z), 0.25f);
                foreach (PlanPoint pt in r.Boundary)
                {
                    minX = Mathf.Min(minX, pt.X); maxX = Mathf.Max(maxX, pt.X);
                    minZ = Mathf.Min(minZ, pt.Z); maxZ = Mathf.Max(maxZ, pt.Z);
                }
            }
            foreach (Wall w in p.Walls) DrawWall(w, BaseY(w, rooms));
            foreach (Opening o in p.Openings) DrawOpening(o, walls, rooms);
            foreach (PlacedObject o in p.Objects) DrawObject(o);
            foreach (ProjectLight l in p.Lights) DrawLight(l);
            if (_fakeReality)
                Box("Реальный стол (имитация)", _reality.Center, _reality.Size, Quaternion.identity,
                    new Color(0.5f, 0.5f, 0.55f));

            OnSelectionChanged(_selection.Key);
            if (frameCamera && !_framed && p.Rooms.Count > 0) FrameCamera(minX, maxX, minZ, maxZ);
            if (ViewRebuilt != null) ViewRebuilt();
        }

        private static float BaseY(Wall w, Dictionary<string, Room> rooms)
        {
            Room room;
            if (w.RoomId != null && rooms.TryGetValue(w.RoomId, out room)) return room.Elevation;
            return 0f;
        }

        // Жёлтый маркер выбора. Коллайдера нет, чтобы не мешать хвату в VR.
        private GameObject MakeMarker(string key, Transform parent, Vector3 localPosition, float size)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Выбран";
            DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = localPosition;
            marker.transform.localScale = Vector3.one * size;
            marker.GetComponent<Renderer>().material.color = new Color(1f, 0.9f, 0.1f);
            marker.SetActive(false);
            _markers[key] = marker;
            return marker;
        }

        // Стена: корень стоит в середине стены на уровне 0 и повёрнут вдоль неё (к нему крепится хват в VR),
        // форма с коллайдером и жёлтый маркер выбора лежат внутри.
        private void DrawWall(Wall w, float baseY)
        {
            Vector3 a = new Vector3(w.A.X, 0f, w.A.Z);
            Vector3 b = new Vector3(w.B.X, 0f, w.B.Z);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f) return;

            float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            Color color = w.State == ElementState.Existing ? new Color(0.85f, 0.80f, 0.70f) : new Color(0.55f, 0.85f, 0.55f);
            string wallMaterial = MaterialRules.GetMaterialId(_projects.Current, w.Id, MaterialSlots.Surface);
            if (wallMaterial != null) color = MaterialColor(wallMaterial);

            var root = new GameObject("Стена " + w.Id);
            root.transform.SetParent(_viewRoot.transform, false);
            root.transform.localPosition = (a + b) / 2f;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);   // коллайдер оставляем: за него берётся луч
            body.name = "Форма";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, baseY + w.Height / 2f, 0f);
            body.transform.localScale = new Vector3(len + w.Thickness, w.Height, w.Thickness);
            body.GetComponent<Renderer>().material.color = color;

            // Стена не твёрдая: игрок проходит сквозь неё, как и раньше. Коллайдер-триггер нужен только лучу выбора.
            body.GetComponent<Collider>().isTrigger = true;

            // Жёлтая метка выбора прямо на стене (видна с обеих сторон), на середине высоты.
            GameObject plate = MakeMarker(w.Id, root.transform, new Vector3(0f, baseY + Mathf.Min(1.4f, w.Height / 2f), 0f), 1f);
            plate.transform.localScale = new Vector3(0.45f, 0.45f, w.Thickness + 0.08f);

            if (WallViewBuilt != null) WallViewBuilt(root, w);
        }

        private void DrawOpening(Opening o, Dictionary<string, Wall> walls, Dictionary<string, Room> rooms)
        {
            Wall w;
            if (o.WallId == null || !walls.TryGetValue(o.WallId, out w)) return;

            Vector3 a = new Vector3(w.A.X, 0f, w.A.Z);
            Vector3 b = new Vector3(w.B.X, 0f, w.B.Z);
            Vector3 d = b - a;
            if (d.magnitude < 0.001f) return;
            Vector3 dir = d.normalized;

            float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            Vector3 pos = a + dir * (o.Offset + o.Width / 2f);
            pos.y = BaseY(w, rooms) + o.SillHeight + o.Height / 2f;
            Color color = o.Type == OpeningType.Door ? new Color(0.45f, 0.30f, 0.15f) : new Color(0.5f, 0.8f, 1f);
            Box((o.Type == OpeningType.Door ? "Дверь " : "Окно ") + o.Id, pos,
                new Vector3(o.Width, o.Height, w.Thickness + 0.04f), Quaternion.Euler(0f, yaw, 0f), color);
        }

        private void DrawObject(PlacedObject o)
        {
            Quaternion rot = Quaternion.Euler(o.Rotation.X, o.Rotation.Y, o.Rotation.Z);
            Vector3 size = new Vector3(0.8f, 0.8f, 0.8f);
            Vector3 localCenter = new Vector3(0f, 0.4f, 0f);
            string label = o.CatalogId;

            CatalogItem item = _catalog != null ? _catalog.GetItem(o.CatalogId) : null;
            if (item != null)
            {
                size = new Vector3(item.Size.X, item.Size.Y, item.Size.Z);
                // Точка привязки: пол - центр основания; стена - центр задней стороны; потолок - центр верха.
                if (item.Placement == PlacementRule.Wall) localCenter = new Vector3(0f, 0f, item.Size.Z / 2f);
                else if (item.Placement == PlacementRule.Ceiling) localCenter = new Vector3(0f, -item.Size.Y / 2f, 0f);
                else localCenter = new Vector3(0f, item.Size.Y / 2f, 0f);
                label = item.Name;
            }

            float hue = Mathf.Abs((o.CatalogId ?? "").GetHashCode() % 100) / 100f;

            // Предметы, которые пересекаются со стеной, другим предметом или реальным объектом, красные.
            bool collides = false;
            if (_catalog != null && _projects.Current != null)
            {
                Project proj = _projects.Current;
                collides = CollisionService.CheckObject(proj, o, _catalog,
                    _fakeReality ? _reality : null, FloorOf(proj)).HasCollisions;
            }
            Color baseColor = Color.HSVToRGB(hue, 0.55f, 0.9f);
            string objectMaterial = MaterialRules.GetMaterialId(_projects.Current, o.Id, MaterialSlots.Surface);
            if (objectMaterial != null) baseColor = MaterialColor(objectMaterial);
            Color color = collides ? new Color(0.95f, 0.2f, 0.2f) : baseColor;

            // Корень стоит ровно в точке привязки предмета (к нему крепится хват в VR), форма смещена внутри.
            var root = new GameObject("Объект " + label + (collides ? " [пересечение]" : "") + " (" + o.Id + ")");
            root.transform.SetParent(_viewRoot.transform, false);
            root.transform.localPosition = new Vector3(o.Position.X, o.Position.Y, o.Position.Z);
            root.transform.localRotation = rot;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);   // коллайдер оставляем: за него берётся хват
            body.name = "Форма";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = localCenter;
            body.transform.localScale = size;
            body.GetComponent<Renderer>().material.color = color;

            // Жёлтый маркер над выбранным предметом (едет вместе с ним).
            MakeMarker(o.Id, root.transform, localCenter + Vector3.up * (size.y / 2f + 0.25f), 0.15f);

            if (ObjectViewBuilt != null) ObjectViewBuilt(root, o);
        }

        private void DrawLight(ProjectLight l)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Светильник " + l.Id;
            go.transform.SetParent(_viewRoot.transform, false);
            go.transform.position = new Vector3(l.Position.X, l.Position.Y, l.Position.Z);
            go.transform.localScale = Vector3.one * 0.25f;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().material.color = l.Enabled ? new Color(1f, 0.9f, 0.3f) : new Color(0.3f, 0.3f, 0.3f);
        }

        private void DrawFloor(Room r)
        {
            string floorMaterial = MaterialRules.GetMaterialId(_projects.Current, r.Id, MaterialSlots.Floor);
            Color color = floorMaterial != null ? MaterialColor(floorMaterial) : new Color(0.40f, 0.40f, 0.46f);
            DrawPlane(r, "Пол ", r.Elevation, true, color, true);
        }

        // Потолок виден только снизу: сверху его нет, и камеру предпросмотра он не закрывает.
        private void DrawCeiling(Room r)
        {
            string ceilingMaterial = MaterialRules.GetMaterialId(_projects.Current, r.Id, MaterialSlots.Ceiling);
            Color color = ceilingMaterial != null ? MaterialColor(ceilingMaterial) : new Color(0.93f, 0.93f, 0.95f);
            DrawPlane(r, "Потолок ", r.Elevation + r.Height, false, color, false);
        }

        // Плоскость по контуру помещения. facesUp = true: видна сверху (пол); false: видна снизу (потолок).
        private void DrawPlane(Room r, string namePrefix, float y, bool facesUp, Color color, bool solid)
        {
            var pts = new List<Vector2>();
            foreach (PlanPoint pt in r.Boundary) pts.Add(new Vector2(pt.X, pt.Z));
            int[] tris = Triangulate(pts);
            if (tris.Length == 0) return;

            int n = pts.Count;
            var verts = new Vector3[n];
            var normals = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(pts[i].x, y, pts[i].y);
                normals[i] = facesUp ? Vector3.up : Vector3.down;
            }

            var indices = new int[tris.Length];
            for (int t = 0; t < tris.Length; t += 3)
            {
                indices[t] = tris[t];
                indices[t + 1] = facesUp ? tris[t + 2] : tris[t + 1];
                indices[t + 2] = facesUp ? tris[t + 1] : tris[t + 2];
            }

            var mesh = new Mesh { name = namePrefix + r.Name };
            mesh.vertices = verts;
            mesh.triangles = indices;
            mesh.normals = normals;
            mesh.RecalculateBounds();

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = namePrefix + r.Name;
            go.transform.SetParent(_viewRoot.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (solid) go.AddComponent<MeshCollider>().sharedMesh = mesh;   // твёрдый пол: на нём стоит XR-риг
            go.GetComponent<Renderer>().material.color = color;
        }

        private GameObject Box(string objName, Vector3 center, Vector3 size, Quaternion rot, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objName;
            go.transform.SetParent(_viewRoot.transform, false);
            go.transform.SetPositionAndRotation(center, rot);
            go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }

        private void FrameCamera(float minX, float maxX, float minZ, float maxZ)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 center = new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
            float size = Mathf.Max(maxX - minX, maxZ - minZ, 2f);
            cam.transform.position = center + new Vector3(0f, size * 1.1f, -size * 0.9f);
            cam.transform.LookAt(center);
            _framed = true;
        }

        private static int FloorOf(Project p)
        {
            return p.Rooms.Count > 0 ? p.Rooms[0].Floor : 1;
        }

        // =====================================================================
        // Меню
        // =====================================================================

        private Action _pending;
        private UiState _ui = new UiState();
        private readonly List<CategoryUi> _categories = new List<CategoryUi>();
        private int _categoryIndex;
        private bool _wallsAll;
        private readonly HashSet<string> _closed = new HashSet<string> { "room", "reality" };
        private Vector2 _scroll;
        private GUIStyle _label, _statusLabel, _button, _rowButton, _header, _swatchDark, _swatchLight, _toggle;

        private sealed class SwatchUi
        {
            public string Id;
            public string Name;
            public Color Color;
            public bool Current;
        }

        private sealed class ItemUi
        {
            public string Id;
            public string Name;
        }

        private sealed class CategoryUi
        {
            public string Name;
            public List<ItemUi> Items = new List<ItemUi>();
        }

        // Снимок состояния для OnGUI. Меняется только в Update, поэтому меню не «ломается» посреди отрисовки.
        private sealed class UiState
        {
            public bool HasProject;
            public string Title = "";
            public int Walls;
            public int Objects;
            public int UndoCount;
            public bool CanUndo;
            public bool CanRedo;
            public string UndoName = "";
            public string RedoName = "";
            public bool HasRoom;
            public string FloorText = "";
            public string CeilingText = "";
            public Color FloorColor = Color.gray;
            public Color CeilingColor = Color.gray;
            public SelectionKind Kind;
            public string SelectionLabel = "ничего не выбрано";
            public string SelectionMaterialText = "";
            public Color SelectionColor = Color.gray;
            public bool IsWallItem;
            public bool WallInRoom;
            public bool WallIsMain;
            public string MaterialTitle = "";
            public bool CanResetMaterial;
            public List<SwatchUi> Swatches = new List<SwatchUi>();
        }

        private void BuildCategories()
        {
            _categories.Clear();
            if (_catalog == null) return;
            foreach (CatalogCategory c in _catalog.GetCategories())
            {
                var ui = new CategoryUi { Name = c.Name };
                foreach (CatalogItem item in _catalog.Search(null, c.Id))
                    ui.Items.Add(new ItemUi { Id = item.Id, Name = item.Name });
                if (ui.Items.Count > 0) _categories.Add(ui);
            }
            if (_categoryIndex >= _categories.Count) _categoryIndex = 0;
        }

        private string MaterialText(Project p, string targetId, string slot)
        {
            string id = MaterialRules.GetMaterialId(p, targetId, slot);
            if (id == null) return "по умолчанию";
            MaterialInfo info = _catalog != null ? _catalog.GetMaterial(id) : null;
            return info != null ? info.Name : id;
        }

        private static Color MaterialColorOrGray(string materialId)
        {
            return materialId == null ? new Color(0.5f, 0.5f, 0.5f) : MaterialColor(materialId);
        }

        private void RefreshUi()
        {
            var ui = new UiState();
            Project p = _projects != null ? _projects.Current : null;
            if (p != null && _commands != null)
            {
                ui.HasProject = true;
                ui.Title = p.Name + " (источник: " + p.Source + ")";
                ui.Walls = p.Walls.Count;
                ui.Objects = p.Objects.Count;
                ui.UndoCount = _commands.UndoCount;
                ui.CanUndo = _commands.CanUndo;
                ui.CanRedo = _commands.CanRedo;
                ui.UndoName = ui.CanUndo ? _commands.NextUndoName : "";
                ui.RedoName = ui.CanRedo ? _commands.NextRedoName : "";

                if (p.Rooms.Count > 0)
                {
                    Room room = p.Rooms[0];
                    ui.HasRoom = true;
                    ui.FloorText = MaterialText(p, room.Id, MaterialSlots.Floor);
                    ui.CeilingText = MaterialText(p, room.Id, MaterialSlots.Ceiling);
                    ui.FloorColor = MaterialColorOrGray(MaterialRules.GetMaterialId(p, room.Id, MaterialSlots.Floor));
                    ui.CeilingColor = MaterialColorOrGray(MaterialRules.GetMaterialId(p, room.Id, MaterialSlots.Ceiling));
                }

                ui.Kind = _selection.KindOf(p);
                string target = _selection.SelectedId;
                string slot = MaterialSlots.Surface;
                switch (ui.Kind)
                {
                    case SelectionKind.Object:
                    {
                        PlacedObject o = p.Objects.Find(x => x.Id == target);
                        CatalogItem item = (o != null && _catalog != null) ? _catalog.GetItem(o.CatalogId) : null;
                        ui.SelectionLabel = "Предмет: " + (item != null ? item.Name : (o != null ? o.CatalogId : "?"));
                        ui.IsWallItem = item != null && item.Placement == PlacementRule.Wall;
                        ui.MaterialTitle = "материал предмета";
                        break;
                    }
                    case SelectionKind.Wall:
                    {
                        Wall w = p.Walls.Find(x => x.Id == target);
                        ui.WallIsMain = WallPlacement.IsRoomBoundaryWall(p, w);
                        ui.SelectionLabel = (ui.WallIsMain ? "Основная стена" : "Стена") + (w != null ? " (" + WallRules.Length(w).ToString("0.00") + " м, толщина " + w.Thickness.ToString("0.00") + " м)" : "");
                        ui.WallInRoom = w != null && w.RoomId != null;
                        ui.MaterialTitle = "материал стены";
                        break;
                    }
                    case SelectionKind.Floor:
                        ui.SelectionLabel = "Пол";
                        ui.MaterialTitle = "материал пола";
                        slot = MaterialSlots.Floor;
                        break;
                    case SelectionKind.Ceiling:
                        ui.SelectionLabel = "Потолок";
                        ui.MaterialTitle = "материал потолка";
                        slot = MaterialSlots.Ceiling;
                        break;
                }

                if (ui.Kind != SelectionKind.None && _catalog != null)
                {
                    string current = MaterialRules.GetMaterialId(p, target, slot);
                    ui.CanResetMaterial = current != null;
                    ui.SelectionMaterialText = MaterialText(p, target, slot);
                    ui.SelectionColor = MaterialColorOrGray(current);
                    foreach (MaterialInfo m in MaterialRules.GetAllowedMaterials(p, _catalog, target, slot))
                        ui.Swatches.Add(new SwatchUi { Id = m.Id, Name = m.Name, Color = MaterialColor(m.Id), Current = m.Id == current });
                }
            }
            _ui = ui;
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _label.normal.textColor = Color.white;
            _statusLabel = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
            _statusLabel.normal.textColor = new Color(1f, 0.9f, 0.3f);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 28f };
            _rowButton = new GUIStyle(_button) { alignment = TextAnchor.MiddleLeft };
            _header = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, fixedHeight = 30f, alignment = TextAnchor.MiddleLeft };
            _swatchDark = new GUIStyle(_button) { fontSize = 12, clipping = TextClipping.Clip };
            _swatchLight = new GUIStyle(_swatchDark);
            SetTextColor(_swatchDark, Color.white);
            SetTextColor(_swatchLight, Color.black);
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 14 };
            _toggle.normal.textColor = Color.white;
            _toggle.onNormal.textColor = Color.white;
            _toggle.hover.textColor = Color.white;
            _toggle.onHover.textColor = Color.white;
        }

        private static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
        }

        private static float Luminance(Color c) { return 0.3f * c.r + 0.59f * c.g + 0.11f * c.b; }

        // ---- элементы меню ----

        private void Btn(string text, Action action, bool enabled = true)
        {
            bool old = GUI.enabled;
            GUI.enabled = old && enabled;
            if (GUILayout.Button(text, _button) && action != null && _pending == null) _pending = action;
            GUI.enabled = old;
        }

        private void ToggleBtn(string text, bool on, Action action)
        {
            Color old = GUI.backgroundColor;
            if (on) GUI.backgroundColor = new Color(1f, 0.9f, 0.3f);
            if (GUILayout.Button(text, _button) && action != null && _pending == null) _pending = action;
            GUI.backgroundColor = old;
        }

        private bool Section(string key, string title)
        {
            bool open = !_closed.Contains(key);
            if (GUILayout.Button((open ? "[-] " : "[+] ") + title, _header) && _pending == null)
            {
                string k = key;
                _pending = () => { if (!_closed.Remove(k)) _closed.Add(k); };
            }
            return open;
        }

        private void ColorRow(Color color, string text, Action onClick)
        {
            GUILayout.BeginHorizontal();
            Rect swatch = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f), GUILayout.Height(18f));
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(swatch, Texture2D.whiteTexture);
            GUI.color = old;
            if (onClick != null)
            {
                if (GUILayout.Button(text, _rowButton) && _pending == null) _pending = onClick;
            }
            else GUILayout.Label(text, _label);
            GUILayout.EndHorizontal();
        }

        private void Stepper(string title, float value, Action<float> set, float min, float max, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _label, GUILayout.Width(190f));
            float lower = Mathf.Max(min, value - step);
            float upper = Mathf.Min(max, value + step);
            if (GUILayout.Button("-", _button, GUILayout.Width(36f)) && _pending == null) _pending = () => set(lower);
            GUILayout.Label(value.ToString("0.0") + " м", _label, GUILayout.Width(60f));
            if (GUILayout.Button("+", _button, GUILayout.Width(36f)) && _pending == null) _pending = () => set(upper);
            GUILayout.EndHorizontal();
        }

        // ---- отрисовка ----

        private void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 720f, 0.8f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUILayout.BeginArea(new Rect(8f, 8f, 440f, Screen.height / scale - 16f), GUI.skin.box);

            UiState ui = _ui;
            if (!ui.HasProject)
            {
                GUILayout.Label(_status, _statusLabel);
                Btn("Создать комнату", () => { source = PreviewSource.NewTemplateRoom; BuildProject(); });
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Проект: " + ui.Title, _label);
            GUILayout.Label("Стен: " + ui.Walls + "   Предметов: " + ui.Objects + "   Команд в истории: " + ui.UndoCount, _label);
            GUILayout.Label("» " + _status, _statusLabel);

            // Цветовая полоса: пол и потолок (нажатие выбирает их) и выбранный элемент.
            if (ui.HasRoom)
            {
                ColorRow(ui.FloorColor, "Пол: " + ui.FloorText, SelectFloor);
                ColorRow(ui.CeilingColor, "Потолок: " + ui.CeilingText, SelectCeiling);
            }
            ColorRow(ui.SelectionColor, "Выбрано: " + ui.SelectionLabel
                + (ui.Kind != SelectionKind.None ? " - " + ui.SelectionMaterialText : ""), null);
            GUILayout.Space(4f);

            _scroll = GUILayout.BeginScrollView(_scroll);

            if (Section("select", "Выбор"))
            {
                GUILayout.BeginHorizontal();
                Btn("← Предмет", () => SelectCycle(SelectionKind.Object, -1));
                Btn("Предмет →", () => SelectCycle(SelectionKind.Object, 1));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Btn("← Стена", () => SelectCycle(SelectionKind.Wall, -1));
                Btn("Стена →", () => SelectCycle(SelectionKind.Wall, 1));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Btn("Пол", SelectFloor, ui.HasRoom);
                Btn("Потолок", SelectCeiling, ui.HasRoom);
                Btn("Снять выбор", _selection.Clear, ui.Kind != SelectionKind.None);
                GUILayout.EndHorizontal();
            }

            if (Section("selected", "Выбранный элемент: " + ui.SelectionLabel))
            {
                DrawSelectedSection(ui);
            }

            if (Section("add", "Добавить"))
            {
                DrawAddSection(ui);
            }

            if (Section("room", "Комната и потолок"))
            {
                DrawRoomSection();
            }

            if (Section("reality", "Реальная геометрия (имитация скана)"))
            {
                Btn(_fakeReality ? "Убрать «реальный стол»" : "Показать «реальный стол»", ToggleReality);
                GUILayout.Label("Предмет, который пересекается с «реальным столом», становится красным (как и при пересечении со стеной или другим предметом).", _label);
            }

            if (Section("history", "История и сохранение"))
            {
                Btn("Отменить" + (ui.CanUndo ? ": " + ui.UndoName : ""), UndoAction, ui.CanUndo);
                Btn("Повторить" + (ui.CanRedo ? ": " + ui.RedoName : ""), RedoAction, ui.CanRedo);
                Btn("Сохранить и открыть заново", ReopenFromDisk);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSelectedSection(UiState ui)
        {
            if (ui.Kind == SelectionKind.None)
            {
                GUILayout.Label("Выберите предмет, стену, пол или потолок: кнопками в блоке «Выбор» или (в VR) хватом предмета.", _label);
                return;
            }

            bool mainWall = ui.Kind == SelectionKind.Wall && ui.WallIsMain;
            if (mainWall)
            {
                GUILayout.Label("Основную стену комнаты двигать, крутить и удалять нельзя. Можно покрасить и добавить окно или дверь.", _label);
                GUILayout.BeginHorizontal();
                Btn("Добавить окно", () => AddOpeningToSelected(OpeningType.Window));
                Btn("Добавить дверь", () => AddOpeningToSelected(OpeningType.Door));
                GUILayout.EndHorizontal();
            }

            bool movable = (ui.Kind == SelectionKind.Object || ui.Kind == SelectionKind.Wall) && !mainWall;
            if (movable)
            {
                if (ui.IsWallItem)
                {
                    GUILayout.Label("Предмет на стене: вдоль стены и по высоте, шаг 0,25 м.", _label);
                    GUILayout.BeginHorizontal();
                    Btn("← вдоль стены", () => SlideSelected(-EditorTools.MoveStep));
                    Btn("вдоль стены →", () => SlideSelected(EditorTools.MoveStep));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    Btn("↑ выше", () => RaiseSelected(EditorTools.MoveStep));
                    Btn("↓ ниже", () => RaiseSelected(-EditorTools.MoveStep));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("Сдвиг на 0,25 м:", _label);
                    GUILayout.BeginHorizontal();
                    Btn("← влево", () => MoveSelected(-EditorTools.MoveStep, 0f));
                    Btn("вправо →", () => MoveSelected(EditorTools.MoveStep, 0f));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    Btn("↑ вперёд", () => MoveSelected(0f, EditorTools.MoveStep));
                    Btn("↓ назад", () => MoveSelected(0f, -EditorTools.MoveStep));
                    GUILayout.EndHorizontal();
                }

                if (ui.Kind == SelectionKind.Wall)
                {
                    GUILayout.Label("Размер стены (длина меняется с конца стены):", _label);
                    GUILayout.BeginHorizontal();
                    Btn("Длиннее +0,25 м", () => ResizeSelected(EditorTools.LengthStep, 0f));
                    Btn("Короче -0,25 м", () => ResizeSelected(-EditorTools.LengthStep, 0f));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    Btn("Толще +0,05 м", () => ResizeSelected(0f, EditorTools.ThicknessStep));
                    Btn("Тоньше -0,05 м", () => ResizeSelected(0f, -EditorTools.ThicknessStep));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    Btn("Повернуть -90°", () => RotateSelected(-EditorTools.WallRotationStep));
                    Btn("Повернуть +90°", () => RotateSelected(EditorTools.WallRotationStep));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    Btn("Добавить окно", () => AddOpeningToSelected(OpeningType.Window));
                    Btn("Добавить дверь", () => AddOpeningToSelected(OpeningType.Door));
                    GUILayout.EndHorizontal();
                }
                else if (!ui.IsWallItem)
                {
                    GUILayout.BeginHorizontal();
                    Btn("Повернуть -15°", () => RotateSelected(-15f));
                    Btn("Повернуть +15°", () => RotateSelected(15f));
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                if (ui.Kind == SelectionKind.Object) Btn("Дублировать", DuplicateSelected);
                Btn("Удалить", DeleteSelected);
                GUILayout.EndHorizontal();
            }

            // Материал выбранного: цветные кнопки только из допустимых для этого элемента.
            GUILayout.Space(4f);
            if (ui.Swatches.Count == 0)
            {
                GUILayout.Label("Для этого элемента материал менять нельзя (в каталоге нет допустимых).", _label);
                return;
            }

            GUILayout.Label("Сменить " + ui.MaterialTitle + " (отмечен точкой):", _label);
            if (ui.Kind == SelectionKind.Wall && ui.WallInRoom)
            {
                bool value = GUILayout.Toggle(_wallsAll, "На все стены комнаты сразу", _toggle);
                if (value != _wallsAll && _pending == null) { bool nv = value; _pending = () => _wallsAll = nv; }
            }

            GUILayout.BeginHorizontal();
            for (int i = 0; i < ui.Swatches.Count; i++)
            {
                if (i > 0 && i % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                SwatchUi s = ui.Swatches[i];
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = s.Color;
                GUIStyle style = Luminance(s.Color) > 0.55f ? _swatchLight : _swatchDark;
                if (GUILayout.Button((s.Current ? "• " : "") + s.Name, style) && _pending == null)
                {
                    string materialId = s.Id;
                    _pending = () => ApplyMaterial(materialId);
                }
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            Btn("Сбросить к умолчанию", ResetMaterial, ui.CanResetMaterial);
        }

        private void DrawAddSection(UiState ui)
        {
            if (_catalog == null || _categories.Count == 0)
            {
                GUILayout.Label("Каталог не загружен.", _label);
            }
            else
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < _categories.Count; i++)
                {
                    if (i > 0 && i % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                    int index = i;
                    ToggleBtn(_categories[i].Name, i == _categoryIndex, () => _categoryIndex = index);
                }
                GUILayout.EndHorizontal();

                CategoryUi category = _categories[Mathf.Clamp(_categoryIndex, 0, _categories.Count - 1)];
                GUILayout.BeginHorizontal();
                for (int i = 0; i < category.Items.Count; i++)
                {
                    if (i > 0 && i % 2 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                    string id = category.Items[i].Id;
                    Btn(category.Items[i].Name, () => AddItem(id));
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Предмет появляется рядом с вами (или в центре комнаты). Если свободного места нет, он всё равно появится, красным.", _label);
            }

            GUILayout.Space(4f);
            Btn("Добавить перегородку", AddPartitionAction, ui.HasRoom);
        }

        private void DrawRoomSection()
        {
            GUILayout.BeginHorizontal();
            ToggleBtn("Прямоугольная", template == RoomTemplate.Rectangular, () => template = RoomTemplate.Rectangular);
            ToggleBtn("Г-образная", template == RoomTemplate.LShaped, () => template = RoomTemplate.LShaped);
            ToggleBtn("Коридор", template == RoomTemplate.Corridor, () => template = RoomTemplate.Corridor);
            GUILayout.EndHorizontal();

            switch (template)
            {
                case RoomTemplate.LShaped:
                    Stepper("Ширина", lWidth, v => lWidth = v, 4f, 30f, 0.5f);
                    Stepper("Глубина", lDepth, v => lDepth = v, 4f, 30f, 0.5f);
                    Stepper("Вырез: ширина", lCutWidth, v => lCutWidth = v, 1f, 20f, 0.5f);
                    Stepper("Вырез: глубина", lCutDepth, v => lCutDepth = v, 1f, 20f, 0.5f);
                    break;
                case RoomTemplate.Corridor:
                    Stepper("Длина", corridorLength, v => corridorLength = v, 3f, 40f, 0.5f);
                    Stepper("Ширина", corridorWidth, v => corridorWidth = v, 1.2f, 8f, 0.5f);
                    break;
                default:
                    Stepper("Ширина", rectWidth, v => rectWidth = v, 2f, 30f, 0.5f);
                    Stepper("Глубина", rectDepth, v => rectDepth = v, 2f, 30f, 0.5f);
                    break;
            }

            Btn("Создать комнату заново (предметы пропадут)", () => { source = PreviewSource.NewTemplateRoom; BuildProject(); });
            GUILayout.Space(4f);
            Btn(showCeiling ? "Скрыть потолок" : "Показать потолок", ToggleCeiling);
        }

        // =====================================================================
        // Действия меню (выполняются в Update)
        // =====================================================================

        private void Run(Action action)
        {
            try
            {
                ApplyReality();
                action();
            }
            catch (Exception ex)
            {
                _status = "Ошибка: " + ex.Message;
                Debug.LogError(ex);
            }
        }

        private void Report(ToolResult r)
        {
            _status = r.Message + (r.Warning != null ? " Красный: " + r.Warning : "");
        }

        private bool ToolsReady()
        {
            if (_tools != null && _projects != null && _projects.Current != null) return true;
            _status = "Каталог не загружен, инструменты недоступны.";
            return false;
        }

        private PlanPoint NearPoint()
        {
            PlanPoint near;
            if (TryGetSpawnPoint(out near)) return near;
            Project p = _projects.Current;
            return PlanGeometry.InteriorPoint(p.Rooms[0].Boundary);
        }

        private void SelectCycle(SelectionKind kind, int step)
        {
            string id = _selection.Cycle(_projects.Current, kind, step);
            RefreshUi();
            if (id == null) _status = kind == SelectionKind.Wall ? "Стен нет." : "Предметов нет.";
            else _status = "Выбрано: " + _ui.SelectionLabel + ".";
        }

        private void SelectFloor()
        {
            Project p = _projects.Current;
            if (p.Rooms.Count == 0) return;
            _selection.SelectRoomPart(p.Rooms[0].Id, EditorSelection.PartFloor);
            _status = "Выбран пол.";
        }

        private void SelectCeiling()
        {
            Project p = _projects.Current;
            if (p.Rooms.Count == 0) return;
            _selection.SelectRoomPart(p.Rooms[0].Id, EditorSelection.PartCeiling);
            _status = "Выбран потолок.";
        }

        private void AddItem(string catalogId)
        {
            if (!ToolsReady()) return;
            ToolResult r = _tools.AddFromCatalog(catalogId, NearPoint());
            Report(r);
            if (r.Success && r.ObjectId != null) _selection.Select(r.ObjectId);
        }

        private void AddPartitionAction()
        {
            if (!ToolsReady()) return;
            ToolResult r = _tools.AddPartition(NearPoint());
            Report(r);
            if (r.Success && r.ObjectId != null) _selection.Select(r.ObjectId);
        }

        private void MoveSelected(float dx, float dz)
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            Report(_tools.MoveBy(_selection.SelectedId, dx, dz));
        }

        private void SlideSelected(float meters)
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            Report(_tools.SlideBy(_selection.SelectedId, meters));
        }

        private void RaiseSelected(float meters)
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            Report(_tools.RaiseBy(_selection.SelectedId, meters));
        }

        private void ResizeSelected(float deltaLength, float deltaThickness)
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            Report(_tools.ResizeWall(_selection.SelectedId, deltaLength, deltaThickness));
        }

        private void RotateSelected(float degrees)
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            Report(_tools.RotateBy(_selection.SelectedId, degrees));
        }

        private void DuplicateSelected()
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            ToolResult r = _tools.Duplicate(_selection.SelectedId);
            Report(r);
            if (r.Success && r.ObjectId != null) _selection.Select(r.ObjectId);
        }

        private void DeleteSelected()
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            Report(_tools.Delete(_selection.SelectedId));
        }

        private void AddOpeningToSelected(OpeningType type)
        {
            if (!ToolsReady() || !_selection.HasSelection) return;
            Report(_tools.AddOpening(_selection.SelectedId, type));
        }

        private void ToggleReality()
        {
            _fakeReality = !_fakeReality;
            ApplyReality();
            _status = _fakeReality ? "«Реальный стол» показан." : "«Реальный стол» убран.";
            Rebuild();
        }

        private void ToggleCeiling()
        {
            showCeiling = !showCeiling;
            Rebuild();
        }

        private void UndoAction()
        {
            string name = _commands.NextUndoName;
            _commands.Undo();
            _status = "Отменено: " + name + ".";
        }

        private void RedoAction()
        {
            string name = _commands.NextRedoName;
            _commands.Redo();
            _status = "Повторено: " + name + ".";
        }

        // ---------- материалы (B-12) ----------

        private string SlotOfSelection(SelectionKind kind)
        {
            if (kind == SelectionKind.Floor) return MaterialSlots.Floor;
            if (kind == SelectionKind.Ceiling) return MaterialSlots.Ceiling;
            return MaterialSlots.Surface;
        }

        private void ApplyMaterial(string materialId)
        {
            Project p = _projects.Current;
            SelectionKind kind = _selection.KindOf(p);
            if (kind == SelectionKind.None) { _status = "Сначала выберите элемент."; return; }

            string target = _selection.SelectedId;
            MaterialInfo info = _catalog.GetMaterial(materialId);
            string name = info != null ? info.Name : materialId;

            if (kind == SelectionKind.Wall && _wallsAll)
            {
                Wall wall = p.Walls.Find(w => w.Id == target);
                if (wall != null && wall.RoomId != null)
                {
                    _commands.Execute(MaterialCommands.ApplyToRoomWalls(p, wall.RoomId, materialId, _catalog));
                    _status = "Все стены комнаты: " + name + ".";
                    return;
                }
            }

            _commands.Execute(new SetMaterialCommand(target, SlotOfSelection(kind), materialId, _catalog));
            _status = _ui.SelectionLabel + ": " + name + ".";
        }

        private void ResetMaterial()
        {
            Project p = _projects.Current;
            SelectionKind kind = _selection.KindOf(p);
            if (kind == SelectionKind.None) return;
            string target = _selection.SelectedId;

            if (kind == SelectionKind.Wall && _wallsAll)
            {
                Wall wall = p.Walls.Find(w => w.Id == target);
                if (wall != null && wall.RoomId != null)
                {
                    var commands = new List<ICommand>();
                    foreach (Wall w in p.Walls)
                        if (w.RoomId == wall.RoomId)
                            commands.Add(new SetMaterialCommand(w.Id, MaterialSlots.Surface, null, _catalog));
                    _commands.Execute(new CompositeCommand("Сбросить материалы стен", commands));
                    _status = "Материалы всех стен комнаты сброшены.";
                    return;
                }
            }

            _commands.Execute(new SetMaterialCommand(target, SlotOfSelection(kind), null, _catalog));
            _status = "Материал сброшен к умолчанию.";
        }

        private void ReopenFromDisk()
        {
            _projects.Save();
            string id = _projects.Current.Id;

            _projects.ProjectChanged -= Rebuild;
            _projects = new ProjectService(new ProjectStorage(_storageDir));
            _projects.Load(id);
            AttachCommands();
            Rebuild();

            _status = "Проект записан на диск и открыт заново. Предметов: " + _projects.Current.Objects.Count + ".";
        }

        // Разбиение многоугольника на треугольники (метод "отсечения ушей").
        private static int[] Triangulate(List<Vector2> pts)
        {
            var result = new List<int>();
            int n = pts.Count;
            if (n < 3) return result.ToArray();

            var idx = new List<int>();
            for (int i = 0; i < n; i++) idx.Add(i);

            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            if (area < 0f) idx.Reverse();

            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];
                    Vector2 a = pts[i0], b = pts[i1], c = pts[i2];
                    if (Cross(b - a, c - b) <= 0f) continue;

                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (PointInTriangle(pts[j], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;

                    result.Add(i0); result.Add(i1); result.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result.ToArray();
        }

        private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }
    }
}
