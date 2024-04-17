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
    private readonly string PistonGroupName = "Trivella.Pistons";
    private readonly string DrillsGroupName = "Trivella.Drills";
    private readonly string DrillRotorName = "Trivella.Rotor";
    private readonly string DrillDebugLightName = "Trivella.DebugLight";
    /* 
     * Pannello LCD su cui scrivere lo stato di avanzamento:
     * se non lo trova usa il pannello principale del blocco program
     */
    private readonly string DrillLcdPanelName = "Trivella.TextPanel";
    /* 
     * Funzione che restituisce un indice a partire dal nome del pistone,
     * questo indice influenzerà l'ordine di estensione dei pistoni
     */
    private readonly Func<string, int> getPistonIndexFromName = (string text) =>
    {
      var match = new System.Text.RegularExpressions.Regex(@"^.*-(?<index>\d+)$").Match(text);
      return match.Success ? int.Parse(match.Groups["index"].Value) : 0;
    };

    private readonly float angleThreshold = 0.1f;
    private readonly float fillThreshold = 0.9f;
    private float nextTargetAngle = 0.0f;
    public Program()
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
              referenceInventory != null &&
              referenceInventory.IsConnectedTo(block.GetInventory())
          );

      var currentTotalVolume = 0.0f;
      var maxTotalVolume = 0.0f;

      foreach (var block in blocksInTheGrid)
      {
        var inventory = block.GetInventory();
        currentTotalVolume += (float)inventory.CurrentVolume;
        maxTotalVolume += (float)inventory.MaxVolume;
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

    private void LogPistonPosition(IMyTextPanel lcd, IMyPistonBase current, List<IMyPistonBase> pistons)
    {
      pistons.ForEach((piston) =>{
        var currentFlag = current.EntityId == piston.EntityId ? "> " : "";
        lcd.WriteText($"{currentFlag}{piston.CustomName} at {piston.CurrentPosition} / {piston.MaxLimit} | {piston.HighestPosition}\n", true);
      });
    }
    public void Main(string argument, UpdateType updateSource)
    {
      var pistons = GridTerminalSystem.GetBlockGroupWithName(PistonGroupName);
      var drills = GridTerminalSystem.GetBlockGroupWithName(DrillsGroupName);
      var rotor = GridTerminalSystem.GetBlockWithName(DrillRotorName) as IMyMotorStator;
      var light = GridTerminalSystem.GetBlockWithName(DrillDebugLightName);
      var lcd = GridTerminalSystem.GetBlockWithName(DrillLcdPanelName) as IMyTextPanel ?? Me.GetSurface(0) as IMyTextPanel;

      if (!CheckExistence(pistons, lcd, "Pistons Group")) return;
      if (!CheckExistence(drills, lcd, "Drills Group")) return;
      if (!CheckExistence(rotor, lcd, "Rotor")) return;
      if (!CheckExistence(light, lcd, "Light")) return;

      var pistonsBlockList = new List<IMyPistonBase> { };
      var drillsBlockList = new List<IMyTerminalBlock> { };

      pistons.GetBlocksOfType(pistonsBlockList);
      drills.GetBlocksOfType(drillsBlockList);

      Echo("Pistons Found: " + pistonsBlockList.Count);
      Echo("Drill Found: " + drillsBlockList.Count);

      lcd.SetValue("FontColor", Color.Green);

      // select current piston (ordered by the number in the name)
      var sortedPistons = pistonsBlockList
          .Where((pistonBlock) => pistonBlock.MaxLimit < pistonBlock.HighestPosition)
          .OrderBy((pistonBlock) => getPistonIndexFromName(pistonBlock.CustomName))
          .ToList();
      Echo("Not Extended pistons: " + sortedPistons.Count);
      if (sortedPistons.Count == 0)
      {
        Echo("All pistons are fully extended! Exiting.");
        return;
      }
      var currentPiston = sortedPistons[0];

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
      Echo("I am here");

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

      lcd.WriteText($"current angle: {rotor.Angle}\n");
      lcd.WriteText($"station capacity at {(fillRatio * 100).ToString("0.00")}%\n\n", true);

      LogPistonPosition(lcd, currentPiston, pistonsBlockList);
    }

    public void Save()
    {
      // Method intentionally left empty.
    }
  }
}
