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

namespace SpaceEngeneersQOL.DrillManagement
{
    partial class Trivella : MyGridProgram
    {
        private readonly float angleThreshold = 0.1f;
        private readonly float fillThreshold = 0.9f;
        private float nextTargetAngle = 0.0f;

        public Trivella()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        private float GetReachableCargoFillRatio(VRage.Game.ModAPI.Ingame.IMyInventory referenceInventory)
        {

            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock> { };
            GridTerminalSystem.GetBlocks(blocks);

            var blocksInTheGrid = blocks
                .Where((block) =>
                    block.IsSameConstructAs(Me) &&
                    block.GetInventory() != null &&
                    referenceInventory.IsConnectedTo(block.GetInventory())
                );

            var currentTotalVolume = 0.0f;
            var maxTotalVolume = 0.0f;

            foreach (var block in blocksInTheGrid)
            {
                var inventory = block.GetInventory();
                currentTotalVolume += ((float)inventory.CurrentVolume);
                maxTotalVolume += ((float)inventory.MaxVolume);
            }

            var totalFillRatio = currentTotalVolume / maxTotalVolume;

            return totalFillRatio;
        }

        private bool CheckExistence(Object entity, IMyTextPanel output, String entityName)
        {
            if (output == null)
            {
                Echo("no output for debugging");
                return false;
            }

            if (entity == null)
            {
                output.WriteText($"[color='#FF0000'] entity not found: {entityName}");

                return false;
            }

            return true;
        }

        private bool HasCompletedFullTurn(IMyMotorStator rotor)
        {
            return rotor.Angle < nextTargetAngle + angleThreshold && rotor.Angle >= nextTargetAngle - angleThreshold;
        }

        private bool TurnOn(IMyTerminalBlock entity)
        {
            if (entity != null)
            {
                entity.ApplyAction("OnOff_On");

                return true;
            }

            return false;
        }

        private bool TurnOff(IMyTerminalBlock entity)
        {
            if (entity != null)
            {
                entity.ApplyAction("OnOff_Off");

                return true;
            }

            return false;
        }

        private void IncreaseMaxDistanceAndMove(IMyPistonBase piston)
        {
            piston.ApplyAction("IncreaseUpperLimit");
            piston.ApplyAction("Extend");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            var pistons = GridTerminalSystem.GetBlockGroupWithName("Trivella.Pistons");
            var drills = GridTerminalSystem.GetBlockGroupWithName("Trivella.Drills");
            var rotor = GridTerminalSystem.GetBlockWithName("Trivella.Rotor") as IMyMotorStator;
            var light = GridTerminalSystem.GetBlockWithName("Trivella.DebugLight");
            var lcd = GridTerminalSystem.GetBlockWithName("Trivella.TextPanel") as IMyTextPanel;

            if (!CheckExistence(pistons, lcd, "Pistons Group")) return;
            if (!CheckExistence(drills, lcd, "Drills Group")) return;
            if (!CheckExistence(rotor, lcd, "Rotor")) return;
            if (!CheckExistence(light, lcd, "Light")) return;

            var pistonsBlockList = new List<IMyTerminalBlock> { };
            var drillsBlockList = new List<IMyTerminalBlock> { };

            pistons.GetBlocks(pistonsBlockList);
            drills.GetBlocks(drillsBlockList);

            lcd.SetValue("FontColor", Color.Green);

            // select current piston (ordered by the number in the name)
            var currentPiston = pistonsBlockList
                .Where((pistonBlock) => (pistonBlock as IMyPistonBase).MaxLimit < (pistonBlock as IMyPistonBase).HighestPosition)
                .OrderBy((pistonBlock) => float.Parse(pistonBlock.CustomName.Split(' ')[1]))
                .ToList()
                .First();
            var fillRatio = GetReachableCargoFillRatio(drillsBlockList[0].GetInventory());

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
                IncreaseMaxDistanceAndMove(currentPiston as IMyPistonBase);
            }
            else
            {
                TurnOff(light);
            }

            lcd.WriteText($"current angle: {rotor.Angle}\n");
            lcd.WriteText($"station capacity at {(fillRatio * 100).ToString("0.00")}%\n\n", true);

            pistonsBlockList.ForEach((pistonBlock) =>
            {
                lcd.WriteText($"{(pistonBlock.EntityId == currentPiston.EntityId ? "> " : "")}{pistonBlock.CustomName} at {(pistonBlock as IMyPistonBase).CurrentPosition} / {(pistonBlock as IMyPistonBase).MaxLimit} | {(pistonBlock as IMyPistonBase).HighestPosition}\n", true);
            });
        }

    }
}
