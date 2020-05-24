using BepInEx;
using R2API.Utils;
using R2API;
using BepInEx.Configuration;

namespace TILER2 {
    
    [BepInDependency("com.bepis.r2api")]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(ResourcesAPI), nameof(PlayerAPI), nameof(PrefabAPI), nameof(BuffAPI), nameof(CommandHelper))]
    public class TILER2Plugin:BaseUnityPlugin {
        public const string ModVer = "1.3.0";
        public const string ModName = "TILER2";
        public const string ModGuid = "com.ThinkInvisible.TILER2";

        internal ConfigFile cfgFile;

        internal TILER2Plugin() {}

        public void Awake() {
            cfgFile = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, ModGuid + ".cfg"), true);
            
            NetConfig.Setup(cfgFile);
            StatHooks.Setup();
            AutoItemConfigModule.Setup();
            MiscUtil.Setup();
            ItemBoilerplateModule.Setup();

            CommandHelper.AddToConsoleWhenReady();
        }

        private void Update() {
            AutoItemConfigModule.Update();
        }
    }
}