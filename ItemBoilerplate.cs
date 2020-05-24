using BepInEx.Configuration;
using R2API;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using static TILER2.MiscUtil;

namespace TILER2 {
    internal static class ItemBoilerplateModule {
        internal static FilingDictionary<ItemBoilerplate> masterItemList = new FilingDictionary<ItemBoilerplate>();
        private static bool itemDropAPISupportsRemoval;

        internal static void Setup() {
            itemDropAPISupportsRemoval = typeof(ItemDropAPI).GetMethods().Where(m => m.Name == "RemoveFromDefaultByTier").Count() > 0;
            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;
            On.RoR2.Run.Start += On_RunStart;
            On.RoR2.Run.BuildDropTable += On_RunBuildDropTable;
            

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
        private static void On_RunStart(On.RoR2.Run.orig_Start orig, Run self) {
            orig(self);
            if(!NetworkServer.active) return;
            var itemRngGenerator = new Xoroshiro128Plus(self.seed);
            foreach(var bpl in masterItemList)
                bpl.itemRng = new Xoroshiro128Plus(itemRngGenerator.nextUlong);
        }

        private static void On_RunBuildDropTable(On.RoR2.Run.orig_BuildDropTable orig, Run self) {
            var newItemMask = self.availableItems;
            var newEqpMask = self.availableEquipment;
            foreach(ItemBoilerplate bpl in masterItemList) {
                if(!bpl.enabled) {
                    if(bpl is Equipment eqp) newEqpMask.RemoveEquipment(eqp.regIndex);
                    else if(bpl is Item item) newItemMask.RemoveItem(item.regIndex);
                }
            }
            self.availableItems = newItemMask;
            self.availableEquipment = newEqpMask;
            self.NetworkavailableItems = newItemMask;
            self.NetworkavailableEquipment = newEqpMask;
            //ItemDropAPI completely overwrites drop tables; need to perform separate removal
            if(R2API.R2API.IsLoaded("ItemDropAPI")) {
                //RemoveFromDefaultAllTiers is in a potentially unreleased R2API update
                if(itemDropAPISupportsRemoval) {
                    TemporaryCompat.ItemDropAPIRemoveAll();
                } else {
                    //Temporary reflection patch. TODO: remove at some point after R2API updates
                    Dictionary<ItemTier, List<ItemIndex>> ati = (Dictionary<ItemTier, List<ItemIndex>>)typeof(ItemDropAPI).GetFieldCached("AdditionalTierItems").GetValue(null);
                    List<EquipmentIndex> aeqp = (List<EquipmentIndex>)typeof(ItemDropAPI).GetFieldCached("AdditionalEquipment").GetValue(null);
                    foreach(ItemBoilerplate bpl in masterItemList) {
                        if(bpl is Equipment eqp) {
                            if(eqp.enabled) {
                                if(!aeqp.Contains(eqp.regIndex)) aeqp.Add(eqp.regIndex);
                            } else aeqp.Remove(eqp.regIndex);
                        } else if(bpl is Item item) {
                            if(item.enabled) {
                                if(!ati[item.itemTier].Contains(item.regIndex)) ati[item.itemTier].Add(item.regIndex);
                            } else ati[item.itemTier].Remove(item.regIndex);
                        }
                    }
                }
            }
            orig(self);
            //should force-update most cached drop tables
            typeof(PickupDropTable).GetMethodCached("RegenerateAll").Invoke(null, new object[]{Run.instance});
            //update existing Command droplets. part of an effort to disable items mid-stage, may not be necessary while that's prevented
            var pickerOptions = typeof(PickupPickerController).GetFieldCached("options");
            foreach(var picker in UnityEngine.Object.FindObjectsOfType<PickupPickerController>()) {
                var oldOpt = ((PickupPickerController.Option[])pickerOptions.GetValue(picker))[0];
                picker.SetOptionsFromPickupForCommandArtifact(oldOpt.pickupIndex);
            }
        }

        private static void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
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

        private static RoR2.UI.LogBook.Entry[] On_LogbookBuildPickupEntries(On.RoR2.UI.LogBook.LogBookController.orig_BuildPickupEntries orig) {
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

    public abstract class ItemBoilerplate : AutoItemConfigContainer {
        public string nameToken {get; private protected set;}
        public string pickupToken {get; private protected set;}
        public string descToken {get; private protected set;}
        public string loreToken {get; private protected set;}

        public string modName {get; private protected set;}

        /// <summary>Used by TILER2 to request language token value updates (item name). If langID is null, the request is for the invariant token.</summary>
        protected abstract string NewLangName(string langID = null);
        /// <summary>Used by TILER2 to request language token value updates (pickup text). If langID is null, the request is for the invariant token.</summary>
        protected abstract string NewLangPickup(string langID = null);
        /// <summary>Used by TILER2 to request language token value updates (description text). If langID is null, the request is for the invariant token.</summary>
        protected abstract string NewLangDesc(string langID = null);
        /// <summary>Used by TILER2 to request language token value updates (lore text). If langID is null, the request is for the invariant token.</summary>
        protected abstract string NewLangLore(string langID = null);
        /// <summary>Used by TILER2 to request pickup/logbook model updates. Return null (default behavior) to keep the original.</summary>
        protected virtual GameObject NewPickupModel() {
            return null;
        }

        public ItemBoilerplate() {
            defaultEnabledUpdateFlags = AutoUpdateEventFlags.AnnounceToRun;

            ItemBoilerplateModule.masterItemList.Add(this);
            //private Dictionary allRegisteredLanguages; todo; RegLang is never called with a langid!=null param for now
            ConfigEntryChanged += (sender, args) => {
                if((args.flags & AutoUpdateEventFlags.InvalidateNameToken) == AutoUpdateEventFlags.InvalidateNameToken) {
                    LanguageAPI.Add(nameToken, NewLangName());
                }
                if((args.flags & AutoUpdateEventFlags.InvalidatePickupToken) == AutoUpdateEventFlags.InvalidatePickupToken) {
                    LanguageAPI.Add(pickupToken, (enabled ? "" : "<color=#FF0000>[DISABLED]</color>") + NewLangPickup());
                }
                if((args.flags & AutoUpdateEventFlags.InvalidateDescToken) == AutoUpdateEventFlags.InvalidateDescToken) {
                    LanguageAPI.Add(descToken, (enabled ? "" : "<color=#FF0000>[DISABLED]</color>\n") + NewLangDesc());
                }
                if((args.flags & AutoUpdateEventFlags.InvalidateLoreToken) == AutoUpdateEventFlags.InvalidateLoreToken) {
                    LanguageAPI.Add(loreToken, NewLangLore());
                }
            
                if((args.flags & AutoUpdateEventFlags.InvalidateModel) == AutoUpdateEventFlags.InvalidateModel) {
                    var newModel = NewPickupModel();
                    if(newModel != null) {
                        if(pickupDef != null) pickupDef.displayPrefab = newModel;
                        if(logbookEntry != null) logbookEntry.modelPrefab = newModel;
                    }
                }
            };
        }

        public PickupDef pickupDef {get; internal set;}
        public PickupIndex pickupIndex {get; internal set;}
        public RoR2.UI.LogBook.Entry logbookEntry {get; internal set;}

        [AutoUpdateEventInfo(AutoUpdateEventFlags.InvalidateDescToken | AutoUpdateEventFlags.InvalidatePickupToken | AutoUpdateEventFlags.InvalidateStats | AutoUpdateEventFlags.InvalidateDropTable)]
        [AutoItemConfig("If false, this item/equipment will not drop ingame, and it will not work if you somehow get a copy (all IL patches and hooks will be disabled for compatibility).",
            AutoItemConfigFlags.PreventNetMismatch | AutoItemConfigFlags.DeferUntilNextStage)]
        public bool enabled {get; protected set;} = true;

        ///<summary>A resource string pointing to the item's model.</summary>
        public string modelPathName {get; protected set;} = null;
        ///<summary>A resource string pointing to the item's icon.</summary>
        public string iconPathName {get; protected set;} = null;
        
        protected ItemDisplayRuleDict displayRules = new ItemDisplayRuleDict(null);
            
        ///<summary>Set to true when SetupConfig is called; prevents SetupConfig from being called multiple times on the same instance.</summary>
        public bool configDone {get; private protected set;} = false;
        ///<summary>Set to true when SetupAttributes is called; prevents SetupAttributes from being called multiple times on the same instance.</summary>
        public bool attributesDone {get; private protected set;} = false;
        ///<summary>Set to true when SetupBehavior is called; prevents SetupBehavior from being called multiple times on the same instance.</summary>
        public bool behaviorDone {get; private protected set;} = false;

        ///<summary>A server-only rng instance based on the current run's seed.</summary>
        public Xoroshiro128Plus itemRng {get; internal set;}

        /// <summary>The item's internal name. Will be identical to the name of the innermost class deriving from ItemBoilerplate.</summary>
        public string itemCodeName {get; private protected set;}
        /// <summary>The item's display name in the mod's default language. Will be used in config files; should also be used in RegLang if called with no language parameter.</summary>
        public abstract string displayName {get;}

        public abstract void SetupConfig(ConfigFile cfl);
        
        public abstract void SetupAttributes(string modTokenIdent, string modCNamePrefix = "");

        protected void RegLang(string langid = null) {
            if(langid == null) {
                if(nameToken != null) LanguageAPI.Add(nameToken, NewLangName());
                if(pickupToken != null) LanguageAPI.Add(pickupToken, NewLangPickup());
                if(descToken != null) LanguageAPI.Add(descToken, NewLangDesc());
                if(loreToken != null) LanguageAPI.Add(loreToken, NewLangLore());
            } else {
                if(nameToken != null) LanguageAPI.Add(nameToken, NewLangName(langid), langid);
                if(pickupToken != null) LanguageAPI.Add(pickupToken, NewLangPickup(langid), langid);
                if(descToken != null) LanguageAPI.Add(descToken, NewLangDesc(langid), langid);
                if(loreToken != null) LanguageAPI.Add(loreToken, NewLangLore(langid), langid);
            }
        }
        
        public abstract void SetupBehavior(); 
        protected abstract void LoadBehavior();
        protected abstract void UnloadBehavior();

        public static FilingDictionary<ItemBoilerplate> InitAll(string modDisplayName) {
            if(AutoItemConfig.instances.Exists(x => x.modName == modDisplayName)) {
                Debug.LogError("TILER2: ItemBoilerplate.InitAll FAILED: the modDisplayName \"" + modDisplayName + "\" is already in use!");
                return null;
            }

            FilingDictionary<ItemBoilerplate> f = new FilingDictionary<ItemBoilerplate>();
            foreach(Type type in Assembly.GetCallingAssembly().GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ItemBoilerplate)))) {
                var newBpl = (ItemBoilerplate)Activator.CreateInstance(type);
                newBpl.modName = modDisplayName;
                f.Add(newBpl);
            }
            return f; //:regional_indicator_f:
        }
    }
}
