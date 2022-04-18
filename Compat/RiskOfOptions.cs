using BepInEx.Configuration;
using System;
using System.Runtime.CompilerServices;
using RiskOfOptions;
using RiskOfOptions.Options;
using RiskOfOptions.OptionConfigs;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the RiskOfOptions mod. Check Compat_RiskOfOptions.enabled before using any other contained members.
    ///</summary>
    public static class Compat_RiskOfOptions {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddOption_CheckBox(ConfigEntry<bool> configEntry, string category, string name, string description, bool restartRequired, Func<bool> isDisabledDelegate) {
            ModSettingsManager.AddOption(new CheckBoxOption(configEntry, new CheckBoxConfig {
                category = category,
                name = name,
                restartRequired = restartRequired,
                description = description,
                checkIfDisabled = () => { return isDisabledDelegate(); }
            }));
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void AddOption_Slider(ConfigEntry<float> configEntry, float min, float max, string formatString, string category, string name, string description, bool restartRequired, Func<bool> isDisabledDelegate) {
            ModSettingsManager.AddOption(new SliderOption(configEntry, new SliderConfig {
                category = category,
                name = name,
                max = max,
                min = min,
                formatString = formatString,
                restartRequired = restartRequired,
                description = description,
                checkIfDisabled = () => { return isDisabledDelegate(); }
            }));
        }

        private static bool? _enabled;
        public static bool enabled {
            get {
                if(_enabled == null) _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");
                return (bool)_enabled;
            }
        }
    }
}
