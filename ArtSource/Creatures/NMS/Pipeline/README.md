# NMS Creature Family Pipeline

Data-driven creature-family pipeline using original geometry InvBindMatrix,
native animation TRS, descriptor modules, deterministic SplitMix64 selection,
per-frame validation, human review, and an explicit Unity publish gate.

Run the review stages in Blender:

```python
import sys
sys.path.insert(0, r"D:\unity planet\unity-planet\ArtSource\Creatures\NMS\Pipeline")
from run_pipeline import run_family

review = run_family(
    "TrexBiped",
    stages=("extract", "baseline", "actions", "library", "validate", "review"),
)
print(review["validationHash"])
```

After manually reviewing the generated Blend files and review renders, publish
with the exact validation hash:

```python
run_family(
    "TrexBiped",
    stages=("unity_publish",),
    approval=review["validationHash"],
)
```

unity_publish rejects missing or stale approval hashes. It exports from the
validated SpeciesLibrary Blend, preserves the original Armature and native
actions, and writes the publish request last. Unity then creates the runtime
Prefab, Animator Controller, family definition, and updates the global catalog.

New families are added in creature_families.json; the runtime generator does
not require family-specific C# changes. See USAGE_CN.md for the full contract.
