using BepInEx;
using R2API.Utils;
using R2API;
using BepInEx.Configuration;

namespace TILER2 {
    [BepInDependency("com.bepis.r2api", "2.5.14")]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [BepInDependency("com.funkfrog_sipondo.sharesuite",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("dev.ontrigger.itemstats",BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.xoxfaby.BetterUI",BepInDependency.DependencyFlags.SoftDependency)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper), nameof(R2API.Networking.NetworkingAPI))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "2.2.1";
        public const string ModName = "TILER2";
        public const string ModGuid = "com.ThinkInvisible.TILER2";

        internal ConfigFile cfgFile;

        internal TILER2Plugin() {}

        internal static BepInEx.Logging.ManualLogSource _logger;

        public void Awake() {
            _logger = Logger;

            cfgFile = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, ModGuid + ".cfg"), true);
            
            NetConfig.Setup(cfgFile);
            StatHooks.Setup();
            AutoItemConfigModule.Setup();
            MiscUtil.Setup();
            ItemBoilerplateModule.Setup();

            FakeInventory.Setup();
            ItemWard.Setup();

            CommandHelper.AddToConsoleWhenReady();
        }

        private void Update() {
            AutoItemConfigModule.Update();
        }
    }
}