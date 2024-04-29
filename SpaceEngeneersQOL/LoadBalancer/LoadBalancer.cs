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

namespace IngameScript.LoadBalancer
{
    partial class Program : MyGridProgram
    {
        private readonly MyFixedPoint volumeToKeepEmpty = 2; // m^3
        private readonly List<IMyAssembler> assemblers = new List<IMyAssembler>();
        private readonly List<IMyCargoContainer> allCargoContainers = new List<IMyCargoContainer>();

        private readonly IMyTextSurface lcdKeyboard;
        private readonly IMyTextSurface lcdDebug;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            GridTerminalSystem.GetBlocksOfType(assemblers);
            GridTerminalSystem.GetBlocksOfType(allCargoContainers);
            lcdDebug = Me.GetSurface(0);
            lcdKeyboard = Me.GetSurface(1);
        }

        private IEnumerable<IMyCargoContainer> GetFreeInventoryBlocks()
        {
            var inventoryBlocksWithFreeSpace = allCargoContainers
                .Where(block => block.GetInventory(block.InventoryCount - 1).VolumeFillFactor < 0.99);
            return inventoryBlocksWithFreeSpace;
        }
        private IEnumerable<IMyAssembler> GetCloggedAssemblers()
        {
            var cloggedAssemblers = assemblers.Where(assembler =>
            {
                if (assembler.GetInventory() != null)
                {
                    var inv = assembler.GetInventory(0);
                    return inv.MaxVolume - inv.CurrentVolume < volumeToKeepEmpty;
                }
                return false;
            });
            return cloggedAssemblers;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            var inventoryBlocksWithFreeSpace = GetFreeInventoryBlocks();
            var cloggedAssemblers = GetCloggedAssemblers();

            var aaC = assemblers.Count;
            var caC = cloggedAssemblers.Count();
            var acC = allCargoContainers.Count;
            var ifC = inventoryBlocksWithFreeSpace.Count();

            lcdKeyboard.WriteText($"********* LOAD BALANCER **********\n", false);
            lcdKeyboard.WriteText($"* Assemblers Found........: {aaC,4} *\n", true);
            lcdKeyboard.WriteText($"*    of which                    *\n", true);
            lcdKeyboard.WriteText($"*       clogged...........: {caC,4} *\n", true);
            lcdKeyboard.WriteText($"* All Cargo blocks........: {acC,4} *\n", true);
            lcdKeyboard.WriteText($"*    of which                    *\n", true);
            lcdKeyboard.WriteText($"*       with free space...: {ifC,4} *\n", true);
            lcdKeyboard.WriteText($"**********************************\n", true);

            lcdDebug.WriteText("");

            foreach (var assembler in cloggedAssemblers)
            {
                var cloggedInventory = assembler.GetInventory(0);
                var availableInventories = inventoryBlocksWithFreeSpace.Select((block) => block.GetInventory(block.InventoryCount - 1));
                Unclog(cloggedInventory, availableInventories);
            }

        }

        private void Unclog(IMyInventory cloggedInventory, IEnumerable<IMyInventory> availableInventories)
        {
            var volumeToFree = volumeToKeepEmpty - (cloggedInventory.MaxVolume - cloggedInventory.CurrentVolume);
            List<MyInventoryItem> items = new List<MyInventoryItem> { };
            cloggedInventory.GetItems(items);

            var itemToMove = items.MaxBy(item => item.Amount.RawValue);

            var inventoryIndex = 0;
            while (volumeToFree > 0 && inventoryIndex < availableInventories.Count() - 1)
            {
                var target = availableInventories.ElementAt(inventoryIndex);
                var targetCapacity = target.MaxVolume - target.CurrentVolume;

                var transfer = targetCapacity < volumeToFree ? targetCapacity : volumeToFree;
                var result = target.TransferItemFrom(cloggedInventory, itemToMove, transfer * 1000);

                if (result)
                {
                    volumeToFree = volumeToKeepEmpty - (cloggedInventory.MaxVolume - cloggedInventory.CurrentVolume);
                }
                else
                {
                    // debug time
                    lcdDebug.WriteText($"Unable to transfer item ${itemToMove.Type.SubtypeId}\n", true);
                    lcdDebug.WriteText($"     from ${cloggedInventory.Owner.DisplayName}\n", true);
                    lcdDebug.WriteText($"     to ${target.Owner.DisplayName}\n", true);
                }

                inventoryIndex += 1;
            }
        }

    }
}
