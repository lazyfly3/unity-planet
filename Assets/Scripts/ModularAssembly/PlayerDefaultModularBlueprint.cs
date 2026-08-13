using UnityEngine;

namespace ModularAssembly
{
    /// <summary>
    /// Project-owned starter ship for player saves that do not have a
    /// modular_ship.json yet. This is a data-only copy of save "1231";
    /// existing save files always take precedence over this template.
    /// </summary>
    public static class PlayerDefaultModularBlueprint
    {
        const string StructureId =
            "neox@block:common:block_111";
        const string LargeRocketId =
            "neox@block:common:rocket_222";
        const string EnergyCoreId =
            "neox@block:core:core_energy_111";

        public static ModularBlueprintData Create()
        {
            return new ModularBlueprintData
            {
                formatVersion =
                    ModularBlueprintData.CurrentFormatVersion,
                savedUtcTicks = 0,
                coreAssistMode =
                    UnityPlanet.ModularAssembly.
                        VehicleCoreAssistMode.Training,
                modules = new[]
                {
                    Module("core", GridAssemblyModel.CoreModuleId,
                        -1, -1, -1, 0),

                    Module("176098ce34784ca7b939f3a8ab462f9f",
                        StructureId, -2, 0, 0, 14,
                        "0f876647085f45e39579f075f3509380"),
                    Module("78e1d075831c4ecd84a4b5d25f23de92",
                        StructureId, 1, 0, 0, 6,
                        "0f876647085f45e39579f075f3509380"),
                    Module("63bb7500147646b7a5f1a88db7d41ada",
                        StructureId, -2, 0, -1, 14,
                        "0bce927ec22b448b9c04b15be8acc978"),
                    Module("923e7ba697d348cd8dd3d95a2c3010f0",
                        StructureId, 1, 0, -1, 6,
                        "0bce927ec22b448b9c04b15be8acc978"),
                    Module("edd0f1bc9e2a4c369932b3865fc85137",
                        StructureId, 0, 0, -2, 10,
                        "bf845f2297df4091998cdb4ba54bd27a"),
                    Module("1638f5e5561b4b1684d73e3c1251c758",
                        StructureId, -1, 0, -2, 10,
                        "bf845f2297df4091998cdb4ba54bd27a"),
                    Module("3995e83f024447a08e565cf615e0339a",
                        StructureId, -2, 0, -2, 14,
                        "4b1283f7a706443994097d3b400c03a4"),
                    Module("f1196773571440a88704a8f2173ba647",
                        StructureId, 1, 0, -2, 6,
                        "4b1283f7a706443994097d3b400c03a4"),
                    Module("f31618b8ad9248808323909ea0d29d75",
                        StructureId, 0, 0, 1, 0,
                        "f35e75639a004a1f819e79d0ff729733"),
                    Module("33c1fd6fe5b64267af43e07738468296",
                        StructureId, -1, 0, 1, 0,
                        "f35e75639a004a1f819e79d0ff729733"),
                    Module("ca91016f4eb4422db7f1950a20bf7365",
                        StructureId, -2, 0, 1, 0,
                        "96ab273fd333490d92c525e2ce083ab8"),
                    Module("edf75962fc9743dc8c9ae0f9416f862d",
                        StructureId, 1, 0, 1, 0,
                        "96ab273fd333490d92c525e2ce083ab8"),

                    Module("f2a7c1dc40a94c1db9a1d684ccd0976e",
                        LargeRocketId, -1, -3, -1, 20),
                    Module("4083edd8642940699c97ba7155438efa",
                        LargeRocketId, -2, 1, -1, 16,
                        "d9aff94e026a40b6ba3590bbc543d144"),
                    Module("dda6ed92cc2b49069d237a0b2cd87517",
                        LargeRocketId, 0, 1, -1, 16,
                        "d9aff94e026a40b6ba3590bbc543d144"),
                    Module("2aacdceab60443559ae2c43b41c1bc72",
                        LargeRocketId, -2, 0, 2, 0,
                        "b6953a00d7a948c78236d9235e70a63d"),
                    Module("be310aa0b32942ea819cbd185c1fa8f4",
                        LargeRocketId, 0, 0, 2, 0,
                        "b6953a00d7a948c78236d9235e70a63d"),
                    Module("f0df744ad6434133afe67e2fb2e86106",
                        LargeRocketId, -2, 0, -4, 10,
                        "e496b653522a43f18b7214fe91f85267"),
                    Module("cf2a490887204dc7a0d1fa90fde30d64",
                        LargeRocketId, 0, 0, -4, 10,
                        "e496b653522a43f18b7214fe91f85267"),
                    Module("b6278ce96a78421787290e7f2762cb93",
                        LargeRocketId, -4, 0, -1, 14,
                        "500d38642f2b4ad3a7f0d1d36ad9dabe"),
                    Module("eee05d2864974dffba4b21bc61c0105d",
                        LargeRocketId, 2, 0, -1, 6,
                        "500d38642f2b4ad3a7f0d1d36ad9dabe"),

                    Module("29021a01285d4dd0a686cb3db93fa3c3",
                        EnergyCoreId, 0, 1, 1, 16,
                        "b38e5be6a7c5433ca6ab64fef3cbc98d"),
                    Module("a404f36cea294d0d8fd57f587762c83d",
                        EnergyCoreId, -1, 1, 1, 16,
                        "b38e5be6a7c5433ca6ab64fef3cbc98d"),
                    Module("d3cb46b89c834e6ba5ea5dc456584481",
                        EnergyCoreId, 0, 1, -2, 16,
                        "7e15c30760594872a7e3ccdf91c594aa"),
                    Module("f49ee5d9aa874c559abe9a3af64f5094",
                        EnergyCoreId, -1, 1, -2, 16,
                        "7e15c30760594872a7e3ccdf91c594aa"),

                    Module("1c65b7e3fc954b59b67dccc34e34ed71",
                        LargeRocketId, -1, -2, 1, 20),
                    Module("753efb989a634bf7a445bb387edc0a59",
                        LargeRocketId, 1, -2, -2, 20,
                        "74d0b90f59994fe0bfea106ce09d0284"),
                    Module("0295442b6b934ee3add01486744c0a17",
                        LargeRocketId, -3, -2, -2, 20,
                        "74d0b90f59994fe0bfea106ce09d0284")
                }
            };
        }

        static ModularBlueprintModule Module(
            string runtimeId,
            string moduleId,
            int x,
            int y,
            int z,
            int orientation,
            string mirrorGroupId = "")
        {
            return new ModularBlueprintModule
            {
                runtimeId = runtimeId,
                moduleId = moduleId,
                pose = new GridModulePose(
                    new Vector3Int(x, y, z),
                    orientation,
                    mirrorGroupId),
                behaviorSettings = string.Empty
            };
        }
    }
}
