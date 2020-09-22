using System.Runtime.CompilerServices;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the ShareSuite mod. Check Compat_ShareSuite.enabled before using any other contained members.
    ///</summary>
    public static class Compat_ShareSuite {
        //taken from https://github.com/harbingerofme/DebugToolkit/blob/master/Code/DT-Commands/Money.cs
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void GiveMoney(uint amount) {
            ShareSuite.MoneySharingHooks.AddMoneyExternal((int) amount);
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static bool MoneySharing() {
            if (ShareSuite.ShareSuite.MoneyIsShared.Value)
                return true;
            return false;
        }

        private static bool? _enabled;
        public static bool enabled {
            get {
                if(_enabled == null) _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.funkfrog_sipondo.sharesuite");
                return (bool)_enabled;
            }
        }
    }
}