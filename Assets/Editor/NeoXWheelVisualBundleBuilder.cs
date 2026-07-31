#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityPlanet.ModularAssembly.Editor
{
    /// <summary>
    /// Reconstructs the six supported base-wheel visuals from the verified
    /// NeoX face/bone semantics. The source OBJ remains the authority for
    /// vertices, normals and UVs; only its faces are partitioned.
    /// </summary>
    internal static class NeoXWheelVisualBundleBuilder
    {
        private const string RawPackageFolder =
            "g98_release_out_netease_94_nxpk";
        private const string GeneratedFolderName = "__WheelVisuals";
        private const string GeneratedAssetRoot =
            "Assets/__NeoXGeneratedTemp/__WheelVisuals";

        private static readonly WheelVisualSpec[] Specs =
        {
            new WheelVisualSpec(
                "wheel_basic_111",
                "block/common/wheel_basic_111.mesh",
                "addb490322a5a629038539b5976493ea540fa6ef0b53c2744c14cf30ca965b80",
                "c85e885a0171974d93328e059fc23f62ddcf72e0e14bc7baadbec3f7e4e7db0e",
                246,
                56,
                190,
                0,
                new Vector3(-0.112396f, -4.017775f, -0.000011f),
                Vector3.zero,
                new[] { new FaceRange(57, 246) },
                Array.Empty<FaceRange>()),
            new WheelVisualSpec(
                "wheel_m_222",
                "block/common/wheel_m_222.mesh",
                "9b8e51bc1643d2315741ec8dfe7d28da06a6ca43712aedf9e57e042ac0bc8478",
                "0b25be10bec622f5db0140719f293ac5bb828ef02eb09f67253e2f7e9d34b7cb",
                930,
                226,
                704,
                0,
                new Vector3(7.858856f, -11.683099f, -5.451517f),
                Vector3.zero,
                new[]
                {
                    new FaceRange(136, 777),
                    new FaceRange(813, 832),
                    new FaceRange(837, 878)
                },
                Array.Empty<FaceRange>()),
            new WheelVisualSpec(
                "speedwheel_small_l_322",
                "block/speed/speedwheel_small_l_322.mesh",
                "8695aa66ddc56bf275bf2abd5779182bcf44c0cb4ff51c5acc520b0cdbd37068",
                "084f7694da10c3a571730b851db6d9c77ca5ecdf28789339b1abd30c3a065c17",
                490,
                362,
                128,
                0,
                new Vector3(-2.845842f, -3.352069f, -5.406010f),
                Vector3.zero,
                new[]
                {
                    new FaceRange(322, 426),
                    new FaceRange(467, 489)
                },
                Array.Empty<FaceRange>()),
            new WheelVisualSpec(
                "speedwheel_small_r_322",
                "block/speed/speedwheel_small_r_322.mesh",
                "f8997de12efbed06b89ea43501819240afe26bc22a0d7565cad4d11643609a3b",
                "164be32b3f5533e6608db2384f3339d626b2dcf9a07f394d401b88bc25ca408f",
                490,
                362,
                128,
                0,
                new Vector3(2.882787f, -3.352069f, -5.406014f),
                Vector3.zero,
                new[]
                {
                    new FaceRange(322, 426),
                    new FaceRange(467, 489)
                },
                Array.Empty<FaceRange>()),
            new WheelVisualSpec(
                "speedwheel_large_l_522",
                "block/speed/speedwheel_large_l_522.mesh",
                "d33933236816ace37cecffe6ca711ba685d0b1c77dbdecae53aa6aec471558ee",
                "5d592d11f039727c8aef0cd4135467ed4984861437d372edf7cce7ad09ba5c9f",
                323,
                235,
                44,
                44,
                new Vector3(-3.897605f, -3.408370f, 4.254250f),
                new Vector3(-3.897605f, -3.408370f, -10.522564f),
                new[] { new FaceRange(234, 277) },
                new[] { new FaceRange(278, 321) }),
            new WheelVisualSpec(
                "speedwheel_large_r_522",
                "block/speed/speedwheel_large_r_522.mesh",
                "3da79e4ba4e78c28fc8317df41daec496768b22ee8734ba69c13f0dee41b0b91",
                "12a68f283ecbc017025ecf73cc89c53b032ef95dc27af60ee8f8808e87da2191",
                323,
                235,
                44,
                44,
                new Vector3(3.637492f, -3.408370f, 4.254250f),
                new Vector3(3.637492f, -3.408370f, -10.522564f),
                new[] { new FaceRange(234, 277) },
                new[] { new FaceRange(278, 321) })
        };

        public static void BuildSemanticWheelPrefabs(
            string stagingRoot,
            string temporaryAbsoluteRoot)
        {
            if (string.IsNullOrWhiteSpace(stagingRoot) ||
                !Directory.Exists(stagingRoot))
            {
                throw new DirectoryNotFoundException(
                    "NeoX wheel semantic build requires the conversion staging root: " +
                    stagingRoot);
            }
            if (string.IsNullOrWhiteSpace(temporaryAbsoluteRoot) ||
                !Directory.Exists(temporaryAbsoluteRoot))
            {
                throw new DirectoryNotFoundException(
                    "NeoX wheel semantic build requires the temporary bundle root: " +
                    temporaryAbsoluteRoot);
            }

            string rawRoot = Path.Combine(
                NeoXExternalPaths.RawRoot,
                RawPackageFolder);
            if (!Directory.Exists(rawRoot))
            {
                throw new DirectoryNotFoundException(
                    "The authoritative unpacked NeoX NPK root is missing: " +
                    rawRoot);
            }

            string generatedAbsoluteRoot = Path.Combine(
                temporaryAbsoluteRoot,
                GeneratedFolderName);
            Directory.CreateDirectory(generatedAbsoluteRoot);

            foreach (WheelVisualSpec spec in Specs)
            {
                ValidateAuthoritativeInputs(
                    spec,
                    rawRoot,
                    stagingRoot,
                    temporaryAbsoluteRoot);
                SplitTemporaryObj(
                    spec,
                    temporaryAbsoluteRoot,
                    generatedAbsoluteRoot);
            }

            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            foreach (WheelVisualSpec spec in Specs)
            {
                CreateSemanticPrefab(spec);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static bool TryGetSemanticPrefabAssetPath(
            string originalAssetAddress,
            out string prefabAssetPath)
        {
            string normalized = NormalizeRelativePath(originalAssetAddress);
            WheelVisualSpec spec = Specs.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.ObjRelativePath,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
            if (spec == null)
            {
                // wheel_l_422 is deliberately not part of the semantic set.
                prefabAssetPath = null;
                return false;
            }

            prefabAssetPath = PrefabAssetPath(spec);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath) == null)
            {
                throw new InvalidOperationException(
                    $"Semantic wheel prefab was not generated for address " +
                    $"'{originalAssetAddress}': {prefabAssetPath}");
            }
            return true;
        }

        private static void ValidateAuthoritativeInputs(
            WheelVisualSpec spec,
            string rawRoot,
            string stagingRoot,
            string temporaryAbsoluteRoot)
        {
            string rawPath = CombineRelative(
                rawRoot,
                spec.RawMeshRelativePath);
            string stagingObjPath = CombineRelative(
                stagingRoot,
                spec.ObjRelativePath);
            string temporaryObjPath = CombineRelative(
                temporaryAbsoluteRoot,
                spec.ObjRelativePath);

            ValidateSha256(
                rawPath,
                spec.RawMeshSha256,
                spec.NeoXId + " authoritative raw .mesh");
            ValidateSha256(
                stagingObjPath,
                spec.StagingObjSha256,
                spec.NeoXId + " conversion staging OBJ");
            ValidateSha256(
                temporaryObjPath,
                spec.StagingObjSha256,
                spec.NeoXId + " copied temporary OBJ");
        }

        private static void SplitTemporaryObj(
            WheelVisualSpec spec,
            string temporaryAbsoluteRoot,
            string generatedAbsoluteRoot)
        {
            string sourcePath = CombineRelative(
                temporaryAbsoluteRoot,
                spec.ObjRelativePath);
            string[] lines = File.ReadAllLines(sourcePath);
            FaceRole[] roles = ClassifyFaces(spec, lines);

            string outputDirectory = Path.Combine(
                generatedAbsoluteRoot,
                spec.NeoXId);
            Directory.CreateDirectory(outputDirectory);
            WritePartObj(
                lines,
                roles,
                FaceRole.Carrier,
                "Carrier",
                Path.Combine(outputDirectory, "Carrier.obj"),
                spec.CarrierFaceCount);
            WritePartObj(
                lines,
                roles,
                FaceRole.Wheel,
                "wheel",
                Path.Combine(outputDirectory, "wheel.obj"),
                spec.WheelFaceCount);
            if (spec.Wheel2FaceCount > 0)
            {
                WritePartObj(
                    lines,
                    roles,
                    FaceRole.Wheel2,
                    "wheel2",
                    Path.Combine(outputDirectory, "wheel2.obj"),
                    spec.Wheel2FaceCount);
            }
        }

        private static FaceRole[] ClassifyFaces(
            WheelVisualSpec spec,
            IReadOnlyList<string> lines)
        {
            int totalFaces = lines.Count(IsFaceLine);
            if (totalFaces != spec.TotalFaceCount)
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId} OBJ face count changed: expected " +
                    $"{spec.TotalFaceCount}, found {totalFaces}. Refusing to " +
                    "apply stale wheel/carrier semantics.");
            }

            FaceRole[] roles = new FaceRole[totalFaces];
            int carrierCount = 0;
            int wheelCount = 0;
            int wheel2Count = 0;
            for (int index = 0; index < totalFaces; index++)
            {
                int oneBasedFace = index + 1;
                bool wheel = IsInAnyRange(
                    oneBasedFace,
                    spec.WheelFaceRanges);
                bool wheel2 = IsInAnyRange(
                    oneBasedFace,
                    spec.Wheel2FaceRanges);
                if (wheel && wheel2)
                {
                    throw new InvalidDataException(
                        $"{spec.NeoXId} face {oneBasedFace} is assigned to " +
                        "both wheel and wheel2.");
                }

                FaceRole role = wheel
                    ? FaceRole.Wheel
                    : wheel2
                        ? FaceRole.Wheel2
                        : FaceRole.Carrier;
                roles[index] = role;
                switch (role)
                {
                    case FaceRole.Carrier:
                        carrierCount++;
                        break;
                    case FaceRole.Wheel:
                        wheelCount++;
                        break;
                    case FaceRole.Wheel2:
                        wheel2Count++;
                        break;
                }
            }

            ValidateRoleCount(
                spec,
                "Carrier",
                spec.CarrierFaceCount,
                carrierCount);
            ValidateRoleCount(
                spec,
                "wheel",
                spec.WheelFaceCount,
                wheelCount);
            ValidateRoleCount(
                spec,
                "wheel2",
                spec.Wheel2FaceCount,
                wheel2Count);
            if (carrierCount + wheelCount + wheel2Count != totalFaces)
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId} semantic face partition is incomplete.");
            }
            return roles;
        }

        private static void WritePartObj(
            IReadOnlyList<string> sourceLines,
            IReadOnlyList<FaceRole> roles,
            FaceRole selectedRole,
            string objectName,
            string outputPath,
            int expectedFaceCount)
        {
            List<string> output = new List<string>(sourceLines.Count + 2)
            {
                "# Generated from a SHA256-pinned NeoX conversion OBJ.",
                "o " + objectName
            };
            int faceIndex = 0;
            foreach (string line in sourceLines)
            {
                if (IsObjectLine(line))
                {
                    continue;
                }
                if (!IsFaceLine(line))
                {
                    output.Add(line);
                    continue;
                }
                if (roles[faceIndex] == selectedRole)
                {
                    output.Add(line);
                }
                faceIndex++;
            }

            if (faceIndex != roles.Count)
            {
                throw new InvalidDataException(
                    $"OBJ face traversal mismatch while writing {outputPath}.");
            }
            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath) ??
                throw new InvalidOperationException(
                    "Generated OBJ path has no directory: " + outputPath));
            File.WriteAllLines(
                outputPath,
                output,
                new UTF8Encoding(false));

            int generatedFaceCount =
                File.ReadLines(outputPath).Count(IsFaceLine);
            if (generatedFaceCount != expectedFaceCount)
            {
                throw new InvalidDataException(
                    $"Generated part '{outputPath}' has {generatedFaceCount} " +
                    $"faces; expected {expectedFaceCount}.");
            }
        }

        private static void CreateSemanticPrefab(WheelVisualSpec spec)
        {
            ImportedPart carrier = LoadImportedPart(
                PartAssetPath(spec, "Carrier"),
                spec.CarrierFaceCount);
            ImportedPart wheel = LoadImportedPart(
                PartAssetPath(spec, "wheel"),
                spec.WheelFaceCount);
            ImportedPart wheel2 = spec.Wheel2FaceCount > 0
                ? LoadImportedPart(
                    PartAssetPath(spec, "wheel2"),
                    spec.Wheel2FaceCount)
                : null;

            GameObject root = new GameObject(spec.NeoXId);
            try
            {
                CreateMeshObject(
                    root.transform,
                    "Carrier",
                    carrier,
                    Vector3.zero);
                CreateWheelObject(
                    root.transform,
                    "wheel",
                    wheel,
                    spec.WheelPivot);
                if (wheel2 != null)
                {
                    CreateWheelObject(
                        root.transform,
                        "wheel2",
                        wheel2,
                        spec.Wheel2Pivot);
                }

                if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                {
                    throw new InvalidOperationException(
                        spec.NeoXId +
                        " semantic visual unexpectedly contains a Collider.");
                }

                string prefabPath = PrefabAssetPath(spec);
                bool saved;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    prefabPath,
                    out saved);
                if (!saved || prefab == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save semantic wheel prefab: " + prefabPath);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            ValidatePrefab(spec);
        }

        private static ImportedPart LoadImportedPart(
            string assetPath,
            int expectedFaceCount)
        {
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Generated wheel part OBJ did not import as a GameObject: " +
                    assetPath);
            }

            MeshFilter[] filters = source
                .GetComponentsInChildren<MeshFilter>(true)
                .Where(item => item != null && item.sharedMesh != null)
                .ToArray();
            if (filters.Length != 1)
            {
                throw new InvalidDataException(
                    $"Generated wheel part '{assetPath}' imported with " +
                    $"{filters.Length} meshes; exactly one is required.");
            }
            MeshFilter filter = filters[0];
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                throw new InvalidDataException(
                    "Generated wheel part must use a MeshRenderer: " +
                    assetPath);
            }
            if (!HasIdentityPoseBetween(filter.transform, source.transform))
            {
                throw new InvalidDataException(
                    "Generated wheel part importer introduced a non-identity " +
                    "node pose, so its verified pivot would no longer be exact: " +
                    assetPath);
            }

            int faceCount = GetTriangleCount(filter.sharedMesh);
            if (faceCount != expectedFaceCount)
            {
                throw new InvalidDataException(
                    $"Imported wheel part '{assetPath}' has {faceCount} faces; " +
                    $"expected {expectedFaceCount}.");
            }
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                throw new InvalidDataException(
                    "Generated wheel part has no material slot for the runtime " +
                    "NeoX material binding: " + assetPath);
            }

            return new ImportedPart(
                filter.sharedMesh,
                materials,
                expectedFaceCount);
        }

        private static void CreateWheelObject(
            Transform parent,
            string name,
            ImportedPart part,
            Vector3 pivot)
        {
            GameObject pivotObject = new GameObject(name);
            Transform pivotTransform = pivotObject.transform;
            pivotTransform.SetParent(parent, false);
            pivotTransform.localPosition = pivot;
            pivotTransform.localRotation = Quaternion.identity;
            pivotTransform.localScale = Vector3.one;

            CreateMeshObject(
                pivotTransform,
                "Geometry",
                part,
                -pivot);
        }

        private static GameObject CreateMeshObject(
            Transform parent,
            string name,
            ImportedPart part,
            Vector3 localPosition)
        {
            GameObject result = new GameObject(
                name,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            Transform transform = result.transform;
            transform.SetParent(parent, false);
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            result.GetComponent<MeshFilter>().sharedMesh = part.Mesh;
            result.GetComponent<MeshRenderer>().sharedMaterials =
                part.Materials;
            return result;
        }

        private static void ValidatePrefab(WheelVisualSpec spec)
        {
            string prefabPath = PrefabAssetPath(spec);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Saved semantic wheel prefab cannot be loaded: " +
                    prefabPath);
            }
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
            {
                throw new InvalidDataException(
                    spec.NeoXId +
                    " semantic prefab must contain only collider-free static visuals.");
            }

            int expectedRenderers = spec.Wheel2FaceCount > 0 ? 3 : 2;
            MeshRenderer[] renderers =
                prefab.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length != expectedRenderers)
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId} semantic prefab has {renderers.Length} " +
                    $"MeshRenderers; expected {expectedRenderers}.");
            }

            ValidateCarrier(
                spec,
                prefab.transform.Find("Carrier"));
            ValidateWheel(
                spec,
                prefab.transform.Find("wheel"),
                spec.WheelPivot,
                spec.WheelFaceCount);
            if (spec.Wheel2FaceCount > 0)
            {
                ValidateWheel(
                    spec,
                    prefab.transform.Find("wheel2"),
                    spec.Wheel2Pivot,
                    spec.Wheel2FaceCount);
            }
        }

        private static void ValidateCarrier(
            WheelVisualSpec spec,
            Transform carrier)
        {
            if (carrier == null ||
                !Approximately(carrier.localPosition, Vector3.zero) ||
                !Approximately(carrier.localRotation, Quaternion.identity) ||
                !Approximately(carrier.localScale, Vector3.one))
            {
                throw new InvalidDataException(
                    spec.NeoXId +
                    " Carrier must render its mesh at the prefab identity.");
            }
            ValidateMeshObject(
                spec,
                carrier,
                spec.CarrierFaceCount,
                "Carrier");
        }

        private static void ValidateWheel(
            WheelVisualSpec spec,
            Transform pivot,
            Vector3 expectedPivot,
            int expectedFaceCount)
        {
            if (pivot == null ||
                !Approximately(pivot.localPosition, expectedPivot) ||
                !Approximately(pivot.localRotation, Quaternion.identity) ||
                !Approximately(pivot.localScale, Vector3.one))
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId} wheel pivot is not the verified bone " +
                    $"translation {expectedPivot}.");
            }

            Transform geometry = pivot.Find("Geometry");
            if (geometry == null ||
                !Approximately(geometry.localPosition, -expectedPivot) ||
                !Approximately(geometry.localRotation, Quaternion.identity) ||
                !Approximately(geometry.localScale, Vector3.one) ||
                !Approximately(
                    pivot.localPosition + geometry.localPosition,
                    Vector3.zero))
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId}/{pivot.name} geometry does not cancel its " +
                    "pivot translation, so the initial appearance would move.");
            }
            ValidateMeshObject(
                spec,
                geometry,
                expectedFaceCount,
                pivot.name);
        }

        private static void ValidateMeshObject(
            WheelVisualSpec spec,
            Transform target,
            int expectedFaceCount,
            string role)
        {
            MeshFilter filter = target.GetComponent<MeshFilter>();
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            if (filter == null ||
                filter.sharedMesh == null ||
                renderer == null)
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId}/{role} is not an ordinary MeshRenderer.");
            }
            int actualFaceCount = GetTriangleCount(filter.sharedMesh);
            if (actualFaceCount != expectedFaceCount)
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId}/{role} prefab mesh has {actualFaceCount} " +
                    $"faces; expected {expectedFaceCount}.");
            }
            if (renderer.sharedMaterials == null ||
                renderer.sharedMaterials.Length == 0)
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId}/{role} lost the material slot required by " +
                    "the runtime material flow.");
            }
        }

        private static int GetTriangleCount(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            ulong indexCount = 0;
            for (int index = 0; index < mesh.subMeshCount; index++)
            {
                if (mesh.GetTopology(index) != MeshTopology.Triangles)
                {
                    throw new InvalidDataException(
                        $"Mesh '{mesh.name}' contains a non-triangle submesh.");
                }
                indexCount += mesh.GetIndexCount(index);
            }
            if (indexCount % 3UL != 0UL ||
                indexCount / 3UL > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Mesh '{mesh.name}' has an invalid triangle index count.");
            }
            return (int)(indexCount / 3UL);
        }

        private static bool HasIdentityPoseBetween(
            Transform child,
            Transform root)
        {
            Transform current = child;
            while (current != null && current != root)
            {
                if (!Approximately(current.localPosition, Vector3.zero) ||
                    !Approximately(current.localRotation, Quaternion.identity) ||
                    !Approximately(current.localScale, Vector3.one))
                {
                    return false;
                }
                current = current.parent;
            }
            return current == root;
        }

        private static void ValidateSha256(
            string path,
            string expected,
            string description)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    description + " is missing.",
                    path);
            }
            string actual;
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                actual = string.Concat(
                    sha.ComputeHash(stream)
                        .Select(value => value.ToString("x2")));
            }
            if (!string.Equals(
                    actual,
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{description} SHA256 changed.\n" +
                    $"Expected: {expected}\n" +
                    $"Actual:   {actual}\n" +
                    $"Path: {path}");
            }
        }

        private static void ValidateRoleCount(
            WheelVisualSpec spec,
            string role,
            int expected,
            int actual)
        {
            if (actual != expected)
            {
                throw new InvalidDataException(
                    $"{spec.NeoXId} {role} face count changed: expected " +
                    $"{expected}, found {actual}.");
            }
        }

        private static bool IsInAnyRange(
            int oneBasedFace,
            IEnumerable<FaceRange> ranges)
        {
            return ranges.Any(range =>
                oneBasedFace >= range.First &&
                oneBasedFace <= range.Last);
        }

        private static bool IsFaceLine(string line)
        {
            string trimmed = (line ?? string.Empty).TrimStart();
            return trimmed.StartsWith("f ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("f\t", StringComparison.Ordinal);
        }

        private static bool IsObjectLine(string line)
        {
            string trimmed = (line ?? string.Empty).TrimStart();
            return trimmed.StartsWith("o ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("o\t", StringComparison.Ordinal);
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.00000001f;
        }

        private static bool Approximately(
            Quaternion left,
            Quaternion right)
        {
            return Quaternion.Angle(left, right) <= 0.001f;
        }

        private static string PartAssetPath(
            WheelVisualSpec spec,
            string role)
        {
            return GeneratedAssetRoot + "/" + spec.NeoXId + "/" +
                   role + ".obj";
        }

        private static string PrefabAssetPath(WheelVisualSpec spec)
        {
            return GeneratedAssetRoot + "/" + spec.NeoXId + "/" +
                   spec.NeoXId + ".prefab";
        }

        private static string CombineRelative(
            string root,
            string relative)
        {
            return Path.Combine(
                root,
                NormalizeRelativePath(relative)
                    .Replace('/', Path.DirectorySeparatorChar));
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');
        }

        private enum FaceRole
        {
            Carrier,
            Wheel,
            Wheel2
        }

        private sealed class ImportedPart
        {
            public readonly Mesh Mesh;
            public readonly Material[] Materials;
            public readonly int FaceCount;

            public ImportedPart(
                Mesh mesh,
                Material[] materials,
                int faceCount)
            {
                Mesh = mesh;
                Materials = materials;
                FaceCount = faceCount;
            }
        }

        private sealed class WheelVisualSpec
        {
            public readonly string NeoXId;
            public readonly string RawMeshRelativePath;
            public readonly string ObjRelativePath;
            public readonly string RawMeshSha256;
            public readonly string StagingObjSha256;
            public readonly int TotalFaceCount;
            public readonly int CarrierFaceCount;
            public readonly int WheelFaceCount;
            public readonly int Wheel2FaceCount;
            public readonly Vector3 WheelPivot;
            public readonly Vector3 Wheel2Pivot;
            public readonly FaceRange[] WheelFaceRanges;
            public readonly FaceRange[] Wheel2FaceRanges;

            public WheelVisualSpec(
                string neoXId,
                string rawMeshRelativePath,
                string rawMeshSha256,
                string stagingObjSha256,
                int totalFaceCount,
                int carrierFaceCount,
                int wheelFaceCount,
                int wheel2FaceCount,
                Vector3 wheelPivot,
                Vector3 wheel2Pivot,
                FaceRange[] wheelFaceRanges,
                FaceRange[] wheel2FaceRanges)
            {
                NeoXId = neoXId;
                RawMeshRelativePath =
                    NormalizeRelativePath(rawMeshRelativePath);
                ObjRelativePath = RawMeshRelativePath + ".obj";
                RawMeshSha256 = rawMeshSha256;
                StagingObjSha256 = stagingObjSha256;
                TotalFaceCount = totalFaceCount;
                CarrierFaceCount = carrierFaceCount;
                WheelFaceCount = wheelFaceCount;
                Wheel2FaceCount = wheel2FaceCount;
                WheelPivot = wheelPivot;
                Wheel2Pivot = wheel2Pivot;
                WheelFaceRanges =
                    wheelFaceRanges ?? Array.Empty<FaceRange>();
                Wheel2FaceRanges =
                    wheel2FaceRanges ?? Array.Empty<FaceRange>();
            }
        }

        private readonly struct FaceRange
        {
            public readonly int First;
            public readonly int Last;

            public FaceRange(int first, int last)
            {
                if (first <= 0 || last < first)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(first),
                        $"Invalid OBJ face range {first}-{last}.");
                }
                First = first;
                Last = last;
            }
        }
    }
}
#endif
