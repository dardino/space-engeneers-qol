using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
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

namespace IngameScript.DrillManagement
{
    public class Program : MyGridProgram
    {
        /*
         * Questo script gestisce l'avanzamento verticale di una trivella rotante
         * per poter funzionare ha bisogno di trovare alcuni componenti, procedere come segue:
         * 1. Mettere i nomi corretti per i seguenti componenti:
         *    PistonGroupName      =>  Nome del gruppo che include tutti i pistoni verticali che spingono le trivelle
         *    DrillsGroupName      =>  Nome del gruppo che include tutte le trivelle
         *    DrillRotorName       =>  Nome del rotore che fa ruotare la trivella
         *    DrillDebugLightName  =>  Nome della luce da accendere quando c'è un problema
         *    DrillLcdPanelName    =>  (opzionale) Nome del componente LCD su cui scrivere l'avanzamento.
         * 2. Controllare il metodo `getPistonIndexFromName` ed eventualmente modificarlo per ottenere
         *    un criterio di ordinamento per i pistoni verticali, sarà l'ordine con cui vengono estesi
         */
        static class Config
        {
            public static readonly string PistonGroupName = "Trivella.Pistons";
            public static readonly string DrillsGroupName = "Trivella.Drills";
            public static readonly string DrillRotorName = "Trivella.Rotor";
            public static readonly string DrillDebugLightName = "Trivella.DebugLight";
            /*
             * Pannello LCD su cui scrivere lo stato di avanzamento:
             * se non lo trova usa il pannello principale del blocco program
             */
            public static readonly string DrillLcdPanelName = "Trivella.TextPanel";
            /*
             * Funzione che restituisce un indice a partire dal nome del pistone,
             * questo indice influenzerà l'ordine di estensione dei pistoni
             */
            public static readonly System.Text.RegularExpressions.Regex FindIndexRegex =
              // la regular expression utilizata deve restituire un `Named Group` chiamato "index" con all'interno l'indice da usare
              // la regular expression usata è questa: `^.*(?<index>\d+)$`
              // che tradotta in italiano significa: trova qualunque gruppo di numeri che sia alla fine della stringa e che sia preceduto da
              // un numero indefinito di caratteri qualunque a partire dal primo e chiamalo "index"
              new System.Text.RegularExpressions.Regex(@"^.*(?<index>\d+)$");
        }

        private readonly float angleThreshold = 0.1f;
        private readonly float fillThreshold = 0.9f;
        private float nextTargetAngle = 0.0f;
        private IMyTextSurface logDisplay;
        private int getPistonIndexFromName(string text)
        {
            var match = Config.FindIndexRegex.Match(text);
            return match.Success ? int.Parse(match.Groups["index"].Value) : 0;
        }
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        private float GetReachableCargoFillRatio(VRage.Game.ModAPI.Ingame.IMyInventory referenceInventory)
        {
            List<IMyInventoryOwner> blocksInTheGrid = new List<IMyInventoryOwner>();
            GridTerminalSystem.GetBlocksOfType(blocksInTheGrid, (block) =>
              block.HasInventory &&
              referenceInventory != null &&
              referenceInventory.IsConnectedTo(block.GetInventory(block.InventoryCount - 1))
            );
            Echo("Connected Inventory owners: " + blocksInTheGrid.Count.ToString());

            var currentTotalVolume = 0.0f;
            var maxTotalVolume = 0.0f;

            foreach (var block in blocksInTheGrid)
            {
                var inventory = block.GetInventory(block.InventoryCount - 1);
                currentTotalVolume += (float)inventory.CurrentVolume;
                maxTotalVolume += (float)inventory.MaxVolume;
            }

            var totalFillRatio = currentTotalVolume / maxTotalVolume;

            return totalFillRatio;
        }

        private bool CheckExistence(object entity, string entityName)
        {
            if (logDisplay == null)
            {
                Echo("no output for debugging");
                return false;
            }

            if (entity == null)
            {
                logDisplay.WriteText($"[CFG ERR]: entity not found: {entityName}");
                return false;
            }

            return true;
        }

        private bool HasCompletedFullTurn(IMyMotorStator rotor)
        {
            return rotor.Angle < nextTargetAngle + angleThreshold && rotor.Angle >= nextTargetAngle - angleThreshold;
        }

        private void TurnOn(object entity)
        {
            if (entity is IMyTerminalBlock)
            {
                (entity as IMyTerminalBlock).ApplyAction("OnOff_On");
            }
            else if (entity is IMyBlockGroup)
            {
                var children = new List<IMyTerminalBlock>();
                (entity as IMyBlockGroup).GetBlocks(children);
                foreach (var block in children)
                {
                    block.ApplyAction("OnOff_On");
                }
            }
        }

        private void TurnOff(object entity)
        {
            if (entity is IMyTerminalBlock)
            {
                (entity as IMyTerminalBlock).ApplyAction("OnOff_Off");
            }
            else if (entity is IMyBlockGroup)
            {
                var children = new List<IMyTerminalBlock>();
                (entity as IMyBlockGroup).GetBlocks(children);
                foreach (var block in children)
                {
                    block.ApplyAction("OnOff_Off");
                }
            }
        }

        private void IncreaseMaxDistanceAndMove(IMyPistonBase piston)
        {
            piston.ApplyAction("IncreaseUpperLimit");
            piston.ApplyAction("Extend");
        }

        private void LogPistonPosition(IMyPistonBase current, List<IMyPistonBase> pistons)
        {
            pistons.ForEach((piston) =>
            {
                var currentFlag = current?.EntityId == piston.EntityId ? "> " : "";
                logDisplay.WriteText($"{currentFlag}{piston.CustomName} at {piston.CurrentPosition} / {piston.MaxLimit} | {piston.HighestPosition}\n", true);
            });
        }

        private bool AssertConfig(object pistons, object drills, object rotor, object light)
        {
            if (!CheckExistence(pistons, "Pistons Group")) return false;
            if (!CheckExistence(drills, "Drills Group")) return false;
            if (!CheckExistence(rotor, "Rotor")) return false;
            if (!CheckExistence(light, "Light")) return false;
            return true;
        }
        public void Main(string argument, UpdateType updateSource)
        {
            var pistons = GridTerminalSystem.GetBlockGroupWithName(Config.PistonGroupName);
            var drills = GridTerminalSystem.GetBlockGroupWithName(Config.DrillsGroupName);
            var rotor = GridTerminalSystem.GetBlockWithName(Config.DrillRotorName) as IMyMotorStator;
            object light = GridTerminalSystem.GetBlockWithName(Config.DrillDebugLightName);
            if (light == null) { light = GridTerminalSystem.GetBlockGroupWithName(Config.DrillDebugLightName); }
            logDisplay = GridTerminalSystem.GetBlockWithName(Config.DrillLcdPanelName) as IMyTextSurface ?? Me.GetSurface(0);

            if (!AssertConfig(pistons, drills, rotor, light)) return;

            TurnOff(light);

            var pistonsBlockList = new List<IMyPistonBase> { };
            var drillsBlockList = new List<IMyTerminalBlock> { };

            pistons.GetBlocksOfType(pistonsBlockList);
            drills.GetBlocksOfType(drillsBlockList);

            Echo("Pistons Found: " + pistonsBlockList.Count);
            Echo("Drill Found: " + drillsBlockList.Count);

            // select current piston (ordered by the number in the name)
            var sortedPistons = pistonsBlockList
                .Where((pistonBlock) => pistonBlock.CurrentPosition < pistonBlock.HighestPosition)
                .OrderBy((pistonBlock) => getPistonIndexFromName(pistonBlock.CustomName));
            var pistonCount = sortedPistons.Count();
            Echo($"Not Extended pistons: {pistonCount}");
            if (pistonCount == 0)
            {
                Echo("All pistons are fully extended! Exiting.");
                TurnOn(light);
                LogPistonPosition(null, pistonsBlockList);
                return;
            }
            var currentPiston = sortedPistons.First();
            LogPistonPosition(currentPiston, pistonsBlockList);

            var referenceInventory = drillsBlockList[0].GetInventory(0);
            Echo($"Reference inventory: {drillsBlockList[0].CustomName}");

            var fillRatio = GetReachableCargoFillRatio(referenceInventory);

            if (fillRatio > fillThreshold)
            {
                // storage is full, shut everything down
                drillsBlockList.ForEach((drillBlock) =>
                {
                    TurnOff(drillBlock);
                    TurnOff(rotor);
                });
            }
            else
            {
                drillsBlockList.ForEach((drillBlock) =>
                {
                    TurnOn(drillBlock);
                });
                TurnOn(rotor);
            }

            if (HasCompletedFullTurn(rotor))
            {
                if (nextTargetAngle == 0) nextTargetAngle = 3.14f;
                else nextTargetAngle = 0;
                TurnOn(light);
                IncreaseMaxDistanceAndMove(currentPiston);
            }
            else
            {
                TurnOff(light);
            }

            logDisplay.WriteText($"current angle: {rotor.Angle}\n");
            logDisplay.WriteText($"station capacity at {(fillRatio * 100).ToString("0.00")}%\n\n", true);

        }

        public void Save()
        {
            // Method intentionally left empty.
        }
    }
}
