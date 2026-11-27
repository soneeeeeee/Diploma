// Шаблоны помещений: значения по умолчанию, проверка параметров, построение контура.
// Контур задан по ОСЯМ стен: толщина стены распределяется поровну по обе стороны от линии контура.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public static class RoomTemplates
    {
        public const float MinSize = 0.8f;
        public const float MaxSize = 50f;
        public const float MinHeight = 2.0f;
        public const float MaxHeight = 6.0f;
        public const float MaxThickness = 1.0f;
        /// <summary>Минимальная ширина «плеча» Г-образной комнаты.</summary>
        public const float MinLeg = 0.8f;

        public static RoomTemplateDefaults GetDefaults(RoomTemplate template)
        {
            switch (template)
            {
                case RoomTemplate.Rectangular:
                    return new RoomTemplateDefaults(2.7f, new Dictionary<string, float>
                    {
                        { TemplateParam.Width, 4f }, { TemplateParam.Depth, 3f }
                    });
                case RoomTemplate.LShaped:
                    return new RoomTemplateDefaults(2.7f, new Dictionary<string, float>
                    {
                        { TemplateParam.Width, 6f }, { TemplateParam.Depth, 5f },
                        { TemplateParam.CutWidth, 2.5f }, { TemplateParam.CutDepth, 2f }
                    });
                case RoomTemplate.Corridor:
                    return new RoomTemplateDefaults(2.7f, new Dictionary<string, float>
                    {
                        { TemplateParam.Length, 8f }, { TemplateParam.Width, 1.8f }
                    });
                case RoomTemplate.Auditorium:
                    return new RoomTemplateDefaults(3.2f, new Dictionary<string, float>
                    {
                        { TemplateParam.Width, 10f }, { TemplateParam.Depth, 7.5f }
                    });
                default:
                    throw new ArgumentException("Для шаблона '" + template + "' нет значений по умолчанию.");
            }
        }

        /// <summary>Параметры заявки поверх значений по умолчанию (все ключи заполнены).</summary>
        public static Dictionary<string, float> MergeParams(RoomSpec spec)
        {
            Dictionary<string, float> merged = GetDefaults(spec.Template).Params;
            if (spec.Params != null)
            {
                foreach (var kv in spec.Params)
                    if (merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;
            }
            return merged;
        }

        /// <summary>Список ошибок заявки; пустой список - заявка корректна.</summary>
        public static List<string> Validate(RoomSpec spec)
        {
            var errors = new List<string>();
            if (spec == null)
            {
                errors.Add("Не задана заявка на помещение.");
                return errors;
            }
            if (spec.Template == RoomTemplate.Custom)
            {
                errors.Add("Шаблон Custom не создаётся автоматически.");
                return errors;
            }

            RoomTemplateDefaults defaults = GetDefaults(spec.Template);

            if (spec.Params != null)
            {
                foreach (string key in spec.Params.Keys)
                    if (!defaults.Params.ContainsKey(key))
                        errors.Add("Неизвестный параметр '" + key + "' для шаблона " + spec.Template + ".");
            }

            Dictionary<string, float> p = MergeParams(spec);
            bool rangesOk = true;
            foreach (var kv in p)
            {
                if (float.IsNaN(kv.Value) || kv.Value < MinSize || kv.Value > MaxSize)
                {
                    errors.Add("Параметр '" + kv.Key + "' должен быть от " + MinSize + " до " + MaxSize + " м.");
                    rangesOk = false;
                }
            }

            if (rangesOk)
            {
                if (spec.Template == RoomTemplate.LShaped)
                {
                    if (p[TemplateParam.CutWidth] > p[TemplateParam.Width] - MinLeg)
                        errors.Add("Вырез слишком широкий: должно остаться не менее " + MinLeg + " м ширины.");
                    if (p[TemplateParam.CutDepth] > p[TemplateParam.Depth] - MinLeg)
                        errors.Add("Вырез слишком глубокий: должно остаться не менее " + MinLeg + " м глубины.");
                }
                if (spec.Template == RoomTemplate.Corridor && p[TemplateParam.Length] < p[TemplateParam.Width])
                    errors.Add("Длина коридора должна быть не меньше его ширины.");
            }

            float height = spec.Height ?? defaults.Height;
            if (float.IsNaN(height) || height < MinHeight || height > MaxHeight)
                errors.Add("Высота помещения должна быть от " + MinHeight + " до " + MaxHeight + " м.");

            if (float.IsNaN(spec.WallThickness) || spec.WallThickness <= 0f || spec.WallThickness > MaxThickness)
                errors.Add("Толщина стены должна быть больше 0 и не более " + MaxThickness + " м.");

            return errors;
        }

        /// <summary>
        /// Контур помещения (вид сверху, начало в (0,0)). Заявка должна быть уже проверена через Validate.
        /// </summary>
        public static List<PlanPoint> BuildBoundary(RoomSpec spec)
        {
            Dictionary<string, float> p = MergeParams(spec);
            var b = new List<PlanPoint>();

            switch (spec.Template)
            {
                case RoomTemplate.Rectangular:
                case RoomTemplate.Auditorium:
                    AddRectangle(b, p[TemplateParam.Width], p[TemplateParam.Depth]);
                    break;

                case RoomTemplate.Corridor:
                    AddRectangle(b, p[TemplateParam.Length], p[TemplateParam.Width]);
                    break;

                case RoomTemplate.LShaped:
                {
                    float w = p[TemplateParam.Width];
                    float d = p[TemplateParam.Depth];
                    float cw = p[TemplateParam.CutWidth];
                    float cd = p[TemplateParam.CutDepth];
                    // Прямоугольник w x d с вырезанным углом cw x cd (правый дальний угол).
                    b.Add(new PlanPoint(0f, 0f));
                    b.Add(new PlanPoint(w, 0f));
                    b.Add(new PlanPoint(w, d - cd));
                    b.Add(new PlanPoint(w - cw, d - cd));
                    b.Add(new PlanPoint(w - cw, d));
                    b.Add(new PlanPoint(0f, d));
                    break;
                }

                default:
                    throw new ArgumentException("Нельзя построить контур для шаблона '" + spec.Template + "'.");
            }
            return b;
        }

        private static void AddRectangle(List<PlanPoint> b, float sizeX, float sizeZ)
        {
            b.Add(new PlanPoint(0f, 0f));
            b.Add(new PlanPoint(sizeX, 0f));
            b.Add(new PlanPoint(sizeX, sizeZ));
            b.Add(new PlanPoint(0f, sizeZ));
        }
    }
}
