using BepInEx.Configuration;
using R2API;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace TILER2 {
    public abstract class Item<T>:Item where T : Item<T> {
        public static T instance {get;private set;}

        public Item() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting ItemBoilerplate/Item was instantiated twice");
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
}
