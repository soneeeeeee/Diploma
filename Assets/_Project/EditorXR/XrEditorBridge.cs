// XR-надстройка над ProjectPreview (B-07, часть 2, FR-103, FR-113).
// ProjectPreview рисует проект и выполняет все действия, он не знает про XR. Этот компонент добавляет только то,
// что зависит от XR Interaction Toolkit: хват предметов и добавленных стен лучом контроллера (выбор и перемещение),
// выбор основных стен лучом (их можно выбрать и покрасить, но не двигать),
// постановку XR-рига в комнату и точку, возле которой появляются новые предметы.
// Управления с клавиатуры и кнопок контроллеров здесь нет: все действия - в меню ProjectPreview.
// Кнопки Touch-контроллеров добавим, когда будет гарнитура.
// Все изменения проекта выполняют EditorTools и команды (undo/redo): здесь правил нет.

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using XrInterior.ProjectData;
using XrInterior.ProjectData.UnityIntegration;

namespace XrInterior.EditorXR
{
    public class XrEditorBridge : MonoBehaviour
    {
        [Header("Предпросмотр проекта (если пусто, ищется на этом же объекте или в сцене)")]
        public ProjectPreview preview;

        [Header("XR-риг: перетащить сюда объект XR Origin (XR Rig)")]
        public Transform xrOrigin;

        private ProjectPreview _preview;
        private bool _rigPlaced;

        // ---------- запуск ----------

        private void Awake()
        {
            _preview = preview != null ? preview : GetComponent<ProjectPreview>();
            if (_preview == null) _preview = FindFirstObjectByType<ProjectPreview>();
            if (_preview == null)
            {
                Debug.LogError("XrEditorBridge: в сцене нет ProjectPreview.");
                enabled = false;
                return;
            }

            _preview.frameCamera = false;   // камерой управляет XR-риг, а не предпросмотр
            _preview.ObjectViewBuilt += OnObjectViewBuilt;
            _preview.WallViewBuilt += OnWallViewBuilt;
            _preview.ViewRebuilt += OnViewRebuilt;
            _preview.ProjectReopened += OnProjectReopened;
        }

        private void Start()
        {
            // Новые предметы появляются рядом с пользователем: за точку отсчёта берём камеру XR-рига.
            Camera cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (cam != null) _preview.spawnAnchor = cam.transform;
            AllowRaysToHitTriggers();
        }

        // Стены не твёрдые (коллайдеры-триггеры, игрок проходит сквозь них), но луч должен их «видеть», чтобы выбрать стену.
        // Свойство называется raycastTriggerInteraction и лежит в разных классах в зависимости от версии XRI, поэтому ищем по имени.
        private static void AllowRaysToHitTriggers()
        {
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null) continue;
                Type type = behaviour.GetType();
                if (type.Namespace == null || !type.Namespace.StartsWith("UnityEngine.XR.Interaction.Toolkit")) continue;
                PropertyInfo property = type.GetProperty("raycastTriggerInteraction", BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.PropertyType == typeof(QueryTriggerInteraction) && property.CanWrite)
                    property.SetValue(behaviour, QueryTriggerInteraction.Collide, null);
            }
        }

        private void OnDestroy()
        {
            if (_preview != null)
            {
                _preview.ObjectViewBuilt -= OnObjectViewBuilt;
                _preview.WallViewBuilt -= OnWallViewBuilt;
                _preview.ViewRebuilt -= OnViewRebuilt;
                _preview.ProjectReopened -= OnProjectReopened;
            }
        }

        // ---------- хват ----------

        private static XRGrabInteractable MakeGrabbable(GameObject root)
        {
            // Хват: двигаем, не вращаем (поворот делается кнопками меню).
            XRGrabInteractable grab = root.AddComponent<XRGrabInteractable>();
            Rigidbody body = root.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.useGravity = false; }
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach = false;
            grab.trackRotation = false;
            grab.useDynamicAttach = true;
            return grab;
        }

        private void OnObjectViewBuilt(GameObject root, PlacedObject o)
        {
            string id = o.Id;
            Transform visual = root.transform;
            XRGrabInteractable grab = MakeGrabbable(root);
            grab.selectEntered.AddListener(args => OnGrabbed(id, "Предмет"));
            grab.selectExited.AddListener(args => OnReleased(id, visual, false));
        }

        private void OnWallViewBuilt(GameObject root, Wall w)
        {
            string id = w.Id;

            // Основную стену комнаты можно только выбрать (и покрасить), добавленную перегородку ещё и переносить.
            if (WallPlacement.IsRoomBoundaryWall(_preview.Projects.Current, w))
            {
                XRSimpleInteractable pick = root.AddComponent<XRSimpleInteractable>();
                pick.selectEntered.AddListener(args =>
                {
                    _preview.Selection.Select(id);
                    _preview.Status = "Выбрана основная стена.";
                });
                return;
            }

            Transform visual = root.transform;
            XRGrabInteractable grab = MakeGrabbable(root);
            grab.selectEntered.AddListener(args => OnGrabbed(id, "Стена"));
            grab.selectExited.AddListener(args => OnReleased(id, visual, true));
        }

        private void OnGrabbed(string id, string what)
        {
            _preview.RebuildHolds++;   // пока элемент в руке, сцена не перерисовывается
            _preview.Selection.Select(id);
            _preview.Status = "Выбрано: " + what + ".";
        }

        private void OnReleased(string id, Transform visual, bool isWall)
        {
            try
            {
                Project p = _preview.Projects.Current;
                if (p == null || visual == null || _preview.Tools == null || _preview.ViewRoot == null) return;

                Vector3 local = _preview.ViewRoot.InverseTransformPoint(visual.position);

                if (isWall)
                {
                    Wall wall = p.Walls.Find(x => x.Id == id);
                    if (wall == null) return;
                    // Корень стены стоит в её середине на нулевой высоте: считаем, на сколько его сдвинули.
                    float wx = local.x - (wall.A.X + wall.B.X) / 2f;
                    float wz = local.z - (wall.A.Z + wall.B.Z) / 2f;
                    if (wx * wx + wz * wz < 0.02f * 0.02f) { _preview.Status = "Выбрана стена."; return; }
                    Do(() => Report(_preview.Tools.MoveBy(id, wx, wz)));
                    return;
                }

                PlacedObject o = p.Objects.Find(x => x.Id == id);
                if (o == null) return;
                float dx = local.x - o.Position.X;
                float dy = local.y - o.Position.Y;
                float dz = local.z - o.Position.Z;
                if (dx * dx + dy * dy + dz * dz < 0.02f * 0.02f)
                {
                    // Предмет почти не сдвинули: считаем это просто выбором, в историю ничего не пишем.
                    _preview.Status = "Выбран: " + NameOf(id) + ".";
                    return;
                }

                // Высоту передаём для предметов на стене (картину можно перевесить выше или ниже), остальным она не нужна.
                float mountHeight = local.y - _preview.RoomElevation;
                Do(() => Report(_preview.Tools.MoveTo(id, new PlanPoint(local.x, local.z), mountHeight)));
            }
            finally
            {
                _preview.RebuildHolds = Math.Max(0, _preview.RebuildHolds - 1);
                _preview.RequestRebuild();   // при отказе элемент вернётся на прежнее место
            }
        }

        private void Do(Action action)
        {
            try
            {
                _preview.ApplyReality();
                action();
            }
            catch (Exception ex)
            {
                _preview.Status = "Ошибка: " + ex.Message;
                Debug.LogError(ex);
            }
        }

        private void Report(ToolResult r)
        {
            _preview.Status = r.Message + (r.Warning != null ? " Красный: " + r.Warning : "");
        }

        private string NameOf(string objectId)
        {
            Project p = _preview.Projects.Current;
            PlacedObject o = p == null ? null : p.Objects.Find(x => x.Id == objectId);
            if (o == null) return "?";
            CatalogItem item = _preview.Catalog != null ? _preview.Catalog.GetItem(o.CatalogId) : null;
            return item != null ? item.Name : o.CatalogId;
        }

        // ---------- XR-риг ----------

        private void OnViewRebuilt() { PlaceRig(); }

        // Проект создан или открыт заново (например, другая комната): ставим риг в новое помещение.
        private void OnProjectReopened()
        {
            _rigPlaced = false;
            PlaceRig();
        }

        private void PlaceRig()
        {
            if (_rigPlaced || xrOrigin == null) return;

            float minX, maxX, minZ, maxZ;
            if (!_preview.TryGetRoomBounds(out minX, out maxX, out minZ, out maxZ)) return;

            // Встаём в комнате внутри контура, лицом по оси Z.
            Project p = _preview.Projects.Current;
            PlanPoint c = PlanGeometry.InteriorPoint(p.Rooms[0].Boundary);
            Vector3 target = _preview.transform.TransformPoint(new Vector3(c.X, _preview.RoomElevation, c.Z));

            // Контроллер персонажа на риге иногда игнорирует прямую смену позиции, поэтому на миг выключаем его.
            CharacterController cc = xrOrigin.GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (wasEnabled) cc.enabled = false;
            xrOrigin.position = target;
            xrOrigin.rotation = _preview.transform.rotation;
            if (wasEnabled) cc.enabled = true;

            _rigPlaced = true;
        }
    }
}
