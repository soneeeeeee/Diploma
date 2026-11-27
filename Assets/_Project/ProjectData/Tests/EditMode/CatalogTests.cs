using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    public class CatalogTests
    {
        private static string CatalogPath =>
            Path.Combine(Application.dataPath, "_Project/ProjectData/Samples/catalog_sample.json");

        private static CatalogService Load() { return CatalogService.FromFile(CatalogPath); }

        [Test]
        public void StarterCatalog_LoadsAndMeetsRequirements()
        {
            CatalogData data = CatalogSerializer.FromJson(File.ReadAllText(CatalogPath));
            var errors = CatalogValidator.Validate(data);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            Assert.GreaterOrEqual(data.Items.Count, 20);       // FR-101
            Assert.GreaterOrEqual(data.Categories.Count, 3);   // не менее трёх категорий
            Assert.GreaterOrEqual(data.Materials.Count, 15);   // не менее 15 материалов (раздел 3.2 ТЗ)
        }

        [Test]
        public void Search_ByTextAndCategory()
        {
            CatalogService catalog = Load();

            Assert.AreEqual(20, catalog.Search().Count);
            Assert.AreEqual(4, catalog.Search(null, "seating").Count);

            var byText = catalog.Search("стул");
            Assert.AreEqual(2, byText.Count);
            Assert.IsTrue(byText.All(i => i.CategoryId == "seating"));

            // регистр не важен, поиск и по id
            Assert.AreEqual(1, catalog.Search("ШКАФ ПЛАТЯНОЙ").Count);
            Assert.AreEqual(1, catalog.Search("wardrobe").Count);

            // сочетание текста и категории
            Assert.AreEqual(0, catalog.Search("стул", "tables").Count);

            // ничего не найдено - пустой список, не null
            var none = catalog.Search("такого нет");
            Assert.IsNotNull(none);
            Assert.AreEqual(0, none.Count);
        }

        [Test]
        public void GetItemAndMaterial_FoundAndMissing()
        {
            CatalogService catalog = Load();

            CatalogItem sofa = catalog.GetItem("sofa_3seat_01");
            Assert.IsNotNull(sofa);
            Assert.AreEqual(2.0f, sofa.Size.X, 0.0001f);
            Assert.IsTrue(sofa.AlignToWall);

            Assert.IsNull(catalog.GetItem("net_takogo"));
            Assert.IsNull(catalog.GetItem(null));

            Assert.IsNotNull(catalog.GetMaterial("mat_wood_oak_01"));
            Assert.IsNull(catalog.GetMaterial("net_takogo"));
        }

        [Test]
        public void GetMaterials_FilteredBySurface()
        {
            CatalogService catalog = Load();

            var floor = catalog.GetMaterials(MaterialSurface.Floor);
            Assert.IsNotEmpty(floor);
            Assert.IsTrue(floor.All(m => (m.Surfaces & MaterialSurface.Floor) != MaterialSurface.None));
            Assert.IsTrue(floor.Any(m => m.Id == "mat_wood_oak_01"));
            Assert.IsFalse(floor.Any(m => m.Id == "mat_paint_blue_01"));

            var ceiling = catalog.GetMaterials(MaterialSurface.Ceiling);
            Assert.IsTrue(ceiling.Any(m => m.Id == "mat_ceiling_white_01"));

            Assert.AreEqual(19, catalog.GetMaterials().Count);
        }

        [Test]
        public void Validator_FindsProblems()
        {
            CatalogData data = CatalogSerializer.FromJson(File.ReadAllText(CatalogPath));

            data.Items[1].Id = data.Items[0].Id;                    // дубль id
            data.Items[2].CategoryId = "net_takoy";                 // неизвестная категория
            data.Items[3].AllowedMaterialIds.Add("net_takogo");     // неизвестный материал
            data.Items[4].Size = new Vec3(1f, 0f, 1f);              // нулевая высота

            var errors = CatalogValidator.Validate(data);
            Assert.IsTrue(errors.Any(e => e.Contains("Повторяющийся id предмета")));
            Assert.IsTrue(errors.Any(e => e.Contains("net_takoy")));
            Assert.IsTrue(errors.Any(e => e.Contains("net_takogo")));
            Assert.IsTrue(errors.Any(e => e.Contains("размеры должны быть больше нуля")));

            Assert.Throws<System.ArgumentException>(() => new CatalogService(data));
        }

        [Test]
        public void BrokenFiles_GiveFormatException()
        {
            Assert.Throws<CatalogFormatException>(() => CatalogSerializer.FromJson("{ это не json"));
            Assert.Throws<CatalogFormatException>(() => CatalogSerializer.FromJson(""));

            const string badSurface = "{ \"materials\": [ { \"id\": \"m\", \"name\": \"М\", \"surfaces\": [ \"potolok\" ] } ] }";
            Assert.Throws<CatalogFormatException>(() => CatalogSerializer.FromJson(badSurface));
        }
    }
}
