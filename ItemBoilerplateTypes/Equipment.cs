using BepInEx.Configuration;
using R2API;
using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    public abstract class Equipment<T>:Equipment where T : Equipment<T> {
        public static T instance {get;private set;}

        public Equipment() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting ItemBoilerplate/Equipment was instantiated twice");
            this.itemCodeName = typeof(T).Name;
            instance = this as T;
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
                TILER2Plugin._logger.LogError("Something tried to setup config for an equipment twice");
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
                TILER2Plugin._logger.LogError("Something tried to setup attributes for an equipment twice");
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
            if(eqpIsLunar) 
				regDef.colorIndex = ColorCatalog.ColorIndex.LunarItem;
            regEqp = new CustomEquipment(regDef, displayRules);
            regIndex = ItemAPI.Add(regEqp);
        }

        public override void SetupBehavior() {
            if(behaviorDone) {
                TILER2Plugin._logger.LogError("Something tried to setup behavior for an equipment twice");
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
}
