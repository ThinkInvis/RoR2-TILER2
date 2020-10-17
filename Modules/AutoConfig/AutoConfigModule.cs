using BepInEx.Configuration;
using System;
using RoR2;
using RoR2.Networking;
using UnityEngine.SceneManagement;

namespace TILER2 {
    internal class AutoConfigModule : T2Module<AutoConfigModule> {
        internal static bool globalStatsDirty = false;
        internal static bool globalDropsDirty = false;

        public override void SetupConfig() {
            //this doesn't seem to fire until the title screen is up, which is good because config file changes shouldn't immediately be read during startup; watch for regression (or just implement a check anyways?)
            On.RoR2.RoR2Application.Update += AutoConfigContainer.FilePollUpdateHook;
            
            On.RoR2.Networking.GameNetworkManager.Disconnect += On_GNMDisconnect;
            
            SceneManager.sceneLoaded += Evt_USMSceneLoaded;
        }

        internal static void Update() {
            if(!(Run.instance != null && Run.instance.isActiveAndEnabled)) {
                globalStatsDirty = false;
                globalDropsDirty = false;
            } else {
                if(globalStatsDirty) {
                    globalStatsDirty = false;
                    MiscUtil.AliveList().ForEach(cm => {if(cm.hasBody) cm.GetBody().RecalculateStats();});
                }
                if(globalDropsDirty) {
                    globalDropsDirty = false;
                    Run.instance.BuildDropTable();
                }
            }
        }

        internal static void On_GNMDisconnect(On.RoR2.Networking.GameNetworkManager.orig_Disconnect orig, GameNetworkManager self) {
            orig(self);
            AutoConfigBinding.CleanupDirty(true);
        }

        internal static void Evt_USMSceneLoaded(Scene scene, LoadSceneMode mode) {
            AutoConfigBinding.CleanupDirty(false);
        }
    }

    /// <summary>Used in AutoConfigAttribute to modify the behavior of AutoConfig.</summary>
    [Flags]
    public enum AutoConfigFlags {
        None = 0,
        ///<summary>If UNSET (default): expects acceptableValues to contain 0 or 2 values, which will be added to an AcceptableValueRange. If SET: an AcceptableValueList will be used instead.</summary>
        AVIsList = 1 << 0,
        ///<summary>(TODO: needs testing) If SET: will cache config changes, through auto-update or otherwise, and prevent them from applying to the attached property until the next stage transition.</summary>
        DeferUntilNextStage = 1 << 1,
        ///<summary>(TODO: needs testing) If SET: will cache config changes, through auto-update or otherwise, and prevent them from applying to the attached property while there is an active run. Takes precedence over DeferUntilNextStage and PreventConCmd.</summary>
        DeferUntilEndGame = 1 << 2,
        ///<summary>(TODO: needs testing) If SET: the attached property will never be changed by config. If combined with PreventNetMismatch, mismatches will cause the client to be kicked. Takes precedence over DeferUntilNextStage, DeferUntilEndGame, and PreventConCmd.</summary>
        DeferForever = 1 << 3,
        ///<summary>If SET: will prevent the AIC_set console command from being used on this AutoConfig.</summary>
        PreventConCmd = 1 << 4,
        ///<summary>If SET: will stop the property value from being changed by the initial config read during BindAll.</summary>
        NoInitialRead = 1 << 5,
        ///<summary>If SET: the property will temporarily retrieve its value from the host in multiplayer. If combined with DeferForever, mismatches will cause the client to be kicked.</summary>
        PreventNetMismatch = 1 << 6,
        ///<summary>If SET: will bind individual items in an IDictionary instead of the entire collection.</summary>
        BindDict = 1 << 7
    }

    ///<summary>Used in AutoUpdateEventInfoAttribute to perform some useful stock actions when the property's config entry is updated.</summary>
    ///<remarks>Implementation of these flags is left to classes that inherit AutoConfig, except for InvalidateStats and AnnounceToRun.</remarks>
    [Flags]
    public enum AutoConfigUpdateEventFlags {
        None = 0,
        ///<summary>Causes an immediate update to the linked item's language registry.</summary>
        InvalidateLanguage = 1 << 0,
        ///<summary>Causes an immediate update to the linked item's pickup model.</summary>
        InvalidateModel = 1 << 1,
        ///<summary>Causes a next-frame RecalculateStats on all copies of CharacterMaster which are alive and have a CharacterBody.</summary>
        InvalidateStats = 1 << 2,
        ///<summary>Causes a next-frame drop table recalculation.</summary>
        InvalidateDropTable = 1 << 3,
        ///<summary>Causes an immediate networked chat message informing all players of the updated setting.</summary>
        AnnounceToRun = 1 << 4
    }

    public class AutoConfigUpdateEventArgs : EventArgs {
        ///<summary>Any flags passed to the event by an AutoUpdateEventInfoAttribute.</summary>
        public AutoConfigUpdateEventFlags flags;
        ///<summary>The value that the property had before being set.</summary>
        public object oldValue;
        ///<summary>The value that the property has been set to.</summary>
        public object newValue;
        ///<summary>The AutoItemConfig which received an update.</summary>
        public AutoConfigBinding target;
        ///<summary>Suppresses the AnnounceToRun flag.</summary>
        public bool silent;
    }
    
    ///<summary>Causes some actions to be automatically performed when a property's config entry is updated.</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AutoConfigUpdateEventInfoAttribute : Attribute {
        public readonly AutoConfigUpdateEventFlags flags;
        public readonly bool ignoreDefault;
        public AutoConfigUpdateEventInfoAttribute(AutoConfigUpdateEventFlags flags, bool ignoreDefault = false) {
            this.flags = flags;
            this.ignoreDefault = ignoreDefault;
        }
    }

    ///<summary>Properties in an AutoConfigContainer that have this attribute will be automatically bound to a BepInEx config file when AutoConfigContainer.BindAll is called.</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AutoConfigAttribute : Attribute {
        public readonly string name = null;
        public readonly string desc = null;
        public readonly AcceptableValueBase avb = null;
        public readonly Type avbType = null;
        public readonly AutoConfigFlags flags;
        public AutoConfigAttribute(string name, string desc, AutoConfigFlags flags = AutoConfigFlags.None, params object[] acceptableValues) : this(desc, flags, acceptableValues) {
            this.name = name;
        }

        public AutoConfigAttribute(string desc, AutoConfigFlags flags = AutoConfigFlags.None, params object[] acceptableValues) : this(flags, acceptableValues) {
            this.desc = desc;
        }
        
        public AutoConfigAttribute(AutoConfigFlags flags = AutoConfigFlags.None, params object[] acceptableValues) {
            if(acceptableValues.Length > 0) {
                var avList = (flags & AutoConfigFlags.AVIsList) == AutoConfigFlags.AVIsList;
                if(!avList && acceptableValues.Length != 2) throw new ArgumentException("Range mode for acceptableValues (flag AVIsList not set) requires either 0 or 2 params; received " + acceptableValues.Length + ".\nThe description provided was: \"" + desc + "\".");
                var iType = acceptableValues[0].GetType();
                for(var i = 1; i < acceptableValues.Length; i++) {
                    if(iType != acceptableValues[i].GetType()) throw new ArgumentException("Types of all acceptableValues must match");
                }
                var avbVariety = avList ? typeof(AcceptableValueList<>).MakeGenericType(iType) : typeof(AcceptableValueRange<>).MakeGenericType(iType);
                this.avb = (AcceptableValueBase)Activator.CreateInstance(avbVariety, acceptableValues);
                this.avbType = iType;
            }
            this.flags = flags;
        }
    }
}
