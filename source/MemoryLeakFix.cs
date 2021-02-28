using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using static BattletechPerformanceFix.Extensions;
using BattleTech;
using BattleTech.Analytics.Sim;
using BattleTech.Framework;
using BattleTech.Rendering.UrbanWarfare;
using BattleTech.Save;
using BattleTech.Save.Test;
using BattleTech.UI;
using Localize;

namespace BattletechPerformanceFix
{
    class MemoryLeakFix: Feature
    {
        private static Type self = typeof(MemoryLeakFix);

        public void Activate() {
            // fixes group 1: occurs on save file load
            // fix 1.1: allow the BattleTechSimAnalytics class to properly remove its message subscriptions
            "BeginSession".Transpile<BattleTechSimAnalytics>("Session_Transpile");
            "EndSession".Transpile<BattleTechSimAnalytics>("Session_Transpile");
            // fix 1.2: add a RemoveSubscriber() for a message type that never had one to begin with
            "OnSimGameInitializeComplete".Post<SimGameUXCreator>();
            // fix 1.3: remove OnLanguageChanged subscriptions for these objects, which never unsub and therefore leak.
            //          b/c the user must drop back to main menu to change the language, there's no reason
            //          to use these in the first place (objects are created in-game and never on the main menu)
            // Contract
            var contractCtorTypes = new Type[]{typeof(string), typeof(string), typeof(string), typeof(ContractTypeValue),
                                               typeof(GameInstance), typeof(ContractOverride), typeof(GameContext),
                                               typeof(bool), typeof(int), typeof(int), typeof(int)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(Contract), contractCtorTypes),
                               null, null, new HarmonyMethod(self, "Contract_ctor_Transpile"));
            "PostDeserialize".Transpile<Contract>();
            // ContractObjectiveOverride
            Main.harmony.Patch(AccessTools.Constructor(typeof(ContractObjectiveOverride), new Type[]{}),
                               null, null, new HarmonyMethod(self, "ContractObjectiveOverride_ctor_Transpile"));
            var cooCtorTypes = new Type[]{typeof(ContractObjectiveGameLogic)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(ContractObjectiveOverride), cooCtorTypes),
                               null, null, new HarmonyMethod(self, "ContractObjectiveOverride_ctor_cogl_Transpile"));
            // ObjectiveOverride
            Main.harmony.Patch(AccessTools.Constructor(typeof(ObjectiveOverride), new Type[]{}),
                               null, null, new HarmonyMethod(self, "ObjectiveOverride_ctor_Transpile"));
            var ooCtorTypes = new Type[]{typeof(ObjectiveGameLogic)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(ObjectiveOverride), ooCtorTypes),
                               null, null, new HarmonyMethod(self, "ObjectiveOverride_ctor_ogl_Transpile"));
            // DialogueContentOverride
            Main.harmony.Patch(AccessTools.Constructor(typeof(DialogueContentOverride), new Type[]{}),
                               null, null, new HarmonyMethod(self, "DialogueContentOverride_ctor_Transpile"));
            var dcoCtorTypes = new Type[]{typeof(DialogueContent)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(DialogueContentOverride), dcoCtorTypes),
                               null, null, new HarmonyMethod(self, "DialogueContentOverride_ctor_dc_Transpile"));
            // InterpolatedText
            "Init".Transpile<InterpolatedText>();
            // these finalizers could never run to begin with, and they only did RemoveSubscriber; nop them
            "Finalize".Transpile<Contract>("TranspileNopAll");
            "Finalize".Transpile<ContractObjectiveOverride>("TranspileNopAll");
            "Finalize".Transpile<ObjectiveOverride>("TranspileNopAll");
            "Finalize".Transpile<DialogueContentOverride>("TranspileNopAll");
            "Finalize".Transpile<InterpolatedText>("TranspileNopAll");

            // fixes group 2: occurs on entering/exiting a contract
            // fix 2.1: none of these classes need to store a CombatGameState
            "ContractInitialize".Post<DialogueContent>("DialogueContent_ContractInitialize_Post");
            "ContractInitialize".Post<ConversationContent>("ConversationContent_ContractInitialize_Post");
            "ContractInitialize".Post<DialogBucketDef>("DialogBucketDef_ContractInitialize_Post");
            // fix 2.2: SimGameState._selectedContract is unused once a contract is complete
            //          (with one exception: a consecutive deployment needs it)
            "ClearActiveFlashpoint".Post<SimGameState>();
            "ResolveCompleteContract".Post<SimGameState>();

            // fixes group 3: occurs on creating a new savefile
            // fix 3.1: clean up the GameInstanceSave.references after serialization is complete
            "PostSerialization".Post<GameInstanceSave>();

            // fixes group 4: occurs on exiting combat
            // fix 4.1: there's quite a lot of state left uncleared post-combat, much of it persists because of
            //          the pooling of GameObjects. curiously, methods are provided to do cleanup, but
            //          what cleanup is done in the original code is very incomplete. these methods extend
            //          the existing cleanup process to clear much more memory
            "OnCombatGameDestroyed".Post<AbstractActor>("AA_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Pre<CombatGameState>("CGS_OnCombatGameDestroyed_Pre");
            "OnCombatGameDestroyed".Post<CombatGameState>("CGS_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUD>("CHUD_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDActorInfo>("CHUDAI_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDFloatieAnchor>("CHUDFA_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDInWorldElementMgr>("CHUDIWEM_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDInWorldScalingActorInfo>("CHUDIWSAI_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDMechwarriorTray>("CHUDMT_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDObjectiveItem>("CHUDOI_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDObjectivesList>("CHUDOL_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDPhaseTrack>("CHUDPT_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDPortrait>("CHUDP_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDRegionFlag>("CHUDRF_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatHUDWeaponPanel>("CHUDWP_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<CombatSelectionHandler>("CSH_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<GameRepresentation>("GR_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<ObjectiveBeacon>("OB_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<Team>("T_OnCombatGameDestroyed_Post");
            "OnCombatGameDestroyed".Post<TurnEventNotification>("TEN_OnCombatGameDestroyed_Post");
            "OnCombatSceneEnd".Post<VFXCullingController>();
            "OnDestroy".Post<EncounterLayerParent>("ELP_OnDestroy_Post");

            // fixes group 5: occurs on exiting salvage screen
            // fix 5.1: much like 4.1 above, except for objects that are pooled after exiting the AAR screen
            "OnPooled".Post<MissionResults>("MR_OnPooled_Post");
            "OnPooled".Post<SGDialogWidget>("SGDW_OnPooled_Post");
            "Pool".Pre<ListElementController_BASE_NotListView>("LECBNLV_Pool_Pre");
        }

        private static IEnumerable<CodeInstruction> Session_Transpile(IEnumerable<CodeInstruction> ins)
        {
            var meth = AccessTools.Method(self, "_UpdateMessageSubscriptions");
            return TranspileReplaceCall(ins, "UpdateMessageSubscriptions", meth);
        }

        private static void _UpdateMessageSubscriptions(BattleTechSimAnalytics __instance, bool subscribe)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var mc = __instance.messageCenter;
            if (mc != null) {
                mc.Subscribe(MessageCenterMessageType.OnReportMechwarriorSkillUp,
                             new ReceiveMessageCenterMessage(__instance.ReportMechWarriorSkilledUp), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnReportMechwarriorHired,
                             new ReceiveMessageCenterMessage(__instance.ReportMechWarriorHired), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnReportMechWarriorKilled,
                             new ReceiveMessageCenterMessage(__instance.ReportMechWarriorKilled), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnReportShipUpgradePurchased,
                             new ReceiveMessageCenterMessage(__instance.ReportShipUpgradePurchased), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnSimGameContractComplete,
                             new ReceiveMessageCenterMessage(__instance.ReportContractComplete), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnSimRoomStateChanged,
                             new ReceiveMessageCenterMessage(__instance.ReportSimGameRoomChange), subscribe);
            }
        }

        private static void OnSimGameInitializeComplete_Post(SimGameUXCreator __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.sim.MessageCenter.RemoveSubscriber(
                    MessageCenterMessageType.OnSimGameInitialized,
                    new ReceiveMessageCenterMessage(__instance.OnSimGameInitializeComplete));
        }

        private static IEnumerable<CodeInstruction> Contract_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 125, 134);
        }

        private static IEnumerable<CodeInstruction> PostDeserialize_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 21, 27);
        }

        private static IEnumerable<CodeInstruction>
        ContractObjectiveOverride_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 5, 14);
        }

        private static IEnumerable<CodeInstruction>
        ContractObjectiveOverride_ctor_cogl_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 9, 18);
        }

        private static IEnumerable<CodeInstruction>
        ObjectiveOverride_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 8, 17);
        }

        private static IEnumerable<CodeInstruction>
        ObjectiveOverride_ctor_ogl_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 12, 21);
        }

        private static IEnumerable<CodeInstruction>
        DialogueContentOverride_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 23, 32);
        }

        private static IEnumerable<CodeInstruction>
        DialogueContentOverride_ctor_dc_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 60, 69);
        }

        private static IEnumerable<CodeInstruction> Init_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 3, 10);
        }

        private static IEnumerable<CodeInstruction>
        TranspileNopIndicesRange(IEnumerable<CodeInstruction> ins, int startIndex, int endIndex)
        {
            LogDebug($"TranspileNopIndicesRange: nopping instructions at indices {startIndex}-{endIndex}");
            if (endIndex < startIndex || startIndex < 0) {
                LogError($"TranspileNopIndicesRange: invalid use with startIndex = {startIndex}," +
                         $" endIndex = {endIndex} (transpiled method remains unmodified)");
                return ins;
            }

            var code = ins.ToList();
            try {
                for (int i = startIndex; i <= endIndex; i++) {
                    code[i].opcode = OpCodes.Nop;
                    code[i].operand = null;
                }
                return code.AsEnumerable();
            } catch (ArgumentOutOfRangeException ex) {
                LogError($"TranspileNopIndicesRange: {ex.Message} (transpiled method remains unmodified)");
                return ins;
            }
        }

        private static IEnumerable<CodeInstruction>
        TranspileReplaceCall(IEnumerable<CodeInstruction> ins, string originalMethodName,
                             MethodInfo replacementMethod)
        {
            LogInfo($"TranspileReplaceCall: {originalMethodName} -> {replacementMethod.ToString()}");
            return ins.SelectMany(i => {
                if (i.opcode == OpCodes.Call &&
                   (i.operand as MethodInfo).Name.StartsWith(originalMethodName)) {
                    i.operand = replacementMethod;
                }
                return Sequence(i);
            });
        }

        private static IEnumerable<CodeInstruction> TranspileNopAll(IEnumerable<CodeInstruction> ins)
        {
            return ins.SelectMany(i => {
                i.opcode = OpCodes.Nop;
                i.operand = null;
                return Sequence(i);
            });
        }

        private static void DialogueContent_ContractInitialize_Post(DialogueContent __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void ConversationContent_ContractInitialize_Post(ConversationContent __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void DialogBucketDef_ContractInitialize_Post(DialogBucketDef __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void PostSerialization_Post(GameInstanceSave __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.references = new SerializableReferenceContainer("the one and only");
            __instance.SimGameSave.GlobalReferences = __instance.references;
        }

        private static void ClearActiveFlashpoint_Post(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.SetSelectedContract(null);
        }

        private static void ResolveCompleteContract_Post(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.activeFlashpoint == null) {
                __instance.SetSelectedContract(null);
            }
        }

        private static void AA_OnCombatGameDestroyed_Post(AbstractActor __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.allComponents = null;
            __instance.ammoBoxes = null;
            __instance.jumpjets = null;
            __instance.occupiedEncounterLayerCells = null;
            __instance.weapons = null;
            __instance.BehaviorTree = null;
            __instance.Combat = null;
            __instance.ImaginaryLaserWeapon = null;
            __instance.JumpPathing = null;
            __instance.Pathing = null;
            __instance.VisibilityCache = null;
            __instance._lance = null;

            var mech = __instance as Mech;
            if (mech != null) {
                mech.miscComponents = null;
                mech.DFAWeapon = null;
                mech.MeleeWeapon = null;
                mech.pilot = null;
            }

            var vee = __instance as Vehicle;
            if (vee != null) {
                vee.miscComponents = null;
                vee.pilot = null;
            }

            var turr = __instance as Turret;
            if (turr != null) {
                turr.pilot = null;
            }
        }

        private static void CGS_OnCombatGameDestroyed_Pre(CombatGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var ogls = __instance.ItemRegistry.GetObjectsOfType(TaggedObjectType.ObstructionGameLogic);
            foreach (var ogl in ogls.Cast<ObstructionGameLogic>()) {
                ogl.Combat = null;
                ogl.occupiedCells = null;
            }
        }

        private static void CGS_OnCombatGameDestroyed_Post(CombatGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.dropshipSpawns = null;
            __instance.MapMetaData = null;
        }

        private static void CHUD_OnCombatGameDestroyed_Post(CombatHUD __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.AttackModeSelector = null;
            __instance.CalledShotPopUp = null;
            __instance.CombatChatModule = null;
            __instance.Combat = null;
            __instance.DialogWidget = null;
            __instance.DialogSideStack = null;
            __instance.FiringPreview = null;
            __instance.InWorldMgr = null;
            __instance.MechTray = null;
            __instance.MechWarriorTray = null;
            __instance.MissionEndScreen = null;
            __instance.MultiplayerHUD = null;
            __instance.ObjectivesList = null;
            __instance.ObjectiveStatusNotify = null;
            __instance.PhaseTrack = null;
            __instance.RetreatEscMenu = null;
            __instance.SidePanel = null;
            __instance.TargetingComputer = null;
            __instance.TurnEventNotification = null;
            __instance.WeaponPanel = null;
            __instance.selectedUnit = null;
        }

        private static void CHUDAI_OnCombatGameDestroyed_Post(CombatHUDActorInfo __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;
            __instance.HeatDisplay = null;
            __instance.InspiredDisplay = null;
            __instance.MarkDisplay = null;
            __instance.PhaseDisplay = null;
            __instance.StabilityDisplay = null;
        }

        private static void CHUDFA_OnCombatGameDestroyed_Post(CombatHUDFloatieAnchor __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;
        }

        private static void CHUDIWEM_OnCombatGameDestroyed_Post(CombatHUDInWorldElementMgr __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.AttackDirectionIndicators = null;
            __instance.AuraReticles = null;
            __instance.FloatieAnchors = null;
            __instance.FloatieStacks = null;
            __instance.NumFlags = null;
            __instance.RegionFlags = null;
            __instance.WeaponTickMarks = null;
            __instance.combat = null;
            __instance.genericFloatie = null;
            __instance.HUD = null;
        }

        private static void CHUDIWSAI_OnCombatGameDestroyed_Post(CombatHUDInWorldScalingActorInfo __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;
            var numFlagHex = __instance as CombatHUDNumFlagHex;
            if (numFlagHex != null) {
                numFlagHex.ActorInfo = null;
            }
        }

        private static void CHUDMT_OnCombatGameDestroyed_Post(CombatHUDMechwarriorTray __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;
            __instance.AbilityButtons = null;
            __instance.ActionButtons = null;
            __instance.CommandButton = null;
            __instance.DoneWithMechButton = null;
            __instance.EjectButton = null;
            __instance.EquipmentButton = null;
            __instance.FireButton = null;
            __instance.JumpButton = null;
            __instance.MoraleButtons = null;
            __instance.MoveButton = null;
            __instance.RestartButton = null;
            __instance.SprintButton = null;
        }

        private static void CHUDOI_OnCombatGameDestroyed_Post(CombatHUDObjectiveItem __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void CHUDOL_OnCombatGameDestroyed_Post(CombatHUDObjectivesList __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void CHUDPT_OnCombatGameDestroyed_Post(CombatHUDPhaseTrack __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.IconTrackers = null;
            __instance.Combat = null;
            __instance.phaseTimeClock = null;
            __instance.retreatButton = null;
        }

        private static void CHUDP_OnCombatGameDestroyed_Post(CombatHUDPortrait __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;
            __instance.inspireButton = null;
        }

        private static void CHUDRF_OnCombatGameDestroyed_Post(CombatHUDRegionFlag __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.myRegion = null;
        }

        private static void CHUDWP_OnCombatGameDestroyed_Post(CombatHUDWeaponPanel __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.EquipmentSlots = null;
            __instance.WeaponSlots = null;
            __instance.sortedWeaponsList = null;
            __instance.sortedEquipmentAbilityList = null;
            __instance.Combat = null;
            __instance.dfaSlot = null;
            __instance.displayedActor = null;
            __instance.meleeSlot = null;
        }

        private static void CSH_OnCombatGameDestroyed_Post(CombatSelectionHandler __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.SelectionStack = null;
        }

        private static void GR_OnCombatGameDestroyed_Post(GameRepresentation __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;

            var weapRep = __instance as WeaponRepresentation;
            if (weapRep != null) {
                weapRep.weaponEffect = null;
            }

            var jumpRep = __instance as JumpjetRepresentation;
            if (jumpRep != null) {
                jumpRep.Combat = null;
            }
        }

        private static void OB_OnCombatGameDestroyed_Post(ObjectiveBeacon __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;
        }

        private static void T_OnCombatGameDestroyed_Post(Team __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
            __instance.VisibilityCache = null;
            __instance.ActivationSequence = null;
            __instance.lances = null;
            if (__instance.miscCombatants != null) {
                foreach (var combatant in __instance.miscCombatants) {
                    var bldg = combatant as BattleTech.Building;
                    if (bldg != null) {
                        bldg.Combat = null;
                    }
                }
                __instance.miscCombatants = null;
            }
            __instance.SupportTeam = null;
        }

        private static void TEN_OnCombatGameDestroyed_Post(TurnEventNotification __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.Combat = null;
        }

        private static void OnCombatSceneEnd_Post(VFXCullingController __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.cachedUrbanBuildings = null;
        }

        private static void ELP_OnDestroy_Post(EncounterLayerParent __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var eogls = __instance.GetComponentsInChildren<EncounterObjectGameLogic>(true);
            foreach(var eogl in eogls) {
                eogl.Combat = null;
            }

            var elds = __instance.GetComponentsInChildren<EncounterLayerData>(true);
            foreach (var eld in elds) {
                eld.responseGroup = null;
                eld.mapEncounterLayerDataCells = null;
                eld.lanceSpawnerList = null;
                eld.unitSpawnPointList = null;
                eld.inclineMeshData = null;
            }

            var cogls = __instance.GetComponentsInChildren<ContractObjectiveGameLogic>(true);
            foreach (var cogl in cogls) {
                cogl.objectiveRefList = null;
            }

            var rgls = __instance.GetComponentsInChildren<RegionGameLogic>(true);
            foreach (var rgl in rgls) {
                rgl.mapEncounterLayerDataCellList = null;
                rgl.objectiveRefList = null;
            }

            MapMetaData.invalidCell.mapMetaData = null;
        }

        private static void MR_OnPooled_Post(MissionResults __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.contract = null;
            __instance.simState = null;
            __instance.dm = null;
            __instance.missionResultsHeader = null;
            __instance.unitsScreen = null;
        }

        private static void SGDW_OnPooled_Post(SGDialogWidget __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.onContinueCallBack = null;
        }

        private static void LECBNLV_Pool_Pre(ListElementController_BASE_NotListView __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.ItemWidget.dropParent = null;
        }
    }
}
// vim: ts=4:sw=4
