using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        // --- CONFIG ---
        string version = "v0.1.0";
        int major, minor, build;

        string LCD_Keyword = "[KMS]";

        string UpThrust_GroupName = "[Bore] Up Thrust";
        string GridController_Name = "[Bore] Bore Command Chair";
        string JumpDrives_GroupName = "[Bore] JD";

        float refineryYield = 1.0f; // 100%
        
        
        // DO NOT TOUCH
        double maxMass = 0;
        double savedMass = 0;
        bool pendingSaveMass = false;

        // --- Gravity Align State ---
        private bool gravityAlign = false;
        private bool gravityAlignWasActive = false;
        private float gravityAlignPitch = 0f; // degrees, clamped -90 to +90

        // --- Sprite Drawing Stuff ---
        IMyTextSurface _drawingSurface;
        RectangleF _viewport;

        List<IMyThrust> groupThrusters = new List<IMyThrust>();
        IMyBlockGroup upwardGroup;
        IMyShipController controller;
        double TWRPct = 0;
        int gridJumpDriveCount = 0;
        int jumpDriveMaxMass = 1250000;
        int jumpDriveMaxRange = 2000;
        double maxJumpDistance = 0;

        // Class-Level values for sprites
        double gravLevel = 0;
        double gridMass = 0;
        double twrpct = 0;
        string alignStatus = ""; // Is actually used.
        private int _drawTick = 0;

        // Cruise Mode values
        bool cruiseMode = false;
        bool cruiseModeWasActive = false;
        string cruiseStatus = "OFF";
        IMyBlockGroup forwardGroup;
        IMyBlockGroup reverseGroup;
        bool savedGravAlginStatus = false;
        
        // Cargo stuff
        List<IMyCargoContainer> cargos = new List<IMyCargoContainer>();

        class CachedOre
        {
            public string subtypeId;
            public float amount;
        }
        float oreCountStone = 0;
        float oreCountIron = 0;
        float oreCountNickel = 0;
        float oreCountCobalt = 0;
        float oreCountSilicon = 0;
        float oreCountMagnesium = 0;
        float oreCountSilver = 0;
        float oreCountGold = 0;
        float oreCountUranium = 0;
        float oreCountPlatinum = 0;
        float oreCountCyrite = 0;
        float oreCountKarnyxium = 0;
        
        float ingotCountIron = 0;
        float ingotCountNickel = 0;
        float ingotCountCobalt = 0;
        float ingotCountSilicon = 0;
        float ingotCountMagnesium = 0;
        float ingotCountSilver = 0;
        float ingotCountGold = 0;
        float ingotCountUranium = 0;
        float ingotCountPlatinum = 0;
        float ingotCountCyrite = 0;
        float ingotCountKarnyxium = 0;

        private static readonly Dictionary<string, float> OreYields = new Dictionary<string, float>
        {
            { "Iron", 0.7f },
            { "Nickel", 0.4f },
            { "Cobalt", 0.3f },
            { "Magnesium", 0.007f },
            { "Silicon", 0.7f },
            { "Silver", 0.1f },
            { "Gold", 0.01f },
            { "Platinum", 0.005f },
            { "Uranium", 0.01f },
            { "Karnyxium", 0.00005f },
            { "Cyrite", 0.0005f },
        };

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;

            var data = version.Substring(1).Split('.');
            major = data.Length >= 1 ? int.Parse(data[0]) : 0;
            minor = data.Length >= 2 ? int.Parse(data[1]) : 0;
            build = data.Length >= 3 ? int.Parse(data[2]) : 0;

            OreYields["Iron"] *= refineryYield;
            OreYields["Nickel"] *= refineryYield;
            OreYields["Cobalt"] *= refineryYield;
            OreYields["Silicon"] *= refineryYield;
            OreYields["Magnesium"] *= refineryYield;
            OreYields["Silver"] *= refineryYield;
            OreYields["Gold"] *= refineryYield;
            OreYields["Uranium"] *= refineryYield;
            OreYields["Platinum"] *= refineryYield;
            OreYields["Cyrite"] *= refineryYield;
            OreYields["Karnyxium"] *= refineryYield;

            var lcdBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(lcdBlocks, b => b != Me && b.CustomName.Contains(LCD_Keyword));
            var surfaceProvider = lcdBlocks.Count > 0 ? lcdBlocks[0] as IMyTextSurfaceProvider : null;

            if (surfaceProvider != null && surfaceProvider.SurfaceCount > 0)
            {
                _drawingSurface = surfaceProvider.GetSurface(0);
            }
            else
                throw new Exception("Specified block does not have LCDs!");

            _viewport = new RectangleF(
                (_drawingSurface.TextureSize - _drawingSurface.SurfaceSize) / 2f,
                _drawingSurface.SurfaceSize
            );

            PrepareTextSurfaceForSprites(_drawingSurface);
        }

        public void Save()
        {
           
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                string arg = argument.Trim().ToLower();

                if (arg == "savemass")
                {
                    pendingSaveMass = true;
                }
                if (arg == "clearmass")
                {
                    savedMass = 0;
                }
                if (arg == "togglealign")
                {

                }
            }

            IMyBlockGroup upwardGroup = GridTerminalSystem.GetBlockGroupWithName(UpThrust_GroupName);
            IMyShipController controller = (IMyShipController) GridTerminalSystem.GetBlockWithName(GridController_Name);

            double upwardThrust = 0;

            if (upwardGroup == null)
            {
                Echo($"Group not found: '{UpThrust_GroupName}'");
                return;
            }

            upwardGroup.GetBlocksOfType(groupThrusters);

            foreach (var thruster in groupThrusters)
            {
                if (!thruster.Enabled) continue;

                upwardThrust += thruster.MaxEffectiveThrust;
            }

            var massData = controller.CalculateShipMass();
            gridMass = massData.PhysicalMass;
            double gravity = controller.GetNaturalGravity().Length();

            double weight = gridMass * gravity;
            double TWR = upwardThrust / weight;
            TWRPct = TWR / 100;
            maxMass = upwardThrust / gravity;

            if (pendingSaveMass)
            {
                savedMass = maxMass;
                pendingSaveMass = false;
            }

            var jumpDrives = new List<IMyJumpDrive>();
            var gridJumpDrives = GridTerminalSystem.GetBlockGroupWithName(JumpDrives_GroupName);

            gridJumpDrives.GetBlocksOfType(jumpDrives);

            gridJumpDriveCount = jumpDrives.Count;

            double jumpDistanceDivisor = 0;
            if (gridMass < jumpDriveMaxMass)
            {
                jumpDistanceDivisor = 1;
            }
            else
            {
                jumpDistanceDivisor = (gridMass / jumpDriveMaxMass);
            }

            maxJumpDistance = (gridJumpDriveCount * jumpDriveMaxRange) / jumpDistanceDivisor;
            
            Echo($"Max Jump Range: {maxJumpDistance:N0} km");
            Echo($"JD Count: {gridJumpDriveCount}");
            Echo($"Grid Mass: {gridMass:N0} kg");
            Echo($"Gravity Align Enabled?: {gravityAlign}");

            var totalOres = new List<CachedOre>();
            
            oreCountStone = 0;
            oreCountIron = 0;
            oreCountNickel = 0;
            oreCountCobalt = 0;
            oreCountSilicon = 0;
            oreCountMagnesium = 0;
            oreCountSilver = 0;
            oreCountGold = 0;
            oreCountUranium = 0;
            oreCountPlatinum = 0;
            oreCountCyrite = 0;
            oreCountKarnyxium = 0;
            
            ingotCountIron = 0;
            ingotCountNickel = 0;
            ingotCountCobalt = 0;
            ingotCountSilicon = 0;
            ingotCountMagnesium = 0;
            ingotCountSilver = 0;
            ingotCountGold = 0;
            ingotCountUranium = 0;
            ingotCountPlatinum = 0;
            ingotCountCyrite = 0;
            ingotCountKarnyxium = 0;

            var cargos = new List<IMyCargoContainer>();
            GridTerminalSystem.GetBlocksOfType(cargos);

            foreach (IMyCargoContainer cargo in cargos)
            {
                var inventory = cargo.GetInventory(0);
                var inventoryItems = new List<MyInventoryItem>();
                inventory.GetItems(inventoryItems);

                foreach (MyInventoryItem item in inventoryItems)
                {
                    if (item.Type.TypeId.EndsWith("Ore"))
                    {
                        CachedOre existing = totalOres.Find(ore => ore.subtypeId == item.Type.SubtypeId);

                        if (existing != null)
                        {
                            existing.amount += (float)item.Amount;
                        }
                        else
                        {
                            totalOres.Add(
                                new CachedOre { subtypeId = item.Type.SubtypeId, amount = (float)item.Amount });
                        }
                    }
                }
            }

            foreach (CachedOre oreItem in totalOres)
            {
                
                switch (oreItem.subtypeId)
                {
                    case "Stone":
                        oreCountStone = oreItem.amount;
                        break;
                    case "Iron":
                        oreCountIron = oreItem.amount;
                        ingotCountIron = oreCountIron * OreYields["Iron"];
                        break;
                    case "Nickel":
                        oreCountNickel = oreItem.amount;
                        ingotCountNickel = oreCountNickel * OreYields["Nickel"];
                        break;
                    case "Cobalt":
                        oreCountCobalt = oreItem.amount;
                        ingotCountCobalt = oreCountCobalt * OreYields["Cobalt"];
                        break;
                    case "Silicon":
                        oreCountSilicon = oreItem.amount;
                        ingotCountSilicon = oreCountSilicon * OreYields["Silicon"];
                        break;
                    case "Magnesium":
                        oreCountMagnesium = oreItem.amount;
                        ingotCountMagnesium = oreCountMagnesium * OreYields["Magnesium"];
                        break;
                    case "Silver":
                        oreCountSilver = oreItem.amount;
                        ingotCountSilver = oreCountSilver * OreYields["Silver"];
                        break;
                    case "Gold":
                        oreCountGold = oreItem.amount;
                        ingotCountGold = oreCountGold * OreYields["Gold"];
                        break;
                    case "Uranium":
                        oreCountUranium = oreItem.amount;
                        ingotCountUranium = oreCountUranium * OreYields["Uranium"];
                        break;
                    case "Platinum":
                        oreCountPlatinum = oreItem.amount;
                        ingotCountPlatinum = oreCountPlatinum * OreYields["Platinum"];
                        break;
                    case "Cyrite":
                        oreCountCyrite = oreItem.amount;
                        ingotCountCyrite = oreCountCyrite * OreYields["Cyrite"];
                        break;
                    case "Karnyxium":
                        oreCountKarnyxium = oreItem.amount;
                        ingotCountKarnyxium = oreCountKarnyxium * OreYields["Karnyxium"];
                        break;
                    case "CompactedStone":
                        oreCountStone = oreItem.amount;
                        break;
                    case "CompactedIron":
                        oreCountIron = oreItem.amount; 
                        ingotCountIron = oreCountIron * OreYields["Iron"];
                        break;
                    case "CompactedNickel":
                        oreCountNickel = oreItem.amount;
                        ingotCountNickel = oreCountNickel * OreYields["Nickel"];
                        break;
                    case "CompactedCobalt":
                        oreCountCobalt = oreItem.amount;
                        ingotCountCobalt = oreCountCobalt * OreYields["Cobalt"];
                        break;
                    case "CompactedSilicon":
                        oreCountSilicon = oreItem.amount;
                        ingotCountSilicon = oreCountSilicon * OreYields["Silicon"];
                        break;
                    case "CompactedMagnesium":
                        oreCountMagnesium = oreItem.amount;
                        ingotCountMagnesium = oreCountMagnesium * OreYields["Magnesium"];
                        break;
                    case "CompactedSilver":
                        oreCountSilver = oreItem.amount;
                        ingotCountSilver = oreCountSilver * OreYields["Silver"];
                        break;
                    case "CompactedGold":
                        oreCountGold = oreItem.amount;
                        ingotCountGold = oreCountGold * OreYields["Gold"];
                        break;
                    case "CompactedUranium":
                        oreCountUranium = oreItem.amount;
                        ingotCountUranium = oreCountUranium * OreYields["Uranium"];
                        break;
                    case "CompactedPlatinum":
                        oreCountPlatinum = oreItem.amount;
                        ingotCountPlatinum = oreCountPlatinum * OreYields["Platinum"];
                        break;
                    

                }
            }
            
            var frame = _drawingSurface.DrawFrame();
            DrawSprites(ref frame);
            frame.Dispose();

        }

        public void PrepareTextSurfaceForSprites(IMyTextSurface textSurface)
        {
            textSurface.ScriptBackgroundColor = new Color(0, 0, 0, 255);
            textSurface.ContentType = ContentType.SCRIPT;
            textSurface.Script = "";
        }

        public void DrawSprites(ref MySpriteDrawFrame frame)
        {
            var position = new Vector2(256, 0) + _viewport.Position;
            
            var oreDisplayIncrement = new Vector2(0, 26);

            float scale = 1.0f;

            _drawTick++;

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = $"Max Jump Distance: {maxJumpDistance:N0} km",
                Position = position,
                RotationOrScale = scale,
                Color = Color.White,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });


            if (oreCountStone > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Stone: {oreCountStone:N0}",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            
            if (oreCountIron > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Iron Ore: {oreCountIron:N0} ({ingotCountIron:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountNickel > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Nickel Ore: {oreCountNickel:N0} ({ingotCountNickel:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountCobalt > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Cobalt Ore: {oreCountCobalt:N0} ({ingotCountCobalt:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountSilicon > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Silicon Ore: {oreCountSilicon:N0} ({ingotCountSilicon:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountMagnesium > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Magnesium Ore: {oreCountMagnesium:N0} ({ingotCountMagnesium:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountSilver > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Silver Ore: {oreCountSilver:N0} ({ingotCountSilver:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountGold > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Gold Ore: {oreCountGold:N0} ({ingotCountGold:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountUranium > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Uranium Ore: {oreCountUranium:N0} ({ingotCountUranium:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountPlatinum > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Platinum Ore: {oreCountPlatinum:N0} ({ingotCountPlatinum:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountCyrite > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Cyrite Ore: {oreCountCyrite:N0} ({ingotCountCyrite:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
            if (oreCountKarnyxium > 0)
            {
                position += oreDisplayIncrement;

                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = $"Karnyxium Ore: {oreCountKarnyxium:N0} ({ingotCountKarnyxium:N0} ingots)",
                    Position = position,
                    RotationOrScale = scale,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                });
            }
        }
    }
}
