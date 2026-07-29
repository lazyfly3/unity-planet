# Spaceship PCG pipeline

Unity menu: `Tools > Spacecraft > PCG Fleet`.

The editor invokes Blender 5.1 in `--background --factory-startup` mode,
generates into `Temp/SpaceshipPCG`, validates the report, and only then
publishes FBX, thumbnails, prefabs and data assets.

Command-line smoke test:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --background --factory-startup `
  --python Tools/Blender/SpaceshipPCG/generate_fleet.py -- `
  --manifest Tools/Blender/SpaceshipPCG/fleet_manifest.json `
  --scope hull --id balanced_00 `
  --staging Temp/SpaceshipPCG/smoke `
  --report Temp/SpaceshipPCG/smoke/report.json
```

Generate the same selection twice and compare `geometryHash` values with
`verify_reports.py`. FBX byte hashes are also recorded for provenance, but
geometry hashes are the reproducibility contract because FBX headers can
contain exporter metadata.

## Modular player-ship prototype

The editable modular prototype is deliberately isolated from the production
fleet publisher. It writes its FBX, thumbnail, report, and review `.blend` only
under `Library/`:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --background --factory-startup `
  --python Tools/Blender/SpaceshipPCG/generate_modular_prototype.py
```

After validation, open the interactive audit scene:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --python Tools/Blender/SpaceshipPCG/open_modular_ship_review.py
```

The review file contains source modules, a sealed base flight hull, explorer
and combat configurations, an exploded socket audit, and hidden FBX contract
meshes. The base hull retains its central cruise drive and reaction-control
thrusters; optional twin engine pods are booster/long-range modules. Empty
wing, engine, hardpoint, dorsal, and ventral interfaces use removable flush
covers, so an unloaded hull still reads as a complete spacecraft. It does not
publish or replace any Unity asset.

## High-detail free-placement flagship v2

The v2 arrowhead flagship replaces fixed hull sockets with a continuous
placement surface. It keeps the unloaded hull visually complete, exports a
separate hidden `PlacementSurface`, and authors universal module saddles:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --background --factory-startup `
  --python Tools/Blender/SpaceshipPCG/generate_flagship_v2.py
```

Open the validated review file:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --python Tools/Blender/SpaceshipPCG/open_flagship_v2_review.py
```

The default review view shows only the unloaded hero hull. Toggle the module
library, balanced free-build, asymmetric free-build, or wireframe placement
surface collections in the Outliner. All generated files remain under
`Library/` until the art review is approved.

## Three-hull flagship family

Generate the thin lenticular saucer, rigid-solar-sailer, and conventional
fighter plus their individual review scenes and the combined fleet review:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --background --factory-startup `
  --python Tools/Blender/SpaceshipPCG/generate_flagship_family.py
```

Generate one hull during iteration with `-- --archetype saucer`, `sailer`, or
`fighter`. The shared 13-module contract is emitted once per run, and all
outputs remain under `Library/SpaceshipPCGStaging/flagship_family_v1` and
`Library/SpaceshipPCGReview`.

Open the combined review:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --python Tools/Blender/SpaceshipPCG/open_flagship_family_review.py
```

## External asset audit helpers

Legacy Blender 2.68 ASCII FBX 6.1 files can be converted to UV-preserving OBJ
files before review:

```powershell
python Tools/Blender/SpaceshipPCG/convert_ascii_fbx_to_obj.py `
  --input-root Library/SpaceshipAssetReview/fleet_i_modules/source/FBX `
  --output-root Library/SpaceshipAssetReview/fleet_i_modules/converted_obj
```

Build a normalized, categorized Blender gallery and JSON geometry report from
an FBX, OBJ, GLB, or glTF directory:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --background --factory-startup `
  --python Tools/Blender/SpaceshipPCG/review_external_asset_library.py -- `
  --input-root <model-directory> `
  --output-blend <review.blend> `
  --output-report <report.json> `
  --output-preview <overview.png>
```

Restore the Fleet I grey Unity material family, pack all used textures, and
save a self-contained Blender audit:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' `
  --background --factory-startup `
  --python Tools/Blender/SpaceshipPCG/review_external_asset_library.py -- `
  --input-root Library/SpaceshipAssetReview/fleet_i_modules/converted_obj `
  --output-blend Library/SpaceshipAssetReview/fleet_i_modules/fleet_i_modules_material_review.blend `
  --output-report Library/SpaceshipAssetReview/fleet_i_modules/fleet_i_modules_material_report.json `
  --output-preview Library/SpaceshipAssetReview/fleet_i_modules/fleet_i_modules_material_overview.png `
  --material-profile fleet-i `
  --texture-root Library/SpaceshipAssetReview/fleet_i_modules/source `
  --pack-resources
```

`extract_unitypackage_materials.py` extracts the original Unity `.mat`,
`.meta`, and preview records. `decode_astc_textures.py` converts
gzip-compressed ASTC textures before Blender review when a source FBX refers
to mobile-compressed normal or bump maps.

These helpers only write audit artifacts below `Library/`; they do not import
or publish third-party models into Unity `Assets`.
