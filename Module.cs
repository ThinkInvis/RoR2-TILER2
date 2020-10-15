using BepInEx.Configuration;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static TILER2.MiscUtil;

namespace TILER2 {
    public abstract class Module<T>:Module where T : Module<T> {
        public static T instance {get;private set;}

        protected Module() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting Module was instantiated twice");
            instance = this as T;
        }
    }

    public abstract class Module : AutoConfigContainer {
        public static FilingDictionary<Module> allModules = new FilingDictionary<Module>();

        public bool enabled {get; protected internal set;} = true;
        
        ///<summary>If true, Module.enabled will be registered as a config entry.</summary>
        public virtual bool managedEnable => false;
        ///<summary>If managedEnable is true, configDescription will be appended to the module's enable/disable config description.</summary>
        public virtual string configDescription => null;
        ///<summary>If managedEnable is true, enabledConfigFlags will be used for the resultant config entry.</summary>
        public virtual AutoConfigFlags enabledConfigFlags => AutoConfigFlags.PreventNetMismatch;
        ///<summary>If managedEnable is true, enabledConfigUpdateEventsFlags will be used for the resultant config entry.</summary>
        public virtual AutoUpdateEventFlags enabledConfigUpdateEventFlags => AutoUpdateEventFlags.InvalidateLanguage;
        
        protected readonly List<LanguageAPI.LanguageOverlay> languageOverlays = new List<LanguageAPI.LanguageOverlay>();
        protected readonly Dictionary<string, string> genericLanguageTokens = new Dictionary<string, string>();
        protected readonly Dictionary<string, Dictionary<string, string>> specificLanguageTokens = new Dictionary<string, Dictionary<string, string>>();
        public bool languageInstalled {get; private set;} = false;

        public virtual void Setup() {
            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if((bool)args.newValue == true) {
                        Install();
                    } else {
                        Uninstall();
                        if(languageInstalled) {
                            UninstallLang();
                            Language.CCLanguageReload(new ConCommandArgs());
                        }
                    }
                }
                if(enabled && args.flags.HasFlag(AutoUpdateEventFlags.InvalidateLanguage)) {
                    if(languageInstalled)
                        UninstallLang();
                    InstallLang();
                    Language.CCLanguageReload(new ConCommandArgs());
                }
            };
        }

        public virtual void Install() {
        }

        public virtual void Uninstall() {
        } 

        ///<summary>Will be called once after initial language setup, and also if/when the module is installed after setup. Automatically loads tokens from the languageTokens dictionary.</summary>
        public virtual void InstallLang() {
            languageOverlays.Add(LanguageAPI.AddOverlay(genericLanguageTokens));
            languageOverlays.Add(LanguageAPI.AddOverlay(specificLanguageTokens));
            languageInstalled = true;
        }

        //Will be called if/when the module is uninstalled after setup.
        public virtual void UninstallLang() {
            foreach(var overlay in languageOverlays) {
                overlay.Remove();
            }
            languageOverlays.Clear();
            languageInstalled = false;
        }

        /// <summary>
        /// Call to scan your plugin's assembly for classes inheriting directly or indirectly from a specific subtype of Module, initialize all of them, and prepare a list for further setup.
        /// </summary>
        /// <param name="cfl">A config file to register AutoConfig entries to.</param>
        /// <param name="modDisplayName">A display name to use for your mod. Mostly used to name config categories in the stock ItemBoilerplate : Module implementations.</param>
        /// <returns>A FilingDictionary containing all instances that this method just initialized.</returns>
        public static FilingDictionary<T> InitAll<T>(ConfigFile cfl, string modDisplayName) where T:Module {
            var f = new FilingDictionary<T>();
            foreach(Type type in Assembly.GetCallingAssembly().GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(T)))) {
                var newModule = InitOne<T>(cfl, modDisplayName);
                f.Add(newModule);
            }
            return f;
        }

        /// <summary>
        /// Call to scan your plugin's assembly for classes inheriting directly from a specific subtype of Module, initialize all of them, and prepare a list for further setup.
        /// Has special handling for the MyClass : ModuleOrModuleSubclass&lt;MyClass&gt; pattern.
        /// </summary>
        /// <param name="cfl">A config file to register AutoConfig entries to.</param>
        /// <param name="modDisplayName">A display name to use for your mod. Mostly used to name config categories in the stock ItemBoilerplate : Module implementations.</param>
        /// <returns>A FilingDictionary containing all instances that this method just initialized.</returns>
        public static FilingDictionary<T> InitDirect<T>(ConfigFile cfl, string modDisplayName) where T:Module {
            var f = new FilingDictionary<T>();
            foreach(Type type in Assembly.GetCallingAssembly().GetTypes().Where(t => t.IsClass && !t.IsAbstract && (t.BaseType.IsGenericType
                ? (t.BaseType.GenericTypeArguments[0] == t && t.BaseType.BaseType == typeof(T))
                : t.BaseType == typeof(T)))) {
                var newModule = InitOne<T>(cfl, modDisplayName);
                f.Add(newModule);
            }
            return f;
        }
        
        /// <summary>
        /// Call to scan your plugin's assembly for classes inheriting directly from Module, initialize all of them, and prepare a list for further setup.
        /// </summary>
        /// <param name="cfl">A config file to register AutoConfig entries to.</param>
        /// <param name="modDisplayName">A display name to use for your mod. Mostly used to name config categories in the stock ItemBoilerplate : Module implementations.</param>
        /// <returns>A FilingDictionary containing all instances that this method just initialized.</returns>
        public static FilingDictionary<Module> InitModules(ConfigFile cfl, string modDisplayName) {
            return InitDirect<Module>(cfl, modDisplayName);
        }

        private static T InitOne<T>(ConfigFile cfl, string modDisplayName) where T:Module {
            var newModule = (T)Activator.CreateInstance(typeof(T));
            if(newModule.managedEnable)
                newModule.Bind(typeof(Module).GetProperty(nameof(enabled)), cfl, modDisplayName, "Modules." + newModule.GetType().Name, new AutoConfigAttribute(
                ((newModule.configDescription != null) ? (newModule.configDescription + "\n") : "") + "Set to False to disable this module and all of its content. Doing so may cause changes in other modules as well.",
                newModule.enabledConfigFlags), newModule.enabledConfigUpdateEventFlags != AutoUpdateEventFlags.None ? new AutoUpdateEventInfoAttribute(newModule.enabledConfigUpdateEventFlags) : null);
            newModule.BindAll(cfl, modDisplayName, "Modules." + newModule.GetType().Name);
            return newModule;
        }

        protected Module() {
            allModules.Add(this);
        } 
    }
}
