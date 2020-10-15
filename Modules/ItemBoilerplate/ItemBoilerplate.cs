using BepInEx.Configuration;
using R2API;
using RoR2;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static TILER2.MiscUtil;

namespace TILER2 {
    public abstract class ItemBoilerplate : Module {
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
                if((args.flags & AutoUpdateEventFlags.InvalidateModel) == AutoUpdateEventFlags.InvalidateModel) {
                    var newModel = NewPickupModel();
                    if(newModel != null) {
                        if(pickupDef != null) pickupDef.displayPrefab = newModel;
                        if(logbookEntry != null) logbookEntry.modelPrefab = newModel;
                    }
                }
            };
        }

        public override void InstallLang() {
            genericLanguageTokens[nameToken] = NewLangName();
            genericLanguageTokens[pickupToken] = (enabled ? "" : "<color=#FF0000>[DISABLED]</color>") + NewLangPickup();
            genericLanguageTokens[descToken] = (enabled ? "" : "<color=#FF0000>[DISABLED]</color>\n") + NewLangDesc();
            genericLanguageTokens[loreToken] = NewLangLore();

            base.InstallLang();
        }

        public PickupDef pickupDef {get; internal set;}
        public PickupIndex pickupIndex {get; internal set;}
        public RoR2.UI.LogBook.Entry logbookEntry {get; internal set;}

        public override bool managedEnable => true;
        public override string configDescription => "";
        public override AutoConfigFlags enabledConfigFlags => AutoConfigFlags.PreventNetMismatch | AutoConfigFlags.DeferUntilNextStage;
        public override AutoUpdateEventFlags enabledConfigUpdateEventFlags => AutoUpdateEventFlags.InvalidateLanguage | AutoUpdateEventFlags.InvalidateStats | AutoUpdateEventFlags.InvalidateDropTable;

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

        ///<summary>The item's internal name. Will be identical to the name of the innermost class deriving from ItemBoilerplate.</summary>
        public string itemCodeName {get; private protected set;}
        ///<summary>The item's display name in the mod's default language. Will be used in config files; should also be used in RegLang if called with no language parameter.</summary>
        public abstract string displayName {get;}

        /// <summary>
        /// Implement to handle AutoItemConfig binding and other related actions. With standard base plugin setup, will be performed before SetupAttributes and SetupBehavior.
        /// </summary>
        /// <param name="cfl">The base plugin's ConfigFile (or any other ConfigFile passed to ItemBoilerplate.SetupConfig by the base plugin).</param>
        public abstract void SetupConfig(ConfigFile cfl);
        
        /// <summary>
        /// Implement to handle registration with RoR2 catalogs (e.g. ItemCatalog).
        /// </summary>
        /// <param name="modTokenIdent">A long string to use to identify the mod.</param>
        /// <param name="modCNamePrefix">A short string to add as a prefix to code names (e.g. item name) which are otherwise auto-populated from class names.</param>
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
        
        /// <summary>
        /// Implement to handle permanent hooks and/or other one-time-only setup for hooks.
        /// </summary>
        public abstract void SetupBehavior(); 
        protected abstract void LoadBehavior();
        protected abstract void UnloadBehavior();
    }
}
