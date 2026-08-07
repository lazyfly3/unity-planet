using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPlanet.IcePlanet;

namespace UnityPlanet.IcePlanet.Editor
{
    public static class IcePlanetCombatContentBuilder
    {
        public const string PalettePath =
            "Assets/Resources/IcePlanet/IcePlanetCombatPalette.asset";

        [MenuItem("Tools/Unity Planet/Ice Planet/Rebuild Combat Palette")]
        public static void BuildPalette()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "IcePlanet");
            IcePlanetCombatPalette palette =
                AssetDatabase.LoadAssetAtPath<IcePlanetCombatPalette>(
                    PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<
                    IcePlanetCombatPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }

            var missing = new List<string>();
            SerializedObject serialized = new SerializedObject(palette);
            Assign(serialized, "boundaryCliffs", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Ice Cliffs/WinterCliff00.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Ice Cliffs/WinterCliff003.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Ice Cliffs/WinterCliff005.prefab");
            Assign(serialized, "coverRocks", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Rocks/IceRock001.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Rocks/IceRock002.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Rocks/IceRock003.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Rocks/IceRockBlock00.prefab");
            Assign(serialized, "iceCrystals", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Ice Crystals/IceCrystal000.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Ice Crystals/IceCrystal004.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Ice Crystals/IceCrystal009.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Ice Crystals/IceCrystalBlock00.prefab");
            Assign(serialized, "ruins", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Deco Elements/Ruins/AncientArcBig00_Snow.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Deco Elements/Ruins/AncientColumn00_Snow.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Deco Elements/Ruins/AncientRuinsDeco00.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Deco Elements/Ruins/Arcs/AncientArc00.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Deco Elements/Ruins/Walls/AncientWall00.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Deco Elements/Ruins/Walls/AncientWall004.prefab");
            Assign(serialized, "walls", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Wall/WinterWall001b.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Wall/WinterWall002.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Wall/WinterBlock00.prefab");
            Assign(serialized, "towers", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Wall/WinterTower00.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Wall/WinterTower01.prefab");
            Assign(serialized, "bridges", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Bridge/WinterBridge00.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Snow Bridge/WinterBridgeLarge00.prefab");
            Assign(serialized, "lights", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/Lighting Props/StreetLight00_Snow.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/Lighting Props/StreetLight01_Snow.prefab");
            Assign(serialized, "snowFx", missing,
                "Assets/_DLNK/Winter Lands/[Prefabs]/FXs/Snow Low.prefab",
                "Assets/_DLNK/Winter Lands/[Prefabs]/FXs/Snow Storm Medium.prefab");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "Ice Planet palette is incomplete:\n"
                    + string.Join("\n", missing));
            }
            Debug.Log(
                "[IcePlanet] Combat palette rebuilt at " + PalettePath
                + " with the curated Winter Lands subset.",
                palette);
        }

        static void Assign(
            SerializedObject serialized,
            string propertyName,
            List<string> missing,
            params string[] paths)
        {
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(propertyName);
            property.arraySize = paths.Length;
            for (int index = 0; index < paths.Length; index++)
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);
                property.GetArrayElementAtIndex(index).objectReferenceValue =
                    prefab;
                if (prefab == null)
                    missing.Add(paths[index]);
            }
        }

        static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
