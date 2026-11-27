// Запрос к реальной геометрии (скан помещения). Реализует разработчик A поверх своего сервиса реальности
// (IRealityService.IsBlocked, раздел 11 ТЗ); B использует его при проверке коллизий (B-09, FR-105, FR-207).
// Все числа в метрах и градусах. Проверяемая область - прямоугольная коробка:
// центр (centerX, centerY, centerZ), размеры (sizeX - ширина, sizeY - высота, sizeZ - глубина),
// повёрнутая вокруг вертикальной оси на yawDegrees.

namespace XrInterior.ProjectData
{
    public interface IRealityCollisionChecker
    {
        /// <summary>true, если область пересекается с реальной стеной или крупным реальным объектом.</summary>
        bool IsBlocked(float centerX, float centerY, float centerZ,
                       float sizeX, float sizeY, float sizeZ,
                       float yawDegrees, out string reason);
    }
}
