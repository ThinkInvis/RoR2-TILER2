using RoR2;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TILER2 {
    ///<summary>
    ///Provides safe hooks for the BetterUI mod. Check Compat_BetterUI.enabled before using any other contained members.
    ///</summary>
    [Obsolete("Nonfunctional: BetterUI API has undergone major breaking changes. Will be repaired in a future update.")]
    public static class Compat_BetterUI {
        private static bool? _enabled;
        public static bool enabled {
            get {
                return false;
            }
        }
    }
}
