using BepInEx.Configuration;
using R2API;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static TILER2.MiscUtil;

namespace TILER2 {

    public abstract class Item<T>:Item where T : Item<T> {
        public static T instance {get;private set;}

        public Item() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting ItemBoilerplate/Item was instantiated twice");
            this.itemCodeName = typeof(T).Name;
            instance = this as T;
        }
    }
    public abstract class Equipment<T>:Equipment where T : Equipment<T> {
        public static T instance {get;private set;}

        public Equipment() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting ItemBoilerplate/Equipment was instantiated twice");
            this.itemCodeName = typeof(T).Name;
            instance = this as T;
        }
    }

    public abstract class Item : ItemBoilerplate {
        public ItemIndex regIndex {get; private set;}
        public ItemDef regDef {get; private set;}
        public CustomItem regItem {get; private set;}

        public abstract ItemTier itemTier {get;}

        [AutoItemConfig("If true, the item will not be given to enemies by Evolution nor in the arena map, and it will not be found by Scavengers.")]
        public virtual bool itemAIB {get;protected set;} = false;

        public virtual ReadOnlyCollection<ItemTag> itemTags {get; private set;}

        protected event Action<ConfigFile> preConfig;
        protected event Action<ConfigFile> postConfig;
        protected event Action<string, string> onAttrib;
        protected event Action onBehav;
        
        public override void SetupConfig(ConfigFile cfl) {
            if(configDone) {
                Debug.LogError("TILER2: something tried to setup config for an item twice");
                return;
            }
            configDone = true;

            preConfig?.Invoke(cfl);

            this.BindAll(cfl, modName, "Items." + itemCodeName);

            postConfig?.Invoke(cfl);
            
            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if(Run.instance?.enabled == true) {
                        Run.instance.BuildDropTable();
                    }
                    if(args.oldValue != args.newValue) {
                        if((bool)args.newValue == true) {
                            LoadBehavior();
                            if(Run.instance?.enabled == true) Chat.AddMessage("<color=#" + ColorCatalog.GetColorHexString(regDef.colorIndex) + ">" + displayName + "</color> has been <color=#aaffaa>ENABLED</color>. It will now drop, and existing copies will start working again.");
                        } else {
                            if(Run.instance?.enabled == true) Chat.AddMessage("<color=#" + ColorCatalog.GetColorHexString(regDef.colorIndex) + ">" + displayName + "</color> has been <color=#ffaaaa>DISABLED</color>. It will no longer drop, and existing copies will stop working.");
                            UnloadBehavior();
                        }
                    }
                } else if(args.target.boundProperty.Name == nameof(itemAIB)) {
                    var hasAIB = regDef.tags.Contains(ItemTag.AIBlacklist);
                    if(hasAIB && !itemAIB) {
                        regDef.tags = regDef.tags.Where(tag => tag != ItemTag.AIBlacklist).ToArray();
                    } else if(!hasAIB && itemAIB) {
                        var nl = regDef.tags.ToList();
                        nl.Add(ItemTag.AIBlacklist);
                        regDef.tags = nl.ToArray();
                    }
                }
            };
        }
        public override void SetupAttributes(string modTokenIdent, string modCNamePrefix = "") {
            if(attributesDone) {
                Debug.LogError("TILER2: something tried to setup attributes for an item twice");
                return;
            }
            attributesDone = true;

            nameToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_NAME";
            pickupToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_PICKUP";
            descToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_DESC";
            loreToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_LORE";

            onAttrib?.Invoke(modTokenIdent, modCNamePrefix);

            RegLang();
            
            var _itemTags = new List<ItemTag>(itemTags);
            if(itemAIB) _itemTags.Add(ItemTag.AIBlacklist);
            var iarr = _itemTags.ToArray();
            regDef = new ItemDef {
                name = modCNamePrefix+itemCodeName,
                tier = itemTier,
                pickupModelPath = modelPathName,
                pickupIconPath = iconPathName,
                nameToken = this.nameToken,
                pickupToken = this.pickupToken,
                descriptionToken = this.descToken,
                loreToken = this.loreToken,
                tags = iarr
            };

            itemTags = Array.AsReadOnly(iarr);
            regItem = new CustomItem(regDef, displayRules);
            regIndex = ItemAPI.Add(regItem);
        }

        public override void SetupBehavior() {
            if(behaviorDone) {
                Debug.LogError("TILER2: something tried to setup behavior for an item twice");
                return;
            }
            behaviorDone = true;

            onBehav?.Invoke();

            if(enabled)
                LoadBehavior();
        }

        public int GetCount(Inventory inv) {
            return inv?.GetItemCount(regIndex) ?? 0;
        }
        public int GetCount(CharacterMaster chrm) {
            if(!chrm || !chrm.inventory) return 0;
            return chrm.inventory.GetItemCount(regIndex);
        }
        public int GetCount(CharacterBody body) {
            if(!body || !body.inventory) return 0;
            return body.inventory.GetItemCount(regIndex);
        }
        public int GetCountOnDeploys(CharacterMaster master) {
            if(master == null) return 0;
            var dplist = master.GetFieldValue<List<DeployableInfo>>("deployablesList");
            if(dplist == null) return 0;
            int count = 0;
            foreach(DeployableInfo d in dplist) {
                count += GetCount(d.deployable.gameObject.GetComponent<Inventory>());
            }
            return count;
        }
    }
    public abstract class Equipment : ItemBoilerplate {
        public EquipmentIndex regIndex {get; private set;}
        public EquipmentDef regDef {get; private set;}
        public CustomEquipment regEqp {get; private set;}

        [AutoItemConfig("The base cooldown of the equipment, in seconds.", AutoItemConfigFlags.DeferUntilNextStage, 0f, float.MaxValue)]
        public virtual float eqpCooldown {get; protected set;} = 45f; //TODO: add a getter function to update ingame cooldown properly if in use; marked as DeferUntilNextStage until then
        public virtual bool eqpEnigmable => true;
        public virtual bool eqpIsLunar => false;
        
        protected event Action<ConfigFile> preConfig;
        protected event Action<ConfigFile> postConfig;
        protected event Action<string, string> onAttrib;
        protected event Action onBehav;

        public override void SetupConfig(ConfigFile cfl) {
            if(configDone) {
                Debug.LogError("TILER2: something tried to setup config for an equipment twice");
                return;
            }
            configDone = true;

            preConfig?.Invoke(cfl);

            this.BindAll(cfl, modName, "Items." + itemCodeName);

            postConfig?.Invoke(cfl);
            
            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if(args.oldValue != args.newValue) {
                        if((bool)args.newValue == true) {
                            LoadBehavior();
                            if(Run.instance?.enabled == true) Chat.AddMessage("<color=#" + ColorCatalog.GetColorHexString(regDef.colorIndex) + ">" + displayName + "</color> has been <color=#aaffaa>ENABLED</color>. It will now drop, and existing copies will start working again.");
                        } else {
                            if(Run.instance?.enabled == true) Chat.AddMessage("<color=#" + ColorCatalog.GetColorHexString(regDef.colorIndex) + ">" + displayName + "</color> has been <color=#ffaaaa>DISABLED</color>. It will no longer drop, and existing copies will stop working.");
                            UnloadBehavior();
                        }
                    }
                }
            };
        }
        public override void SetupAttributes(string modTokenIdent, string modCNamePrefix = "") {
            if(attributesDone) {
                Debug.LogError("TILER2: something tried to setup attributes for an equipment twice");
                return;
            }
            attributesDone = true;

            nameToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_NAME";
            pickupToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_PICKUP";
            descToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_DESC";
            loreToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_LORE";

            onAttrib?.Invoke(modTokenIdent, modCNamePrefix);

            RegLang();

            regDef = new EquipmentDef {
                name = modCNamePrefix+itemCodeName,
                pickupModelPath = modelPathName,
                pickupIconPath = iconPathName,
                nameToken = this.nameToken,
                pickupToken = this.pickupToken,
                descriptionToken = this.descToken,
                loreToken = this.loreToken,
                cooldown = eqpCooldown,
                enigmaCompatible = eqpEnigmable,
                isLunar = eqpIsLunar,
                canDrop = true
            };
            regEqp = new CustomEquipment(regDef, displayRules);
            regIndex = ItemAPI.Add(regEqp);
        }

        public override void SetupBehavior() {
            if(behaviorDone) {
                Debug.LogError("TILER2: something tried to setup behavior for an equipment twice");
                return;
            }
            behaviorDone = true;

            onBehav?.Invoke();

            if(enabled)
                LoadBehavior();

            On.RoR2.EquipmentSlot.PerformEquipmentAction += Evt_ESPerformEquipmentAction;
        }

        protected override void LoadBehavior() {}
        protected override void UnloadBehavior() {}

        private bool Evt_ESPerformEquipmentAction(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot self, EquipmentIndex ind) {
            if(this.enabled && ind == this.regIndex) return OnEquipUseInner(self);
            else return orig(self, ind);
        }

        protected abstract bool OnEquipUseInner(EquipmentSlot slot);
        
        public bool HasEqp(Inventory inv, bool inMain = true, bool inAlt = false) {
            return (inMain && (inv?.currentEquipmentIndex ?? EquipmentIndex.None) == regIndex) || (inAlt && (inv?.alternateEquipmentIndex ?? EquipmentIndex.None) == regIndex);
        }
        public bool HasEqp(CharacterBody body) {
            return (body?.equipmentSlot?.equipmentIndex ?? EquipmentIndex.None) == regIndex;
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

            TILER2Plugin.masterItemList.Add(this);
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
        public string modelPathName {get; protected set;}
        ///<summary>A resource string pointing to the item's icon.</summary>
        public string iconPathName {get; protected set;}
        
        protected ItemDisplayRuleDict displayRules = new ItemDisplayRuleDict(null);
            
        ///<summary>Set to true when SetupConfig is called; prevents SetupConfig from being called multiple times on the same instance.</summary>
        public bool configDone {get; private protected set;} = false;
        ///<summary>Set to true when SetupAttributes is called; prevents SetupAttributes from being called multiple times on the same instance.</summary>
        public bool attributesDone {get; private protected set;} = false;
        ///<summary>Set to true when SetupBehavior is called; prevents SetupBehavior from being called multiple times on the same instance.</summary>
        public bool behaviorDone {get; private protected set;} = false;

        /// <summary>The item's internal name. Will be identical to the name of the innermost class deriving from ItemBoilerplate.</summary>
        public string itemCodeName {get; private protected set;}
        /// <summary>The item's display name in the mod's default language. Will be used in config files; should also be used in RegLang if called with no language parameter.</summary>
        public abstract string displayName {get;}

        public abstract void SetupConfig(ConfigFile cfl);
        
        public abstract void SetupAttributes(string modTokenIdent, string modCNamePrefix = "");

        protected void RegLang(string langid = null) {
            if(langid == null) {
                LanguageAPI.Add(nameToken, NewLangName());
                LanguageAPI.Add(pickupToken, NewLangPickup());
                LanguageAPI.Add(descToken, NewLangDesc());
                LanguageAPI.Add(loreToken, NewLangLore());
            } else {
                LanguageAPI.Add(nameToken, NewLangName(langid), langid);
                LanguageAPI.Add(pickupToken, NewLangPickup(langid), langid);
                LanguageAPI.Add(descToken, NewLangDesc(langid), langid);
                LanguageAPI.Add(loreToken, NewLangLore(langid), langid);
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
