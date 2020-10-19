using BepInEx;
using R2API.Utils;
using R2API;
using BepInEx.Configuration;
using static TILER2.MiscUtil;

namespace TILER2 {
    [BepInDependency("com.bepis.r2api", "2.5.14")]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [BepInDependency("com.funkfrog_sipondo.sharesuite",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("dev.ontrigger.itemstats",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.xoxfaby.BetterUI",BepInDependency.DependencyFlags.SoftDependency)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper), nameof(R2API.Networking.NetworkingAPI))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "3.0.3";
        public const string ModName = "TILER2";
        public const string ModGuid = "com.ThinkInvisible.TILER2";

        internal ConfigFile cfgFile;

        internal TILER2Plugin() {}

        internal static BepInEx.Logging.ManualLogSource _logger;

        private FilingDictionary<T2Module> allModules;

        public void Awake() {
            _logger = Logger;

            cfgFile = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, ModGuid + ".cfg"), true);

            T2Module.SetupModuleClass();

            allModules = T2Module.InitModules(new T2Module.ModInfo {
                displayName="TILER2",
                mainConfigFile=cfgFile,
                longIdentifier="TILER2",
                shortIdentifier="TILER2"
            });

            T2Module.SetupAll_PluginAwake(allModules);

            AutoItemConfigModule.Setup();
            ItemBoilerplateModule.Setup();
            MiscUtil.Setup();
            DebugUtil.Setup();

            CommandHelper.AddToConsoleWhenReady();
        }

        private void Start() {
            T2Module.SetupAll_PluginStart(allModules);
        }

        private void Update() {
            AutoConfigModule.Update();
            AutoItemConfigModule.Update();
        }
    }
}