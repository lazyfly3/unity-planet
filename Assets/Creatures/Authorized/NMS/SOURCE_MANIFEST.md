# Authorized No Man's Sky Study Assets

These assets are isolated from the project's original creature library.

- Authorization basis: the project owner stated that a producer with authority over the source assets granted permission for any project operation.
- Source installation: `D:\SteamLibrary\steamapps\common\No Man's Sky`
- Extraction tools: HGPAKtool 1.1.3, MBINCompiler 6.45.0-pre1, NMSDK 0.10.0-alpha13.
- Blender staging: `D:\blender\creature\NMSImport`
- Unity rendering: source shaders are not copied. Materials are recreated for Unity's Built-in Render Pipeline.
- Distribution: keep this directory separable from original project assets and verify the written authorization scope before public release.

## Pilot

`Models/AntelopeVariant_0042.fbx` is a deterministic descriptor selection from `ANTELOPERIG` using seed `42`. The pilot contains 12 visible skinned meshes and one 63-bone armature. Its purpose is to validate import scale, skinning, material reconstruction, and the Unity catalog workflow before importing additional creature families.

## Antelope descriptor family

`AntelopeFamily/` contains the editor-generated module catalog for five compatible Antelope rig variants: standard, biped, glow, robot, and bone. The imported source scenes are split into 337 skinned modules and five renderer-free master rigs.

- Runtime species are assembled deterministically from descriptor choices and nested dependencies.
- Module prefabs are individual mesh containers, not complete creature variants.
- `AntelopeVariant_0042` is retained only as a compatibility and failure fallback.
- Runtime code reads only Unity assets under `AntelopeFamily/`; it does not parse MXML or access the game installation.
- The external conversion and Blender staging files remain under `D:\blender\creature\NMSImport`.
