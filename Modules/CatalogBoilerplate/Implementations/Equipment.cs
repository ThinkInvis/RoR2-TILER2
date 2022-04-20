using R2API;
using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    public abstract class Equipment<T>:Equipment where T : Equipment<T> {
        public static T instance {get;private set;}

        public Equipment() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting ItemBoilerplate/Equipment was instantiated twice");
            instance = this as T;
        }
    }

    public abstract class Equipment : CatalogBoilerplate {
        public override string configCategoryPrefix => "Equipments.";

        public EquipmentIndex catalogIndex {
            get {
                if(equipmentDef == null) {
                    TILER2Plugin._logger.LogError($"TILER2.Equipment {name} has null EquipmentDef! Cannot retrieve EquipmentIndex");
                    return EquipmentIndex.None;
                }
                return equipmentDef.equipmentIndex;
            }
        }
        public EquipmentDef equipmentDef {get; private set;}
        public CustomEquipment customEquipment {get; private set;}

        protected ItemDisplayRuleDict displayRules = new ItemDisplayRuleDict();

        [AutoConfigRoOSlider("{0:N0} s", 0f, 1000f)]
        [AutoConfig("The base cooldown of the equipment, in seconds.", AutoConfigFlags.DeferUntilNextStage, 0f, float.MaxValue)]
        public virtual float cooldown {get; protected set;} = 45f; //TODO: add a getter function to update ingame cooldown properly if in use; marked as DeferUntilNextStage until then
        public virtual bool isEnigmaCompatible => true;
        public virtual bool canBeRandomlyTriggered => true;
        public virtual bool isLunar => false;

        public override void SetupConfig() {
            base.SetupConfig();

            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if(args.oldValue != args.newValue) {
                        if((bool)args.newValue == true) {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage($"<color=#{ColorCatalog.GetColorHexString(equipmentDef.colorIndex)}>{displayName}</color> has been <color=#aaffaa>ENABLED</color>. It will now drop, and existing copies will start working again.");
                        } else {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage($"<color=#{ColorCatalog.GetColorHexString(equipmentDef.colorIndex)}>{displayName}</color> has been <color=#ffaaaa>DISABLED</color>. It will no longer drop, and existing copies will stop working.");
                        }
                    }
                }
            };
        }

        public override void SetupAttributes() {
            base.SetupAttributes();

            equipmentDef = ScriptableObject.CreateInstance<EquipmentDef>();
            equipmentDef.name = modInfo.shortIdentifier + name;
            equipmentDef.pickupModelPrefab = modelResource;
            equipmentDef.pickupIconSprite = iconResource;
            equipmentDef.nameToken = this.nameToken;
            equipmentDef.pickupToken = this.pickupToken;
            equipmentDef.descriptionToken = this.descToken;
            equipmentDef.loreToken = this.loreToken;
            equipmentDef.cooldown = cooldown;
            equipmentDef.enigmaCompatible = isEnigmaCompatible;
            equipmentDef.canBeRandomlyTriggered = canBeRandomlyTriggered;
            equipmentDef.isLunar = isLunar;
            equipmentDef.canDrop = true;
            if(isLunar) 
				equipmentDef.colorIndex = ColorCatalog.ColorIndex.LunarItem;

            SetupModifyEquipmentDef();

            customEquipment = new CustomEquipment(equipmentDef, displayRules);
            ItemAPI.Add(customEquipment);

            EquipmentCatalog.availability.CallWhenAvailable(this.SetupCatalogReady);
        }

        public virtual void SetupModifyEquipmentDef() { }

        public override void Install() {
            base.Install();
            On.RoR2.EquipmentSlot.PerformEquipmentAction += Evt_ESPerformEquipmentAction;
            equipmentDef.pickupIconSprite = iconResource;
        }

        public override void Uninstall() {
            base.Uninstall();
            On.RoR2.EquipmentSlot.PerformEquipmentAction -= Evt_ESPerformEquipmentAction;
            equipmentDef.pickupIconSprite = CatalogBoilerplateModule.lockIcon;
        }

        private bool Evt_ESPerformEquipmentAction(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot self, EquipmentDef def) {
            if(enabled && def.equipmentIndex == catalogIndex) return PerformEquipmentAction(self);
            else return orig(self, def);
        }

        protected abstract bool PerformEquipmentAction(EquipmentSlot slot);
        
        public bool HasEquipment(Inventory inv, bool inMain = true, bool inAlt = false) {
            return (inMain &&
                (inv != null ? inv.currentEquipmentIndex : EquipmentIndex.None) == catalogIndex)
                || (inAlt &&
                (inv != null ? inv.alternateEquipmentIndex : EquipmentIndex.None) == catalogIndex);
        }

        public bool HasEquipment(CharacterBody body) {
            var eqpIndex = EquipmentIndex.None;
            if(body && body.equipmentSlot) eqpIndex = body.equipmentSlot.equipmentIndex;
            return eqpIndex == catalogIndex;
        }

        public override ConsoleStrings GetConsoleStrings() {
            return new ConsoleStrings {
                className = "Equipment",
                objectName = this.name,
                formattedIndex = ((int)this.catalogIndex).ToString()
            };
        }
    }
}
