using BepInEx.Configuration;
using R2API;
using RoR2;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static TILER2.MiscUtil;

namespace TILER2 {
    public abstract class ItemBoilerplate : T2Module {
        public string nameToken {get; private protected set;}
        public string pickupToken {get; private protected set;}
        public string descToken {get; private protected set;}
        public string loreToken {get; private protected set;}

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

            ConfigEntryChanged += (sender, args) => {
                if((args.flags & AutoUpdateEventFlags.InvalidateModel) == AutoUpdateEventFlags.InvalidateModel) {
                    var newModel = NewPickupModel();
                    if(newModel != null) {
                        if(pickupDef != null) pickupDef.displayPrefab = newModel;
                        if(logbookEntry != null) logbookEntry.modelPrefab = newModel;
                    }
                }
            };
        }

        public override void InstallLanguage() {
            genericLanguageTokens[nameToken] = NewLangName();
            genericLanguageTokens[pickupToken] = (enabled ? "" : "<color=#FF0000>[DISABLED]</color>") + NewLangPickup();
            genericLanguageTokens[descToken] = (enabled ? "" : "<color=#FF0000>[DISABLED]</color>\n") + NewLangDesc();
            genericLanguageTokens[loreToken] = NewLangLore();

            base.InstallLanguage();
        }

        public override void SetupAttributes() {
            base.SetupAttributes();
            nameToken = $"{modInfo.longIdentifier}_{name.ToUpper()}_NAME";
            descToken = $"{modInfo.longIdentifier}_{name.ToUpper()}_DESC";
        }

        public PickupDef pickupDef {get; internal set;}
        public PickupIndex pickupIndex {get; internal set;}
        public RoR2.UI.LogBook.Entry logbookEntry {get; internal set;}

        public override bool managedEnable => true;
        public override AutoConfigFlags enabledConfigFlags => AutoConfigFlags.PreventNetMismatch | AutoConfigFlags.DeferUntilNextStage;
        public override AutoUpdateEventFlags enabledConfigUpdateEventFlags => AutoUpdateEventFlags.InvalidateLanguage | AutoUpdateEventFlags.InvalidateStats | AutoUpdateEventFlags.InvalidateDropTable;

        ///<summary>A resource string pointing to the resource's model.</summary>
        public string modelPathName {get; protected set;} = null;
        ///<summary>A resource string pointing to the resource's icon.</summary>
        public string iconPathName {get; protected set;} = null;
        
        ///<summary>The item's display name in the mod's default language. Will be used in config files; should also be used in RegLang if called with no language parameter.</summary>
        public abstract string displayName {get;}
    }
}