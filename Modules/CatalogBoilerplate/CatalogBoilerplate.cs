using BepInEx.Configuration;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static TILER2.MiscUtil;

namespace TILER2 {
    /// <summary>
    /// A wrapper for T2Module that provides some fields/properties common to most RoR2 catalog types.
    /// </summary>
    public abstract class CatalogBoilerplate : T2Module {
        public string nameToken {get; private protected set;}
        public string pickupToken {get; private protected set;}
        public string descToken {get; private protected set;}
        public string loreToken {get; private protected set;}

        /// <summary>Specifies non-generic languages to scan during language invalidation.</summary>
        protected virtual string[] extraLanguages => new string[] { };

        /// <summary>Used by TILER2 to request language token value updates (object name). If langID is null, the request is for the invariant token.</summary>
        protected abstract string GetNameString(string langID = null);
        /// <summary>Used by TILER2 to request language token value updates (pickup text, where applicable). If langID is null, the request is for the invariant token.</summary>
        protected abstract string GetPickupString(string langID = null);
        /// <summary>Used by TILER2 to request language token value updates (description text). If langID is null, the request is for the invariant token.</summary>
        protected abstract string GetDescString(string langID = null);
        /// <summary>Used by TILER2 to request language token value updates (lore text, where applicable). If langID is null, the request is for the invariant token.</summary>
        protected abstract string GetLoreString(string langID = null);
        /// <summary>Used by TILER2 to request pickup/logbook model updates, where applicable. Return null (default behavior) to keep the original.</summary>
        protected virtual GameObject GetPickupModel() {
            return null;
        }

        public CatalogBoilerplate() {
            CatalogBoilerplateModule.allInstances.Add(this);
        }

        public override void SetupConfig() {
            base.SetupConfig();
            ConfigEntryChanged += (sender, args) => {
                if((args.flags & AutoConfigUpdateEventFlags.InvalidateModel) == AutoConfigUpdateEventFlags.InvalidateModel) {
                    var newModel = GetPickupModel();
                    if(newModel != null) {
                        if(pickupDef != null) pickupDef.displayPrefab = newModel;
                        if(logbookEntry != null) logbookEntry.modelPrefab = newModel;
                    }
                }
            };
        }

        public override void InstallLanguage() {
            genericLanguageTokens[nameToken] = GetNameString();
            genericLanguageTokens[pickupToken] = (enabled ? "" : LANG_PREFIX_DISABLED) + GetPickupString();
            genericLanguageTokens[descToken] = (enabled ? "" : $"{LANG_PREFIX_DISABLED}\n") + GetDescString();
            genericLanguageTokens[loreToken] = GetLoreString();

            foreach(var lang in extraLanguages) {
                if(!specificLanguageTokens.ContainsKey(lang)) specificLanguageTokens.Add(lang, new Dictionary<string, string>());
                var specLang = specificLanguageTokens[lang];
                specLang[nameToken] = GetNameString(lang);
                specLang[pickupToken] = (enabled ? "" : LANG_PREFIX_DISABLED) + GetPickupString(lang);
                specLang[descToken] = (enabled ? "" : $"{LANG_PREFIX_DISABLED}\n") + GetDescString(lang);
                specLang[loreToken] = GetLoreString(lang);
            }

            base.InstallLanguage();
        }

        public override void SetupAttributes() {
            base.SetupAttributes();
            nameToken = $"{modInfo.longIdentifier}_{name.ToUpper()}_NAME";
            descToken = $"{modInfo.longIdentifier}_{name.ToUpper()}_DESC";
            pickupToken = $"{modInfo.longIdentifier}_{name.ToUpper()}_PICKUP";
            loreToken = $"{modInfo.longIdentifier}_{name.ToUpper()}_LORE";
        }

        public PickupDef pickupDef {get; internal set;}
        public PickupIndex pickupIndex {get; internal set;}
        public RoR2.UI.LogBook.Entry logbookEntry {get; internal set;}

        protected internal override AutoConfigUpdateEventFlags defaultEnabledUpdateFlags => AutoConfigUpdateEventFlags.AnnounceToRun;
        public override bool managedEnable => true;
        public override AutoConfigFlags enabledConfigFlags => AutoConfigFlags.PreventNetMismatch | AutoConfigFlags.DeferUntilNextStage;
        public override AutoConfigUpdateEventFlags enabledConfigUpdateEventFlags => AutoConfigUpdateEventFlags.InvalidateLanguage | AutoConfigUpdateEventFlags.InvalidateStats | AutoConfigUpdateEventFlags.InvalidateDropTable;

        ///<summary>A resource string pointing to the object's model.</summary>
        public string modelResourcePath {get; protected set;} = null;
        ///<summary>A resource string pointing to the object's icon.</summary>
        public string iconResourcePath {get; protected set;} = null;
        
        ///<summary>The object's display name in the mod's default language. Will be used in config files; should also be used in generic language tokens.</summary>
        public abstract string displayName {get;}

        public static void ConsoleDump(BepInEx.Logging.ManualLogSource logger, FilingDictionary<CatalogBoilerplate> instances) {
            int longestClassName = 0;
            int longestObjectName = 0;
            var allStrings = new List<ConsoleStrings>();
            foreach(CatalogBoilerplate x in instances) {
                var strings = x.GetConsoleStrings();
                allStrings.Add(strings);
                longestClassName = Mathf.Max(strings.className.Length, longestClassName);
                longestObjectName = Mathf.Max(strings.objectName.Length, longestObjectName);
            }

            logger.LogMessage("Index dump follows (pairs of name / index):");
            foreach(ConsoleStrings strings in allStrings) {
                logger.LogMessage($"{strings.className.PadLeft(longestClassName)} {strings.objectName.PadRight(longestObjectName)} / {strings.formattedIndex}");
            }
        }

        public struct ConsoleStrings {
            public string className;
            public string objectName;
            public string formattedIndex;
        }
        public virtual ConsoleStrings GetConsoleStrings() {
            return new ConsoleStrings {
                className = "Other",
                objectName = this.name,
                formattedIndex = "N/A"
                };
        }
    }
}