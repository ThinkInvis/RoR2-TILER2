using BepInEx;
using R2API.Utils;
using R2API;
using BepInEx.Configuration;

namespace TILER2 {
    
    [BepInDependency("com.bepis.r2api")]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper), nameof(R2API.Networking.NetworkingAPI))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "1.5.0";
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