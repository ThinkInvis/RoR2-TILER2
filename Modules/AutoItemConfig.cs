using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;
using RoR2;
using RoR2.Networking;
using System.Text.RegularExpressions;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace TILER2 {
    internal static class AutoItemConfigModule {
        internal static bool globalStatsDirty = false;
        internal static bool globalDropsDirty = false;

        internal static void Setup() {
            //this doesn't seem to fire until the title screen is up, which is good because config file changes shouldn't immediately be read during startup; watch for regression (or just implement a check anyways?)
            On.RoR2.RoR2Application.Update += AutoItemConfigContainer.FilePollUpdateHook;
            
            On.RoR2.Networking.GameNetworkManager.Disconnect += On_GNMDisconnect;
            
            SceneManager.sceneLoaded += Evt_USMSceneLoaded;
        }

        internal static void Update() {
            if(!(Run.instance != null && Run.instance.isActiveAndEnabled)) {
                globalStatsDirty = false;
                globalDropsDirty = false;
            } else {
                if(globalStatsDirty) {
                    globalStatsDirty = false;
                    MiscUtil.AliveList().ForEach(cm => {if(cm.hasBody) cm.GetBody().RecalculateStats();});
                }
                if(globalDropsDirty) {
                    globalDropsDirty = false;
                    Run.instance.BuildDropTable();
                }
            }
        }

        internal static void On_GNMDisconnect(On.RoR2.Networking.GameNetworkManager.orig_Disconnect orig, GameNetworkManager self) {
            orig(self);
            AutoItemConfig.CleanupDirty(true);
        }

        internal static void Evt_USMSceneLoaded(Scene scene, LoadSceneMode mode) {
            AutoItemConfig.CleanupDirty(false);
        }
    }

    public class AutoItemConfig {
        internal readonly static List<AutoItemConfig> instances = new List<AutoItemConfig>();
        internal readonly static Dictionary<AutoItemConfig, (object, bool)> stageDirtyInstances = new Dictionary<AutoItemConfig, (object, bool)>();
        internal readonly static Dictionary<AutoItemConfig, object> runDirtyInstances = new Dictionary<AutoItemConfig, object>();

        internal static void CleanupDirty(bool isRunEnd) {
            TILER2Plugin._logger.LogDebug("Stage ended; applying " + stageDirtyInstances.Count + " deferred config changes...");
            foreach(AutoItemConfig k in stageDirtyInstances.Keys) {
                k.DeferredUpdateProperty(stageDirtyInstances[k].Item1, stageDirtyInstances[k].Item2);
            }
            stageDirtyInstances.Clear();
            if(isRunEnd) {
                TILER2Plugin._logger.LogDebug("Run ended; applying " + runDirtyInstances.Count + " deferred config changes...");
                foreach(AutoItemConfig k in runDirtyInstances.Keys) {
                    k.DeferredUpdateProperty(runDirtyInstances[k], true);
                }
                runDirtyInstances.Clear();
            }
        }

        public AutoItemConfigContainer owner {get; internal set;}
        public object target {get; internal set;}
        public ConfigEntryBase configEntry {get; internal set;}
        public PropertyInfo boundProperty {get; internal set;}
        public string modName {get; internal set;}

        public AutoUpdateEventInfoAttribute updateEventAttribute {get; internal set;}

        public MethodInfo propGetter {get; internal set;}
        public MethodInfo propSetter {get; internal set;}
        public Type propType {get; internal set;}

        public object boundKey {get; internal set;}
        public bool onDict {get; internal set;}

        public bool allowConCmd {get; internal set;}
        public bool allowNetMismatch {get; internal set;}
        public bool netMismatchCritical {get; internal set;}

        public object cachedValue {get; internal set;}

        internal bool isOverridden = false;

        public int deferType {get; internal set;}

        public string readablePath {
            get {return modName + "/" + configEntry.Definition.Section + "/" + configEntry.Definition.Key;}
        }

        internal AutoItemConfig() {
            instances.Add(this);
        }

        ~AutoItemConfig() {
            if(instances.Contains(this))
                instances.Remove(this);
        }

        internal void OverrideProperty(object newValue, bool silent = false) {
            if(!isOverridden) runDirtyInstances[this] = cachedValue;
            isOverridden = true;
            UpdateProperty(newValue, silent);
        }

        private void DeferredUpdateProperty(object newValue, bool silent = false) {
            var oldValue = propGetter.Invoke(target, onDict ? new[] {boundKey} : new object[]{ });
            propSetter.Invoke(target, onDict ? new[]{boundKey, newValue} : new[]{newValue});
            var flags = updateEventAttribute?.flags ?? AutoUpdateEventFlags.None;
            if(updateEventAttribute?.ignoreDefault == false) flags |= owner.defaultEnabledUpdateFlags;
            cachedValue = newValue;
            owner.OnConfigChanged(new AutoUpdateEventArgs{
                flags = flags,
                oldValue = oldValue,
                newValue = newValue,
                target = this,
                silent = silent});
        }

        internal void UpdateProperty(object newValue, bool silent = false) {
            if(NetworkServer.active && !this.allowNetMismatch) {
                NetConfig.EnsureOrchestrator();
                NetConfigOrchestrator.instance.ServerAICSyncOneToAll(this, newValue);
            }
            if(deferType == 0 || Run.instance == null || !Run.instance.enabled) {
                DeferredUpdateProperty(newValue, silent);
            } else if(deferType == 1) {
                AutoItemConfig.stageDirtyInstances[this] = (newValue, silent);
            } else if(deferType == 2) {
                AutoItemConfig.runDirtyInstances[this] = newValue;
            } else {
                TILER2Plugin._logger.LogWarning("Something attempted to set the value of an AutoItemConfig with the DeferForever flag: \"" + readablePath + "\"");
            }
        }
    }

    public class AutoItemConfigContainer {
        /// <summary>All config entries generated by AutoItemCfg.Bind will be stored here. Use nameof(targetProperty) to access, if possible (note that this will not protect against type changes while casting to generic ConfigEntry).</summary>
        private readonly List<AutoItemConfig> autoItemConfigs = new List<AutoItemConfig>();

        protected AutoItemConfig FindConfig(string propName) {
            return autoItemConfigs.Find(x => x.boundProperty.Name == propName && !x.onDict);
        }
        protected AutoItemConfig FindConfig(string propName, object dictKey) {
            return autoItemConfigs.Find(x => x.boundProperty.Name == propName && x.onDict && x.boundKey == dictKey);
        }

        /// <summary>Fired when any of the config entries tracked by this AutoItemConfigContainer change.</summary>
        public event EventHandler<AutoUpdateEventArgs> ConfigEntryChanged;
        /// <summary>Internal handler for ConfigEntryChanged event.</summary>
        internal void OnConfigChanged(AutoUpdateEventArgs e) {
            ConfigEntryChanged?.Invoke(this, e);
            TILER2Plugin._logger.LogDebug(e.target.modName + "/" + e.target.configEntry.Definition.Section + "/" + e.target.configEntry.Definition.Key + ": " + e.oldValue.ToString() + " > " + e.newValue.ToString());
            if(!(Run.instance != null && Run.instance.isActiveAndEnabled)) return;
            if((e.flags & AutoUpdateEventFlags.InvalidateStats) == AutoUpdateEventFlags.InvalidateStats)
                AutoItemConfigModule.globalStatsDirty = true;
            if((e.flags & AutoUpdateEventFlags.InvalidateDropTable) == AutoUpdateEventFlags.InvalidateDropTable)
                AutoItemConfigModule.globalDropsDirty = true;
            if(!e.silent && (e.flags & AutoUpdateEventFlags.AnnounceToRun) == AutoUpdateEventFlags.AnnounceToRun && NetworkServer.active)
                NetConfigOrchestrator.ServerSendGlobalChatMsg("The setting <color=#ffffaa>" + e.target.modName + "/" + e.target.configEntry.Definition.Section + "/" + e.target.configEntry.Definition.Key + "</color> has been changed from <color=#ffaaaa>" + e.oldValue.ToString() + "</color> to <color=#aaffaa>" + e.newValue.ToString() + "</color>.");
        }

        private static readonly Dictionary<ConfigFile, DateTime> observedFiles = new Dictionary<ConfigFile, DateTime>();
        private const float filePollingRate = 10f;
        private static float filePollingStopwatch = 0f;

        internal static void FilePollUpdateHook(On.RoR2.RoR2Application.orig_Update orig, RoR2Application self) {
            orig(self);
            filePollingStopwatch += Time.unscaledDeltaTime;
            if(filePollingStopwatch >= filePollingRate) {
                filePollingStopwatch = 0;
                foreach(ConfigFile cfl in observedFiles.Keys.ToList()) {
                    var thisup = System.IO.File.GetLastWriteTime(cfl.ConfigFilePath);
                    if(observedFiles[cfl] < thisup) {
                        observedFiles[cfl] = thisup;
                        TILER2Plugin._logger.LogDebug("A config file tracked by AutoItemConfig has been changed: " + cfl.ConfigFilePath);
                        cfl.Reload();
                    }
                }
            }
        }

        /// <summary>Stores information about an item of a reflected dictionary during iteration.</summary>
        public struct BindSubDictInfo {
            /// <summary>The key of the current element.</summary>
            public object key;
            /// <summary>The value of the current element.</summary>
            public object val;
            /// <summary>The key type of the entire dictionary.</summary>
            public Type keyType;
            /// <summary>The current index of iteration.</summary>
            public int index;
        }
        
        /// <summary>Simple tag replacer for patterns matching &lt;AIC.Param1.Param2...&gt;, using reflection information as the replacing values.<para />
        /// Supported tags: AIC.Prop.[PropName], AIC.DictKey, AIC.DictInd, AIC.DictKeyProp.[PropName]</summary>
        private string ReplaceTags(string orig, PropertyInfo prop, string categoryName, BindSubDictInfo? subDict = null) {
            return Regex.Replace(orig, @"<AIC.([a-zA-Z\.]+)>", (m)=>{
                string[] strParams = Regex.Split(m.Groups[0].Value.Substring(1, m.Groups[0].Value.Length - 2), @"(?<!\\)\.");;
                if(strParams.Length < 2) return m.Value;
                var errorStr = "AutoItemConfig.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" ";
                switch(strParams[1]) {
                    case "Prop":
                        if(strParams.Length < 3){
                            TILER2Plugin._logger.LogWarning(errorStr + "(not enough params for Prop tag).");
                            return m.Value;
                        }
                        var iprop = prop.DeclaringType.GetProperty(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(iprop == null) {
                            TILER2Plugin._logger.LogWarning(errorStr + "(could not find Prop \"" + strParams[2] + "\").");
                            return m.Value;
                        }
                        return iprop.GetValue(this).ToString();
                    case "DictKey":
                        if(!subDict.HasValue) {
                            TILER2Plugin._logger.LogWarning(errorStr + "(DictKey tag used on non-BindDict).");
                            return m.Value;
                        }
                        return subDict.Value.key.ToString();
                    case "DictInd":
                        if(!subDict.HasValue) {
                            TILER2Plugin._logger.LogWarning(errorStr + "(DictInd tag used on non-BindDict).");
                            return m.Value;
                        }
                        return subDict.Value.index.ToString();
                    case "DictKeyProp":
                        if(!subDict.HasValue) {
                            TILER2Plugin._logger.LogWarning(errorStr + "(DictKeyProp tag used on non-BindDict).");
                            return m.Value;
                        }
                        if(strParams.Length < 3){
                            TILER2Plugin._logger.LogWarning(errorStr + "(not enough params for Prop tag).");
                            return m.Value;
                        }
                        PropertyInfo kprop = subDict.Value.key.GetType().GetProperty(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(kprop == null) {
                            TILER2Plugin._logger.LogWarning(errorStr + "(could not find DictKeyProp \"" + strParams[2] + "\").");
                            return m.Value;
                        }
                        return kprop.GetValue(subDict.Value.key).ToString();
                }
                TILER2Plugin._logger.LogWarning(errorStr + "(unknown tag \"" + strParams[1] + "\").");
                return m.Value;
            });
        }
        
        /// <summary>Binds a property to a BepInEx config file, using reflection and attributes to automatically generate much of the necessary information.</summary>
        public void Bind(PropertyInfo prop, ConfigFile cfl, string modName, string categoryName, AutoItemConfigAttribute attrib, AutoUpdateEventInfoAttribute eiattr = null, BindSubDictInfo? subDict = null) {
            string errorStr = "AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + " failed: ";
            if(!subDict.HasValue) {
                if(this.autoItemConfigs.Exists(x => x.boundProperty == prop)) {
                    TILER2Plugin._logger.LogError(errorStr + "this property has already been bound.");
                    return;
                }
                if((attrib.flags & AutoItemConfigFlags.BindDict) == AutoItemConfigFlags.BindDict) {
                    if(!prop.PropertyType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))) {
                        TILER2Plugin._logger.LogError(errorStr + "BindDict flag cannot be used on property types which don't implement IDictionary.");
                        return;
                    }
                    var kTyp = prop.PropertyType.GetGenericArguments()[1];
                    if(attrib.avb != null && attrib.avbType != kTyp) {
                        TILER2Plugin._logger.LogError(errorStr + "dict value and AcceptableValue types must match (received " + kTyp.Name + " and " + attrib.avbType.Name + ").");
                        return;

                    }
                    if(!TomlTypeConverter.CanConvert(kTyp)) {
                        TILER2Plugin._logger.LogError(errorStr + "dict value type cannot be converted by BepInEx.Configuration.TomlTypeConverter (received " + kTyp.Name + ").");
                        return;
                    }
                    var idict = (System.Collections.IDictionary)prop.GetValue(this, null);
                    int ind = 0;
                    var dkeys = (from object k in idict.Keys
                                 select k).ToList();
                    foreach(object o in dkeys) {
                        Bind(prop, cfl, modName, categoryName, attrib, eiattr, new BindSubDictInfo{key=o, val=idict[o], keyType=kTyp, index=ind});
                        ind++;
                    }
                    return;
                }
            }
            if(!subDict.HasValue) {
                if(attrib.avb != null && attrib.avbType != prop.PropertyType) {
                    TILER2Plugin._logger.LogError(errorStr + "property and AcceptableValue types must match (received " + prop.PropertyType.Name + " and " + attrib.avbType.Name + ").");
                    return;
                }
                if(!TomlTypeConverter.CanConvert(prop.PropertyType)) {
                    TILER2Plugin._logger.LogError(errorStr + "property type cannot be converted by BepInEx.Configuration.TomlTypeConverter (received " + prop.PropertyType.Name + ").");
                    return;
                }
            }
            
            object propObj = subDict.HasValue ? prop.GetValue(this) : this;
            var dict = subDict.HasValue ? (System.Collections.IDictionary)propObj : null;
            var propGetter = subDict.HasValue ? dict.GetType().GetProperty("Item").GetGetMethod(true)
                : (prop.GetGetMethod(true) ?? prop.DeclaringType.GetProperty(prop.Name)?.GetGetMethod(true));
            var propSetter = subDict.HasValue ? dict.GetType().GetProperty("Item").GetSetMethod(true)
                : (prop.GetSetMethod(true) ?? prop.DeclaringType.GetProperty(prop.Name)?.GetSetMethod(true));
            var propType = subDict.HasValue ? subDict.Value.keyType : prop.PropertyType;

            if(propGetter == null || propSetter == null) {
                TILER2Plugin._logger.LogError(errorStr + "property (or IDictionary Item property, if using BindDict flag) must have both a getter and a setter.");
                return;
            }

            string cfgName = attrib.name;
            if(cfgName != null) {
                cfgName = ReplaceTags(cfgName, prop, categoryName, subDict);
            } else cfgName = char.ToUpperInvariant(prop.Name[0]) + prop.Name.Substring(1) + (subDict.HasValue ? ":" + subDict.Value.index : "");

            string cfgDesc = attrib.desc;
            if(cfgDesc != null) {
                cfgDesc = ReplaceTags(cfgDesc, prop, categoryName, subDict);
            } else cfgDesc = "Automatically generated from a C# " + (subDict.HasValue ? "dictionary " : "") + "property.";
            
            //Matches ConfigFile.Bind<T>(ConfigDefinition configDefinition, T defaultValue, ConfigDescription configDescription)
            var genm = typeof(ConfigFile).GetMethods().First(
                    x=>x.Name == nameof(ConfigFile.Bind)
                    && x.GetParameters().Length == 3
                    && x.GetParameters()[0].ParameterType == typeof(ConfigDefinition)
                    && x.GetParameters()[2].ParameterType == typeof(ConfigDescription)
                ).MakeGenericMethod(propType);

            var propValue = subDict.HasValue ? subDict.Value.val : prop.GetValue(this);

            bool allowMismatch = (attrib.flags & AutoItemConfigFlags.PreventNetMismatch) != AutoItemConfigFlags.PreventNetMismatch;
            bool deferForever = (attrib.flags & AutoItemConfigFlags.DeferForever) == AutoItemConfigFlags.DeferForever;
            bool deferRun = (attrib.flags & AutoItemConfigFlags.DeferUntilEndGame) == AutoItemConfigFlags.DeferUntilEndGame;
            bool deferStage = (attrib.flags & AutoItemConfigFlags.DeferUntilNextStage) == AutoItemConfigFlags.DeferUntilNextStage;
            bool allowCon = (attrib.flags & AutoItemConfigFlags.PreventConCmd) != AutoItemConfigFlags.PreventConCmd;
            
            if(deferForever && !allowMismatch) {
                cfgDesc += "\nWARNING: THIS SETTING CANNOT BE CHANGED WHILE THE GAME IS RUNNING, AND MUST BE SYNCED MANUALLY FOR MULTIPLAYER!";
            }

            var cfe = (ConfigEntryBase)genm.Invoke(cfl, new[] {
                new ConfigDefinition(categoryName, cfgName),
                propValue,
                new ConfigDescription(cfgDesc,attrib.avb)});

            observedFiles[cfl] = System.IO.File.GetLastWriteTime(cfl.ConfigFilePath);

            var newAIC = new AutoItemConfig {
                boundProperty = prop,
                allowConCmd = allowCon && !deferForever && !deferRun,
                allowNetMismatch = allowMismatch,
                netMismatchCritical = !allowMismatch && deferForever,
                deferType = deferForever ? 3 : (deferRun ? 2 : (deferStage ? 1 : 0)),
                configEntry = cfe,
                modName = modName,
                owner = this,
                propGetter = propGetter,
                propSetter = propSetter,
                propType = propType,
                onDict = subDict.HasValue,
                boundKey = subDict.HasValue ? subDict.Value.key : null,
                updateEventAttribute = eiattr,
                cachedValue = propValue,
                target = propObj
            };

            this.autoItemConfigs.Add(newAIC);

            if(!deferForever) {
                var gtyp = typeof(ConfigEntry<>).MakeGenericType(propType);
                var evh = gtyp.GetEvent("SettingChanged");
                
                evh.ReflAddEventHandler(cfe, (object obj,EventArgs evtArgs) => {
                    newAIC.UpdateProperty(cfe.BoxedValue);
                });
            }

            if((attrib.flags & AutoItemConfigFlags.NoInitialRead) != AutoItemConfigFlags.NoInitialRead) {
                propSetter.Invoke(propObj, subDict.HasValue ? new[]{subDict.Value.key, cfe.BoxedValue} : new[]{cfe.BoxedValue});
                newAIC.cachedValue = cfe.BoxedValue;
            }
        }

        /// <summary>Calls Bind on all properties in this AutoItemConfigContainer which have an AutoItemConfigAttribute.</summary>
        public void BindAll(ConfigFile cfl, string modName, string categoryName) {
            foreach(var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                var attrib = prop.GetCustomAttribute<AutoItemConfigAttribute>(true);
                if(attrib != null)
                    this.Bind(prop, cfl, modName, categoryName, attrib, prop.GetCustomAttribute<AutoUpdateEventInfoAttribute>(true));
            }
        }
        
        /// <summary>All flags that are set here will override unset flags in AutoUpdateEventInfoAttribute, unless attribute.ignoreDefault is true.</summary>
        protected internal AutoUpdateEventFlags defaultEnabledUpdateFlags = AutoUpdateEventFlags.None;

    }


    /// <summary>Used in AutoItemConfigAttribute to modify the behavior of AutoItemConfig.</summary>
    [Flags]
    public enum AutoItemConfigFlags {
        None = 0,
        ///<summary>If UNSET (default): expects acceptableValues to contain 0 or 2 values, which will be added to an AcceptableValueRange. If SET: an AcceptableValueList will be used instead.</summary>
        AVIsList = 1,
        ///<summary>(TODO: needs testing) If SET: will cache config changes, through auto-update or otherwise, and prevent them from applying to the attached property until the next stage transition.</summary>
        DeferUntilNextStage = 2,
        ///<summary>(TODO: needs testing) If SET: will cache config changes, through auto-update or otherwise, and prevent them from applying to the attached property while there is an active run. Takes precedence over DeferUntilNextStage and PreventConCmd.</summary>
        DeferUntilEndGame = 4,
        ///<summary>(TODO: needs testing) If SET: the attached property will never be changed by config. If combined with PreventNetMismatch, mismatches will cause the client to be kicked. Takes precedence over DeferUntilNextStage, DeferUntilEndGame, and PreventConCmd.</summary>
        DeferForever = 8,
        ///<summary>If SET: will prevent the AIC_set console command from being used on this AutoItemConfig.</summary>
        PreventConCmd = 16,
        ///<summary>If SET: will stop the property value from being changed by the initial config read during BindAll.</summary>
        NoInitialRead = 32,
        ///<summary>If SET: the property will temporarily retrieve its value from the host in multiplayer. If combined with DeferForever, mismatches will cause the client to be kicked.</summary>
        PreventNetMismatch = 64,
        ///<summary>If SET: will bind individual items in an IDictionary instead of the entire collection.</summary>
        BindDict = 128
    }

    ///<summary>Used in AutoUpdateEventInfoAttribute to determine which actions should be performed when the property's config entry is updated.</summary>
    ///<remarks>Implementation of these flags is left to classes that inherit AutoItemConfig, except for InvalidateStats and AnnounceToRun.</remarks>
    [Flags]
    public enum AutoUpdateEventFlags {
        None = 0,
        ///<summary>Causes an immediate update to the linked item's language registry.</summary>
        InvalidateNameToken = 1,
        ///<summary>Causes an immediate update to the linked item's language registry.</summary>
        InvalidatePickupToken = 2,
        ///<summary>Causes an immediate update to the linked item's language registry.</summary>
        InvalidateDescToken = 4,
        ///<summary>Causes an immediate update to the linked item's language registry.</summary>
        InvalidateLoreToken = 8,
        ///<summary>Causes an immediate update to the linked item's pickup model.</summary>
        InvalidateModel = 16,
        ///<summary>Causes a next-frame RecalculateStats on all copies of CharacterMaster which are alive and have a CharacterBody.</summary>
        InvalidateStats = 32,
        ///<summary>Causes a next-frame drop table recalculation.</summary>
        InvalidateDropTable = 64,
        ///<summary>Causes an immediate networked chat message informing all players of the updated setting.</summary>
        AnnounceToRun = 128
    }

    public class AutoUpdateEventArgs : EventArgs {
        ///<summary>Any flags passed to the event by an AutoUpdateEventInfoAttribute.</summary>
        public AutoUpdateEventFlags flags;
        ///<summary>The value that the property had before being set.</summary>
        public object oldValue;
        ///<summary>The value that the property has been set to.</summary>
        public object newValue;
        ///<summary>The AutoItemConfig which received an update.</summary>
        public AutoItemConfig target;
        ///<summary>Suppresses the AnnounceToRun flag.</summary>
        public bool silent;
    }
    
    ///<summary>Causes some actions to be automatically performed when a property's config entry is updated.</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AutoUpdateEventInfoAttribute : Attribute {
        public readonly AutoUpdateEventFlags flags;
        public readonly bool ignoreDefault;
        public AutoUpdateEventInfoAttribute(AutoUpdateEventFlags flags, bool ignoreDefault = false) {
            this.flags = flags;
            this.ignoreDefault = ignoreDefault;
        }
    }

    ///<summary>Properties in an AutoItemConfigContainer that have this attribute will be automatically bound to a BepInEx config file when AutoItemConfigContainer.BindAll is called.</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AutoItemConfigAttribute : Attribute {
        public readonly string name = null;
        public readonly string desc = null;
        public readonly AcceptableValueBase avb = null;
        public readonly Type avbType = null;
        public readonly AutoItemConfigFlags flags;
        public AutoItemConfigAttribute(string name, string desc, AutoItemConfigFlags flags = AutoItemConfigFlags.None, params object[] acceptableValues) : this(desc, flags, acceptableValues) {
            this.name = name;
        }

        public AutoItemConfigAttribute(string desc, AutoItemConfigFlags flags = AutoItemConfigFlags.None, params object[] acceptableValues) : this(flags, acceptableValues) {
            this.desc = desc;
        }
        
        public AutoItemConfigAttribute(AutoItemConfigFlags flags = AutoItemConfigFlags.None, params object[] acceptableValues) {
            if(acceptableValues.Length > 0) {
                var avList = (flags & AutoItemConfigFlags.AVIsList) == AutoItemConfigFlags.AVIsList;
                if(!avList && acceptableValues.Length != 2) throw new ArgumentException("Range mode for acceptableValues (flag AVIsList not set) requires either 0 or 2 params; received " + acceptableValues.Length + ".\nThe description provided was: \"" + desc + "\".");
                var iType = acceptableValues[0].GetType();
                for(var i = 1; i < acceptableValues.Length; i++) {
                    if(iType != acceptableValues[i].GetType()) throw new ArgumentException("Types of all acceptableValues must match");
                }
                var avbVariety = avList ? typeof(AcceptableValueList<>).MakeGenericType(iType) : typeof(AcceptableValueRange<>).MakeGenericType(iType);
                this.avb = (AcceptableValueBase)Activator.CreateInstance(avbVariety, acceptableValues);
                this.avbType = iType;
            }
            this.flags = flags;
        }
    }
}
