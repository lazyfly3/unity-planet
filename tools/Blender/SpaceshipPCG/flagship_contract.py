"""Shared contracts for the high-detail free-placement flagship family."""

from __future__ import annotations

from dataclasses import dataclass


MATERIAL_NAMES = ("HullPrimary", "HullDark", "HullEmission", "Accent")
HULL_BUDGETS = {
    "lod0Triangles": 180000,
    "lod1Triangles": 80000,
    "lod2Triangles": 20000,
    "collisionTriangles": 200,
    "placementTriangles": 18000,
    "dimensionTolerance": 0.0045,
    "minimumLod0Triangles": 120000,
}


@dataclass(frozen=True)
class ModuleSpec:
    module_id: str
    kind: str
    scale: float


MODULE_SPECS = (
    ModuleSpec("ThrusterSmall", "thruster", 0.65),
    ModuleSpec("ThrusterMedium", "thruster", 1.00),
    ModuleSpec("ThrusterLarge", "thruster", 1.45),
    ModuleSpec("EngineNacelle", "engine_nacelle", 1.00),
    ModuleSpec("KineticRepeater", "kinetic_weapon", 1.00),
    ModuleSpec("EnergyPulse", "energy_weapon", 1.00),
    ModuleSpec("SweptWing", "swept_wing", 1.00),
    ModuleSpec("DeltaWing", "delta_wing", 1.00),
    ModuleSpec("Canard", "canard", 1.00),
    ModuleSpec("VerticalFin", "vertical_fin", 1.00),
    ModuleSpec("Radiator", "radiator", 1.00),
    ModuleSpec("SensorMast", "sensor", 1.00),
    ModuleSpec("ArmorFairing", "armor_fairing", 1.00),
)
