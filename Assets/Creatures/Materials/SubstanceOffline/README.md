# Substance Offline Creature Materials

This folder is the fallback path for Substance-authored creature textures.

Use Substance Designer/Sampler/Painter or the Unity Substance plugin to export bitmap textures into:

`Assets/Creatures/Materials/SubstanceOffline/Exports/`

The Unity project does not require the Substance plugin at runtime. If the plugin is installed, use it only to author or export textures. If it is missing, the NMS creature generator falls back to the exported bitmap catalog.

## Naming

Textures are grouped by file name prefix. These suffixes are recognized:

- Base color: `_BaseColor`, `_Base_Color`, `_Albedo`, `_Diffuse`, `_Color`
- Normal: `_Normal`, `_NormalMap`, `_Nrm`, `_N`
- Mask: `_Mask`, `_Masks`, `_MaskMap`, `_ORM`, `_RMA`
- Emission: `_Emission`, `_Emissive`, `_Glow`

Example:

```text
lizard_skin_BaseColor.png
lizard_skin_Normal.png
lizard_skin_Mask.png
lizard_skin_Emission.png
```

After exporting, run:

`Tools > Creatures > Substance > Rebuild Offline Texture Catalog`

The generated runtime catalog is:

`Assets/Resources/Creatures/NmsOrganicMaterialCatalog.asset`

`NmsRandomCreatureGenerator` auto-loads this asset when its `Organic Material Catalog` field is empty.
