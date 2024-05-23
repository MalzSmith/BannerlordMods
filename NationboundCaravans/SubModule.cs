using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;


namespace NationboundCaravans;

public class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();

        var harmony = new Harmony("NationboundCaravan");

        var originalMethod = typeof(CaravansCampaignBehavior).GetMethod("FindNextDestinationForCaravan",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Replace the method that decides the next caravan destination
        harmony.Patch(originalMethod, new(Patches.Prefix));
    }
}

public static class Patches
{
    private static Dictionary<Hero, List<IFaction>> AllowedFactions = new();
    
    // _caravanLastHomeTownVisitTime
    private static readonly FieldInfo CaravanLastHomeTownVisitTimeField;

    // _previouslyChangedCaravanTargetsDueToEnemyOnWay
    private static readonly FieldInfo PreviouslyChangedCaravanTargetsDueToEnemyOnWayField;

    // GetTradeScoreForTown
    private static readonly MethodInfo GetTradeScoreForTownMethod;

    private static float GetTradeScoreForTown2(this CaravansCampaignBehavior instance,
        MobileParty caravanParty,
        Town town,
        CampaignTime lastHomeVisitTimeOfCaravan,
        float caravanFullness,
        bool distanceCut)
    {
        return (float)GetTradeScoreForTownMethod.Invoke(instance,
            [caravanParty, town, lastHomeVisitTimeOfCaravan, caravanFullness, distanceCut]);
    }

    static Patches()
    {
        CaravanLastHomeTownVisitTimeField = typeof(CaravansCampaignBehavior).GetField("_caravanLastHomeTownVisitTime",
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        PreviouslyChangedCaravanTargetsDueToEnemyOnWayField = typeof(CaravansCampaignBehavior).GetField(
            "_previouslyChangedCaravanTargetsDueToEnemyOnWay", BindingFlags.NonPublic | BindingFlags.Instance);
        
        GetTradeScoreForTownMethod = typeof(CaravansCampaignBehavior).GetMethod("GetTradeScoreForTown",
            BindingFlags.NonPublic | BindingFlags.Instance);
    }

    // ReSharper disable InconsistentNaming
    /// <summary>
    /// Pretty much the original code, except it limits the towns to the ones which are in the allowed factions if there are any limitations
    /// </summary>
    public static bool Prefix(MobileParty caravanParty, bool distanceCut, ref Town __result,
        CaravansCampaignBehavior __instance)
    {
        if (!caravanParty.Owner.IsHumanPlayerCharacter)
        {
            return true;
        }
        
        // Get used fields with reflection
        var _caravanLastHomeTownVisitTime =
            (Dictionary<MobileParty, CampaignTime>)CaravanLastHomeTownVisitTimeField.GetValue(__instance);

        var _previouslyChangedCaravanTargetsDueToEnemyOnWay =
            (Dictionary<MobileParty, List<Settlement>>)PreviouslyChangedCaravanTargetsDueToEnemyOnWayField
                .GetValue(__instance);

        float bestTradeScore = 0.0f;
        Town destinationForCaravan = null;
        float caravanFullness = caravanParty.ItemRoster.TotalWeight / (float)caravanParty.InventoryCapacity;

        _caravanLastHomeTownVisitTime.TryGetValue(caravanParty, out var lastHomeVisitTimeOfCaravan);
        foreach (var allTown in Town.AllTowns)
        {
            if (allTown.Owner.Settlement != caravanParty.CurrentSettlement
                && !allTown.IsUnderSiege
                && !allTown.MapFaction.IsAtWarWith(caravanParty.MapFaction)
                && (!allTown.Settlement.Parties.Contains(MobileParty.MainParty)
                    || !MobileParty.MainParty.MapFaction.IsAtWarWith(caravanParty.MapFaction))
                && !_previouslyChangedCaravanTargetsDueToEnemyOnWay[caravanParty].Contains(allTown.Settlement))
            {
                float tradeScoreForTown = __instance.GetTradeScoreForTown2(caravanParty, allTown,
                    lastHomeVisitTimeOfCaravan, caravanFullness, distanceCut);

                if (tradeScoreForTown > bestTradeScore)
                {
                    bestTradeScore = tradeScoreForTown;
                    destinationForCaravan = allTown;
                }
            }
        }

        __result = destinationForCaravan;
        return false;
    }

    // ReSharper enable InconsistentNaming
}