using Sandbox.Game;
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
using VRage.Game.VisualScripting.Utils;
using VRageMath;

namespace SpaceEngeneersQOL.LoadBalancer
{
    partial class Program : MyGridProgram
    {
        #region -- CUT FROM HERE


        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        private float treshold = 0.06f;
        private string ledPanelName = "LoadBalancer.LCD";

        public void Main(string argument, UpdateType updateSource)
        {
            List<IMyAssembler> assemblers = new List<IMyAssembler>();
            var lcd = GridTerminalSystem.GetBlockWithName(ledPanelName) as IMyTextPanel;
            GridTerminalSystem.GetBlocksOfType(assemblers);
            lcd.WritePublicTitle("Load Balancer");
            var full = assemblers.Where(assembler => {
                if (assembler.GetInventory() != null)
                {
                    float ratio = (float)assembler.GetInventory().CurrentVolume.ToIntSafe() / assembler.GetInventory().MaxVolume.ToIntSafe();
                    return ratio >= 1 - treshold;
                }
                return false;
            }).ToList();

            lcd.WriteText($"Number of full assemblers: {full.Count} of {assemblers.Count}\n");
            var withQueue = full.Where(inv => !inv.IsQueueEmpty).ToList();
            lcd.WriteText($"Assemblers with queue: {withQueue.Count} of {full.Count}\n", true);
            var inventoryBlocks = new List<IMyInventoryOwner>();
            GridTerminalSystem.GetBlocksOfType(inventoryBlocks);
            inventoryBlocks = inventoryBlocks.Where(block => !(block is IMyAssembler)
                                      && block.UseConveyorSystem
                                      && block.GetInventory(block.InventoryCount - 1) is IMyInventory
                                      && !block.GetInventory(block.InventoryCount - 1).IsFull).ToList();
            var myInventory = inventoryBlocks.Select(inv => inv.GetInventory(inv.InventoryCount - 1)).ToList();

            if (withQueue.Count > 0)
            {
                lcd.WriteText($"Number of usable inventory: {inventoryBlocks.Count}\n", true);

                if (inventoryBlocks.Count > 0 && withQueue.Count > 0) withQueue.ForEach(assembler => rearrange(assembler, lcd, myInventory));
            }
        }

        private void rearrange(IMyAssembler assembler, IMyTextPanel lcd, List<IMyInventory> inventoryBlocks)
        {
            var inventory = assembler.GetInventory(0);
            var items = new List<MyInventoryItem>();
            inventory.GetItems(items);
            lcd.WriteText($"{assembler.CustomName}:\t", true);
            items = items.OrderBy(item => item.Amount.ToIntSafe()).ToList();
            items.ForEach(item => {
                lcd.WriteText($"{item.Type.SubtypeId}[{item.Amount.ToIntSafe() / 1000}k]\t", true);
            });
            lcd.WriteText("\n", true);

            var itemToMove = items.Last();
            var destinations = inventoryBlocks
                .Where(destInv =>
                {
                    var can = inventory.CanTransferItemTo(destInv, itemToMove.Type);
                    var itemsTypes = new List<MyItemType>();
                    inventory.GetAcceptedItems(itemsTypes);
                    can = can && itemsTypes.TrueForAll(typ => typ.TypeId == itemToMove.Type.TypeId);
                    return can;
                })
                .OrderBy(destInv => destInv.CurrentVolume.ToIntSafe()).ToList();
            if (destinations.Count < 1)
            {
                lcd.WriteText("! No destination found !\n", true);
            }
            foreach (var destination in destinations)
            {
                lcd.WriteText($"Try to move item {itemToMove.Type.SubtypeId} from {assembler.CustomName} to {destination.Owner} inventory... ", true);
                var success = inventory.TransferItemTo(destination, itemToMove, itemToMove.Amount.ToIntSafe() / 2);
                lcd.WriteText(success ? "done!" : "fail", true);
                lcd.WriteText("\n", true);
                if (success) break;
            }

        }
        #endregion // -- TO HERE
    }
}
