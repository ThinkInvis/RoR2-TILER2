﻿using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;
using RoR2;
using System.Text.RegularExpressions;
using UnityEngine.Networking;

namespace TILER2 {
    public class AutoConfigContainer {
        /// <summary>All config entries generated by AutoConfigContainer.Bind will be stored here. Use nameof(targetProperty) to access, if possible (note that this will not protect against type changes while casting to generic ConfigEntry).</summary>
        private readonly List<AutoConfigBinding> bindings = new List<AutoConfigBinding>();

        protected AutoConfigBinding FindConfig(string propName) {
            return bindings.Find(x => x.boundProperty.Name == propName && !x.onDict);
        }
        protected AutoConfigBinding FindConfig(string propName, object dictKey) {
            return bindings.Find(x => x.boundProperty.Name == propName && x.onDict && x.boundKey == dictKey);
        }

        /// <summary>Fired when any of the config entries tracked by this AutoItemConfigContainer change.</summary>
        public event EventHandler<AutoConfigUpdateActionEventArgs> ConfigEntryChanged;
        /// <summary>Internal handler for ConfigEntryChanged event.</summary>
        internal void OnConfigChanged(AutoConfigUpdateActionEventArgs e) {
            ConfigEntryChanged?.Invoke(this, e);
            Debug.Log($"{e.target.readablePath}: {e.oldValue} > {e.newValue}");
            if(!(Run.instance != null && Run.instance.isActiveAndEnabled)) return;
            if((e.flags & AutoConfigUpdateActionTypes.InvalidateStats) == AutoConfigUpdateActionTypes.InvalidateStats)
                AutoConfigModule.globalStatsDirty = true;
            if((e.flags & AutoConfigUpdateActionTypes.InvalidateDropTable) == AutoConfigUpdateActionTypes.InvalidateDropTable)
                AutoConfigModule.globalDropsDirty = true;
            if(!e.silent && (e.flags & AutoConfigUpdateActionTypes.AnnounceToRun) == AutoConfigUpdateActionTypes.AnnounceToRun && NetworkServer.active)
                NetUtil.ServerSendGlobalChatMsg($"The setting <color=#ffffaa>{e.target.readablePath}</color> has been changed from <color=#ffaaaa>{e.oldValue}</color> to <color=#aaffaa>{e.newValue}</color>.");
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
                        TILER2Plugin._logger.LogDebug($"A config file tracked by AutoItemConfig has been changed: {cfl.ConfigFilePath}");
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
        /// Supported tags: AIC.Prop.[PropName], AIC.Field.[FieldName], AIC.DictKey, AIC.DictInd, AIC.DictKeyProp.[PropName], AIC.DictKeyField.[FieldName]</summary>
        private string ReplaceTags(string orig, PropertyInfo prop, string categoryName, BindSubDictInfo? subDict = null) {
            return Regex.Replace(orig, @"<AIC.([a-zA-Z\.]+)>", (m)=>{
                string[] strParams = Regex.Split(m.Groups[0].Value.Substring(1, m.Groups[0].Value.Length - 2), @"(?<!\\)\.");;
                if(strParams.Length < 2) return m.Value;
                var errorStr = $"AutoConfigContainer.Bind on property {prop.Name} in category {categoryName}: malformed string param \"{m.Value}\" ";
                switch(strParams[1]) {
                    case "Prop":
                        if(strParams.Length < 3){
                            TILER2Plugin._logger.LogWarning($"{errorStr}(not enough params for Prop tag).");
                            return m.Value;
                        }
                        var iprop = prop.DeclaringType.GetProperty(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(iprop == null) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(could not find Prop \"{strParams[2]}\").");
                            return m.Value;
                        }
                        return iprop.GetValue(this).ToString();
                    case "Field":
                        if(strParams.Length < 3) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(not enough params for Field tag).");
                            return m.Value;
                        }
                        var ifld = prop.DeclaringType.GetField(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(ifld == null) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(could not find Field \"{strParams[2]}\").");
                            return m.Value;
                        }
                        return ifld.GetValue(this).ToString();
                    case "DictKey":
                        if(!subDict.HasValue) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(DictKey tag used on non-BindDict).");
                            return m.Value;
                        }
                        return subDict.Value.key.ToString();
                    case "DictInd":
                        if(!subDict.HasValue) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(DictInd tag used on non-BindDict).");
                            return m.Value;
                        }
                        return subDict.Value.index.ToString();
                    case "DictKeyProp":
                        if(!subDict.HasValue) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(DictKeyProp tag used on non-BindDict).");
                            return m.Value;
                        }
                        if(strParams.Length < 3){
                            TILER2Plugin._logger.LogWarning($"{errorStr}(not enough params for DictKeyProp tag).");
                            return m.Value;
                        }
                        PropertyInfo kprop = subDict.Value.key.GetType().GetProperty(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(kprop == null) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(could not find DictKeyProp \"{strParams[2]}\").");
                            return m.Value;
                        }
                        return kprop.GetValue(subDict.Value.key).ToString();
                    case "DictKeyField":
                        if(!subDict.HasValue) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(DictKeyField tag used on non-BindDict).");
                            return m.Value;
                        }
                        if(strParams.Length < 3) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(not enough params for DictKeyField tag).");
                            return m.Value;
                        }
                        FieldInfo kfld = subDict.Value.key.GetType().GetField(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(kfld == null) {
                            TILER2Plugin._logger.LogWarning($"{errorStr}(could not find DictKeyField \"{strParams[2]}\").");
                            return m.Value;
                        }
                        return kfld.GetValue(subDict.Value.key).ToString();
                }
                TILER2Plugin._logger.LogWarning($"{errorStr}(unknown tag \"{strParams[1]}\").");
                return m.Value;
            });
        }
        
        /// <summary>Binds a property to a BepInEx config file, using reflection and attributes to automatically generate much of the necessary information.</summary>
        public void Bind(PropertyInfo prop, ConfigFile cfl, string modName, string categoryName, AutoConfigAttribute attrib, AutoConfigUpdateActionsAttribute eiattr = null, BindSubDictInfo? subDict = null) {
            string errorStr = $"AutoConfigContainer.Bind on property {prop.Name} in category {categoryName} failed: ";
            if(!subDict.HasValue) {
                if(this.bindings.Exists(x => x.boundProperty == prop)) {
                    TILER2Plugin._logger.LogError($"{errorStr}this property has already been bound.");
                    return;
                }
                if((attrib.flags & AutoConfigFlags.BindDict) == AutoConfigFlags.BindDict) {
                    if(!prop.PropertyType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))) {
                        TILER2Plugin._logger.LogError($"{errorStr}BindDict flag cannot be used on property types which don't implement IDictionary.");
                        return;
                    }
                    var kTyp = prop.PropertyType.GetGenericArguments()[1];
                    if(attrib.avb != null && attrib.avbType != kTyp) {
                        TILER2Plugin._logger.LogError($"{errorStr}dict value and AcceptableValue types must match (received {kTyp.Name} and {attrib.avbType.Name}).");
                        return;

                    }
                    if(!TomlTypeConverter.CanConvert(kTyp)) {
                        TILER2Plugin._logger.LogError($"{errorStr}dict value type cannot be converted by BepInEx.Configuration.TomlTypeConverter (received {kTyp.Name}).");
                        return;
                    }
                    var idict = (System.Collections.IDictionary)prop.GetValue(this, null);
                    int ind = 0;
                    var dkeys = (from object k in idict.Keys
                                 select k).ToList();
                    if(dkeys.Count == 0) {
                        TILER2Plugin._logger.LogError($"{errorStr}BindDict was used on an empty dictionary. All intended keys must be present at time of binding and cannot be added afterwards.");
                    }
                    foreach(object o in dkeys) {
                        Bind(prop, cfl, modName, categoryName, attrib, eiattr, new BindSubDictInfo{key=o, val=idict[o], keyType=kTyp, index=ind});
                        ind++;
                    }
                    return;
                }
            }
            if(!subDict.HasValue) {
                if(attrib.avb != null && attrib.avbType != prop.PropertyType) {
                    TILER2Plugin._logger.LogError($"{errorStr}property and AcceptableValue types must match (received {prop.PropertyType.Name} and {attrib.avbType.Name}).");
                    return;
                }
                if(!TomlTypeConverter.CanConvert(prop.PropertyType)) {
                    TILER2Plugin._logger.LogError($"{errorStr}property type cannot be converted by BepInEx.Configuration.TomlTypeConverter (received {prop.PropertyType.Name}).");
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
                TILER2Plugin._logger.LogError($"{errorStr}property (or IDictionary Item property, if using BindDict flag) must have both a getter and a setter.");
                return;
            }

            string cfgName = attrib.name;
            if(cfgName != null) {
                cfgName = ReplaceTags(cfgName, prop, categoryName, subDict);
            } else cfgName = $"{char.ToUpperInvariant(prop.Name[0])}{prop.Name.Substring(1)}{(subDict.HasValue ? ":" + subDict.Value.index : "")}";

            string cfgDesc = attrib.desc;
            if(cfgDesc != null) {
                cfgDesc = ReplaceTags(cfgDesc, prop, categoryName, subDict);
            } else cfgDesc = $"Automatically generated from a C# {(subDict.HasValue ? "dictionary " : "")}property.";
            
            //Matches ConfigFile.Bind<T>(ConfigDefinition configDefinition, T defaultValue, ConfigDescription configDescription)
            var genm = typeof(ConfigFile).GetMethods().First(
                    x=>x.Name == nameof(ConfigFile.Bind)
                    && x.GetParameters().Length == 3
                    && x.GetParameters()[0].ParameterType == typeof(ConfigDefinition)
                    && x.GetParameters()[2].ParameterType == typeof(ConfigDescription)
                ).MakeGenericMethod(propType);

            var propValue = subDict.HasValue ? subDict.Value.val : prop.GetValue(this);

            bool allowMismatch = (attrib.flags & AutoConfigFlags.PreventNetMismatch) != AutoConfigFlags.PreventNetMismatch;
            bool deferForever = (attrib.flags & AutoConfigFlags.DeferForever) == AutoConfigFlags.DeferForever;
            bool deferRun = (attrib.flags & AutoConfigFlags.DeferUntilEndGame) == AutoConfigFlags.DeferUntilEndGame;
            bool deferStage = (attrib.flags & AutoConfigFlags.DeferUntilNextStage) == AutoConfigFlags.DeferUntilNextStage;
            bool allowCon = (attrib.flags & AutoConfigFlags.PreventConCmd) != AutoConfigFlags.PreventConCmd;
            
            if(deferForever && !allowMismatch) {
                cfgDesc += "\nWARNING: THIS SETTING CANNOT BE CHANGED WHILE THE GAME IS RUNNING, AND MUST BE SYNCED MANUALLY FOR MULTIPLAYER!";
            }

            var cfe = (ConfigEntryBase)genm.Invoke(cfl, new[] {
                new ConfigDefinition(categoryName, cfgName),
                propValue,
                new ConfigDescription(cfgDesc,attrib.avb)});

            observedFiles[cfl] = System.IO.File.GetLastWriteTime(cfl.ConfigFilePath);

            var newBinding = new AutoConfigBinding {
                boundProperty = prop,
                allowConCmd = allowCon && !deferForever && !deferRun,
                allowNetMismatch = allowMismatch,
                netMismatchCritical = !allowMismatch && deferForever,
                deferType = deferForever ? AutoConfigBinding.DeferType.NeverAutoUpdate :
                    (deferRun ? AutoConfigBinding.DeferType.WaitForRunEnd :
                    (deferStage ? AutoConfigBinding.DeferType.WaitForNextStage :
                    AutoConfigBinding.DeferType.UpdateImmediately)),
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

            this.bindings.Add(newBinding);

            if(!deferForever) {
                var gtyp = typeof(ConfigEntry<>).MakeGenericType(propType);
                var evh = gtyp.GetEvent("SettingChanged");
                
                evh.ReflAddEventHandler(cfe, (object obj,EventArgs evtArgs) => {
                    newBinding.UpdateProperty(cfe.BoxedValue);
                });
            }

            if((attrib.flags & AutoConfigFlags.NoInitialRead) != AutoConfigFlags.NoInitialRead) {
                propSetter.Invoke(propObj, subDict.HasValue ? new[]{subDict.Value.key, cfe.BoxedValue} : new[]{cfe.BoxedValue});
                newBinding.cachedValue = cfe.BoxedValue;
            }

            if(Compat_RiskOfOptions.enabled) {
                var errorStr2 = $"AutoConfigContainer.Bind on property {prop.Name} in category {categoryName} could not apply Risk of Options compat: ";
                var containerInfo = this.GetType().GetCustomAttribute<AutoConfigContainerRoOInfoAttribute>();
                var slider = prop.GetCustomAttribute<AutoConfigRoOSliderAttribute>(true);
                var stepslider = prop.GetCustomAttribute<AutoConfigRoOStepSliderAttribute>(true);
                var intslider = prop.GetCustomAttribute<AutoConfigRoOIntSliderAttribute>(true);
                var checkbox = prop.GetCustomAttribute<AutoConfigRoOCheckboxAttribute>(true);
                var choice = prop.GetCustomAttribute<AutoConfigRoOChoiceAttribute>(true);
                var stringinp = prop.GetCustomAttribute<AutoConfigRoOStringAttribute>(true);
                var keybind = prop.GetCustomAttribute<AutoConfigRoOKeybindAttribute>(true);

                string ownerModGuid = null;
                string ownerModName = null;
                bool foundModInfo = false;
                if(containerInfo != null) {
                    ownerModGuid = containerInfo.modGuid;
                    ownerModName = containerInfo.modName;
                    foundModInfo = true;
                } else {
                    var ownerAssembly = Assembly.GetAssembly(this.GetType());
                    var ownerAssemblyTypes = ownerAssembly.GetExportedTypes();
                    foreach(var t in ownerAssemblyTypes) {
                        var attr = t.GetCustomAttribute<BepInEx.BepInPlugin>();
                        if(attr != null) {
                            ownerModGuid = attr.GUID;
                            ownerModName = attr.Name;
                            foundModInfo = true;
                            break;
                        }
                    }
                }

                if(!foundModInfo) {
                    TILER2Plugin._logger.LogError($"{errorStr2}could not find mod info. Declaring type must be in an assembly with a BepInPlugin, or have an AutoConfigContainerRoOInfoAttribute on it.");
                } else {
                    if(slider != null) {
                        if(propType != typeof(float)) {
                            TILER2Plugin._logger.LogError($"{errorStr2}RoOSlider may only be applied to float properties (got {propType.Name}).");
                        } else {
                            var identStrings = new Compat_RiskOfOptions.OptionIdentityStrings {
                                category = slider.catOverride ?? categoryName,
                                name = slider.nameOverride ?? cfgName,
                                description = cfgDesc,
                                modGuid = ownerModGuid,
                                modName = ownerModName
                            };
                            Compat_RiskOfOptions.AddOption_Slider((ConfigEntry<float>)cfe, identStrings,
                                slider.min, slider.max, slider.format,
                                deferForever, () => {
                                    if(deferRun && Run.instance) return true;
                                    return false;
                                });
                        }
                    }
                    if(stepslider != null) {
                        if(propType != typeof(float)) {
                            TILER2Plugin._logger.LogError($"{errorStr2}RoOStepSlider may only be applied to float properties (got {propType.Name}).");
                        } else {
                            var identStrings = new Compat_RiskOfOptions.OptionIdentityStrings {
                                category = stepslider.catOverride ?? categoryName,
                                name = stepslider.nameOverride ?? cfgName,
                                description = cfgDesc,
                                modGuid = ownerModGuid,
                                modName = ownerModName
                            };
                            Compat_RiskOfOptions.AddOption_StepSlider((ConfigEntry<float>)cfe, identStrings,
                                stepslider.min, stepslider.max, stepslider.step, stepslider.format,
                                deferForever, () => {
                                    if(deferRun && Run.instance) return true;
                                    return false;
                                });
                        }
                    }
                    if(intslider != null) {
                        if(propType != typeof(int)) {
                            TILER2Plugin._logger.LogError($"{errorStr2}RoOIntSlider may only be applied to int properties (got {propType.Name}).");
                        } else {
                            var identStrings = new Compat_RiskOfOptions.OptionIdentityStrings {
                                category = intslider.catOverride ?? categoryName,
                                name = intslider.nameOverride ?? cfgName,
                                description = cfgDesc,
                                modGuid = ownerModGuid,
                                modName = ownerModName
                            };
                            Compat_RiskOfOptions.AddOption_IntSlider((ConfigEntry<int>)cfe, identStrings,
                                intslider.min, intslider.max, intslider.format,
                                deferForever, () => {
                                    if(deferRun && Run.instance) return true;
                                    return false;
                                });
                        }
                    }
                    if(choice != null) {
                        if(!propType.IsEnum) {
                            TILER2Plugin._logger.LogError($"{errorStr2}RoOChoice may only be applied to enum properties (got {propType.Name}).");
                        } else {
                            var identStrings = new Compat_RiskOfOptions.OptionIdentityStrings {
                                category = intslider.catOverride ?? categoryName,
                                name = intslider.nameOverride ?? cfgName,
                                description = cfgDesc,
                                modGuid = ownerModGuid,
                                modName = ownerModName
                            };
                            Compat_RiskOfOptions.AddOption_Choice(cfe, identStrings,
                                deferForever, () => {
                                    if(deferRun && Run.instance) return true;
                                    return false;
                                });
                        }
                    }
                    if(keybind != null) {
                        if(propType != typeof(KeyboardShortcut)) {
                            TILER2Plugin._logger.LogError($"{errorStr2}RoOKeybind may only be applied to BepInEx.Configuration.KeyboardShortcut properties (got {propType.Name}).");
                        } else {
                            var identStrings = new Compat_RiskOfOptions.OptionIdentityStrings {
                                category = checkbox.catOverride ?? categoryName,
                                name = checkbox.nameOverride ?? cfgName,
                                description = cfgDesc,
                                modGuid = ownerModGuid,
                                modName = ownerModName
                            };
                            Compat_RiskOfOptions.AddOption_Keybind((ConfigEntry<KeyboardShortcut>)cfe, identStrings,
                                deferForever, () => {
                                    if(deferRun && Run.instance) return true;
                                    return false;
                                });
                        }
                    }
                    if(stringinp != null) {
                        if(propType != typeof(string)) {
                            TILER2Plugin._logger.LogError($"{errorStr2}RoOString may only be applied to string properties (got {propType.Name}).");
                        } else {
                            var identStrings = new Compat_RiskOfOptions.OptionIdentityStrings {
                                category = stringinp.catOverride ?? categoryName,
                                name = stringinp.nameOverride ?? cfgName,
                                description = cfgDesc,
                                modGuid = ownerModGuid,
                                modName = ownerModName
                            };
                            Compat_RiskOfOptions.AddOption_String((ConfigEntry<string>)cfe, identStrings,
                                deferForever, () => {
                                    if(deferRun && Run.instance) return true;
                                    return false;
                                });
                        }
                    }
                    if(checkbox != null) {
                        if(propType != typeof(bool)) {
                            TILER2Plugin._logger.LogError($"{errorStr2}RoOCheckbox may only be applied to bool properties (got {propType.Name}).");
                        } else {
                            var identStrings = new Compat_RiskOfOptions.OptionIdentityStrings {
                                category = checkbox.catOverride ?? categoryName,
                                name = checkbox.nameOverride ?? cfgName,
                                description = cfgDesc,
                                modGuid = ownerModGuid,
                                modName = ownerModName
                            };
                            Compat_RiskOfOptions.AddOption_CheckBox((ConfigEntry<bool>)cfe, identStrings,
                                deferForever, () => {
                                    if(deferRun && Run.instance) return true;
                                    return false;
                                });
                        }
                    }
                }
            }
        }

        /// <summary>Calls Bind on all properties in this AutoConfigContainer which have an AutoConfigAttribute.</summary>
        public void BindAll(ConfigFile cfl, string modName, string categoryName) {
            foreach(var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                var attrib = prop.GetCustomAttribute<AutoConfigAttribute>(true);
                if(attrib != null)
                    this.Bind(prop, cfl, modName, categoryName, attrib, prop.GetCustomAttribute<AutoConfigUpdateActionsAttribute>(true));
            }
        }
        
        /// <summary>All flags that are set here will override unset flags in AutoUpdateEventInfoAttribute, unless attribute.ignoreDefault is true.</summary>
        protected internal virtual AutoConfigUpdateActionTypes defaultEnabledUpdateFlags => AutoConfigUpdateActionTypes.None;
    }
}
