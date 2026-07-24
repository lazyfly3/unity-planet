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
