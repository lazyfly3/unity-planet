using System;
using System.IO;
using NUnit.Framework;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.Tests.Editor
{
    public sealed class ModularContentCatalogTests
    {
        [Test]
        public void CatalogSearch_MatchesChineseAndNeoXId()
        {
            const string json =
                "{\"formatVersion\":2,\"modelCount\":2,\"moduleSourceCount\":1,\"propSourceCount\":1," +
                "\"items\":[" +
                "{\"sourceId\":\"block:machinegun_111\",\"neoXId\":\"machinegun_111\",\"chineseName\":\"机枪 111\",\"contentKind\":\"module\",\"ownership\":\"base\",\"category\":\"Weapon\",\"behavior\":\"KineticRapid\"}," +
                "{\"sourceId\":\"scene:rock\",\"neoXId\":\"rock\",\"chineseName\":\"岩石\",\"contentKind\":\"prop\",\"ownership\":\"base\",\"category\":\"Decoration\",\"behavior\":\"Decoration\"}]}";

            ModularContentCatalog catalog = ModularContentCatalog.FromJson(json);

            Assert.AreEqual(1, catalog.Search("机枪").Count);
            Assert.AreEqual(1, catalog.Search("machinegun").Count);
            Assert.AreEqual(0, catalog.Search("rock").Count);
            Assert.AreEqual(1, catalog.Search("rock", modulesOnly: false).Count);
        }

        [Test]
        public void LayoutStore_RoundTripsAndClampsToLimit()
        {
            string root = Path.Combine(Path.GetTempPath(), "UnityPlanet-LabLayout-" + Guid.NewGuid().ToString("N"));
            try
            {
                LabPropPose[] props = new LabPropPose[LabLayoutStore.PropLimit + 20];
                for (int index = 0; index < props.Length; index++)
                {
                    props[index] = new LabPropPose { sourceId = "prop-" + index };
                }

                Assert.IsTrue(LabLayoutStore.Save(root, new LabLayoutData { props = props }, out string saveError), saveError);
                Assert.IsTrue(LabLayoutStore.TryLoad(root, out LabLayoutData loaded, out string loadError), loadError);
                Assert.AreEqual(LabLayoutStore.PropLimit, loaded.props.Length);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
