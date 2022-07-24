using BepInEx;
using R2API.Utils;
using R2API;
using BepInEx.Configuration;
using static TILER2.MiscUtil;

namespace TILER2 {
    [BepInDependency(R2API.R2API.PluginGUID, R2API.R2API.PluginVersion)]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [BepInDependency("com.funkfrog_sipondo.sharesuite",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI),
        nameof(PrefabAPI), nameof(CommandHelper), nameof(R2API.Networking.NetworkingAPI))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "7.2.1";
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

            NetUtil.Setup();
            MiscUtil.Setup();
            DebugUtil.Setup();

            CommandHelper.AddToConsoleWhenReady();
        }

        private void Start() {
            T2Module.SetupAll_PluginStart(allModules);
        }

        private void Update() {
            if(!RoR2.RoR2Application.loadFinished) return;
            AutoConfigModule.Update();
        }
    }
}