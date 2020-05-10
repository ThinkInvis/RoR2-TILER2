using UnityEngine;
using RoR2;
using BepInEx;
using System;
using System.Reflection;
using System.Linq;
using static TILER2.MiscUtil;
using R2API.Utils;
using R2API;
using UnityEngine.Networking;

namespace TILER2 {
    
    [BepInDependency("com.bepis.r2api")]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "1.0.0";
        public const string ModName = "TILER2";
        public const string ModGuid = "com.ThinkInvisible.TILER2";
        
        internal static Type nodeRefType;
        internal static Type nodeRefTypeArr;

        internal static FilingDictionary<ItemBoilerplate> masterItemList = new FilingDictionary<ItemBoilerplate>();

        private static readonly RoR2.ConVar.BoolConVar allowClientAICSet = new RoR2.ConVar.BoolConVar("AIC_AllowClientSet", ConVarFlags.SenderMustBeServer, "false", "If true, clients may use AIC_Set to TEMPORARILY set config values on the server. If false, AIC_Set will not work for clients.");

        private static void EnsureOrchestrator() {
            if(!NetworkServer.active) {
                Debug.LogError("TILER2: EnsureOrchestrator called on client");
            }
            if(!netOrchestrator) {
                netOrchestrator = UnityEngine.Object.Instantiate(netOrchPrefab);
                NetworkServer.Spawn(netOrchestrator);
            }
        }

        [ConCommand(commandName = "AIC_Set", flags = ConVarFlags.ExecuteOnServer, helpText = "Temporarily override an ingame value managed by TILER2.AutoItemConfig. This will last until the end of the run. If called on the server, this will be permanent (and write to the config file) instead.")]
        public static void ConCmdAICSet(ConCommandArgs args) {
            EnsureOrchestrator();

            if(!args.sender.isServer && !allowClientAICSet.value == false) {
                Debug.LogWarning("TILER2: Client " + args.sender.userName + " tried to use ConCmd AIC_Set, but ConVar AIC_AllowClientSet is set to false. DO NOT CHANGE AIC_AllowClientSet to true unless you trust everyone who is in or may later join the server; doing so will allow them to temporarily change some config settings!");
                NetOrchestrator.SendConMsg(args.sender, "TILER2: ConCmd AIC_Set cannot be used on this server by anyone other than the host.", 1);
                return;
            }

            var wrongArgsPre = "TILER2: ConCmd AIC_Set was used with bad arguments (";
            var usagePost = ").\nUsage: AIC_Set path newValue. Argument 'path' matches \"Config Name\", AIC_Set \"Config Category/Config Name\", or AIC_Set \"Mod Name/Config Category/Config Name\".";

            if(args.Count < 2) {
                NetOrchestrator.SendConMsg(args.sender, wrongArgsPre + "not enough arguments" + usagePost, 1);
                return;
            }
            if(args.Count > 2) {
                NetOrchestrator.SendConMsg(args.sender, wrongArgsPre + "too many arguments" + usagePost, 1);
                return;
            }
            if(args[0].Length == 0) {
                NetOrchestrator.SendConMsg(args.sender, wrongArgsPre + "argument 1 'path' cannot be empty" + usagePost, 1);
                return;
            }
            var pathParts = args[0].Split('/').Reverse().ToArray();
            if(pathParts.Length > 3) {
                NetOrchestrator.SendConMsg(args.sender, wrongArgsPre + "argument 1 'path' is too long" + usagePost, 1);
                return;
            }
            
            //try case insensitive first
            var matches = AutoItemConfig.instances.FindAll(x => {
                return x.allowConCmd
                && x.configEntry.Definition.Key.ToUpper() == pathParts[0].ToUpper()
                && (pathParts.Length < 2 || x.configEntry.Definition.Section.ToUpper() == pathParts[1].ToUpper())
                && (pathParts.Length < 3 || x.modName.ToUpper() == pathParts[2].ToUpper());
            });
            
            if(matches.Count == 0) {
                NetOrchestrator.SendConMsg(args.sender, "TILER2: ConCmd AIC_Set did not find any matches for argument 1 'path'. Use ConCmd AIC_List (NOT YET IMPLEMENTED) to find valid targets.", 1); //TODO
                return;
            }
            
            var multimatchPre = "TILER2: ConCmd AIC_Set found multiple case-insensitive matches but no case-sensitive matches; you need to use a more specific argument 1 'path'.\n";
            
            if(matches.Count > 1) {
                var matchesCaseSensitive = AutoItemConfig.instances.FindAll(x => {
                    return x.allowConCmd
                    && x.configEntry.Definition.Key == pathParts[0]
                    && (pathParts.Length < 2 || x.configEntry.Definition.Section == pathParts[1])
                    && (pathParts.Length < 3 || x.modName == pathParts[2]);
                });
                
                if(matchesCaseSensitive.Count > 0) {
                    multimatchPre = "TILER2: ConCmd AIC_Set found multiple case-sensitive matches; you need to use a more specific argument 1 'path'.\n";
                    matches = matchesCaseSensitive;
                }
            }

            string matchPath = matches[0].modName + "/" + matches[0].configEntry.Definition.Section + "/" + matches[0].configEntry.Definition.Key;

            if(matches.Count > 1) {
                if(pathParts.Length == 1) {
                    NetOrchestrator.SendConMsg(args.sender, multimatchPre + "The following categories have that config name: " + String.Join(", ", matches.Select(x => x.modName + "/" + x.configEntry.Definition.Section)), 1);
                } else if(pathParts.Length == 2) {
                    NetOrchestrator.SendConMsg(args.sender, multimatchPre + "The following mods have that config category-name combination: " + String.Join(", ", matches.Select(x => x.modName)), 1);
                } else {
                    var errStr = "TILER2: There are multiple complete config entry definitions with the path " + matchPath + "; this should never happen! Please report this as a bug.";
                    NetOrchestrator.SendConMsg(args.sender, errStr, 2);
                    Debug.LogError(errStr);
                }
                return;
            }

            object convObj;
            try {
                convObj = BepInEx.Configuration.TomlTypeConverter.ConvertToValue(args[1], matches[0].propType);
            } catch(InvalidOperationException) {
                NetOrchestrator.SendConMsg(args.sender, "TILER2: ConCmd AIC_Set can't convert argument 2 'newValue' to the target config type (" + matches[0].propType.Name + ").", 1);
                return;
            }

            matches[0].runDeferOnce = true;
            matches[0].UpdateProperty(convObj);
            if(!args.sender.isServer) Debug.Log("TILER2: ConCmd AIC_Set from client " + args.sender.userName + " passed. Changed config setting " + matchPath + " to " + args[1]);
            NetOrchestrator.SendConMsg(args.sender, "TILER2: ConCmd AIC_Set successfully updated config entry!", 0);
        }

        private static GameObject netOrchPrefab;
        internal static GameObject netOrchestrator;

        public void Awake() {
            //this doesn't seem to fire until the title screen is up, which is good because config file changes shouldn't immediately be read during startup; watch for regression (or just implement a check anyways?)
            On.RoR2.RoR2Application.Update += AutoItemConfigContainer.FilePollUpdateHook;
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;

            CommandHelper.AddToConsoleWhenReady();

            nodeRefType = typeof(DirectorCore).GetNestedTypes(BindingFlags.NonPublic).First(t=>t.Name == "NodeReference");
            nodeRefTypeArr = nodeRefType.MakeArrayType();

            var netOrchPrefabPrefab = new GameObject("TILER2NetOrchestratorPrefabPrefab");
            netOrchPrefabPrefab.AddComponent<NetworkIdentity>();
            netOrchPrefab = netOrchPrefabPrefab.InstantiateClone("TILER2NetOrchestratorPrefab");
            netOrchPrefab.AddComponent<NetOrchestrator>();

            On.RoR2.Run.BuildDropTable += (orig, self) => {
                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(!bpl.enabled) {
                        Debug.Log("Removing " + bpl.itemCodeName);
                        if(bpl is Equipment) self.availableEquipment.RemoveEquipment(((Equipment)bpl).regIndex);
                        else if(bpl is Item) self.availableItems.RemoveItem(((Item)bpl).regIndex);
                    }
                }
                orig(self);
                var pickerOptions = typeof(PickupPickerController).GetFieldCached("options");
                foreach(var picker in FindObjectsOfType<PickupPickerController>()) {
                    var oldOpt = ((PickupPickerController.Option[])pickerOptions.GetValue(picker))[0];
                    picker.SetOptionsFromPickupForCommandArtifact(oldOpt.pickupIndex);
                }
                //TODO: reroll (removed) items in choice boxes
            };
            
            On.RoR2.Run.OnDisable += (orig, self) => {
                orig(self);
                AutoItemConfig.CleanupDirty(true);
            };
            On.RoR2.Run.EndStage += (orig, self) => {
                orig(self);
                AutoItemConfig.CleanupDirty(false);
            };
            
            /*On.RoR2.RuleBook.GenerateItemMask += (orig, self) => {
                var retv = orig(self);

                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(bpl.enabled || !(bpl is Item)) continue;
                    Debug.Log("Removing: " + bpl);
                    retv.RemoveItem(((Item)bpl).regIndex);
                }

                return retv;
            };
            On.RoR2.RuleBook.GenerateEquipmentMask += (orig, self) => {
                var retv = orig(self);

                foreach(ItemBoilerplate bpl in masterItemList) {
                    if(bpl.enabled || !(bpl is Equipment)) continue;
                    retv.RemoveEquipment(((Equipment)bpl).regIndex);
                }

                return retv;
            };*/
        }
        
        private void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
            orig();

            foreach(ItemBoilerplate bpl in masterItemList) {
                PickupIndex pind;
                if(bpl is Equipment) pind = PickupCatalog.FindPickupIndex(((Equipment)bpl).regIndex);
                else if(bpl is Item) pind = PickupCatalog.FindPickupIndex(((Item)bpl).regIndex);
                else continue;
                var pickup = PickupCatalog.GetPickupDef(pind);

                bpl.pickupDef = pickup;
                bpl.pickupIndex = pind;
            }
        }

        private RoR2.UI.LogBook.Entry[] On_LogbookBuildPickupEntries(On.RoR2.UI.LogBook.LogBookController.orig_BuildPickupEntries orig) {
            var retv = orig();
            var bplsLeft = masterItemList.ToList();
            foreach(var entry in retv) {
                if(!(entry.extraData is PickupIndex)) continue;
                ItemBoilerplate matchedBpl = null;
                foreach(ItemBoilerplate bpl in bplsLeft) {
                    if((PickupIndex)entry.extraData == bpl.pickupIndex) {
                        matchedBpl = bpl;
                        break;
                    }
                }
                if(matchedBpl != null) {
                    matchedBpl.logbookEntry = entry;
                    bplsLeft.Remove(matchedBpl);
                }
            }
            return retv;
        }
    }

    internal class NetOrchestrator : NetworkBehaviour {
        private static NetOrchestrator instance;
        private void Awake() {
            instance = this;
        }
        internal static void SendConMsg(NetworkUser user, string msg, int severity = 0) {
            instance.TargetConMsg(user.connectionToClient, msg, severity);
        }
        [TargetRpc]
        private void TargetConMsg(NetworkConnection target, string msg, int severity) {
            if(severity == 2)
                Debug.LogError(msg);
            else if(severity == 1)
                Debug.LogWarning(msg);
            else
                Debug.Log(msg);
        }
    }
}