using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Networking;

namespace TILER2 {
    public class AutoConfigBinding {
        internal readonly static List<AutoConfigBinding> instances = new List<AutoConfigBinding>();
        internal readonly static Dictionary<AutoConfigBinding, (object, bool)> stageDirtyInstances = new Dictionary<AutoConfigBinding, (object, bool)>();
        internal readonly static Dictionary<AutoConfigBinding, object> runDirtyInstances = new Dictionary<AutoConfigBinding, object>();

        internal static void CleanupDirty(bool isRunEnd) {
            TILER2Plugin._logger.LogDebug("Stage ended; applying " + stageDirtyInstances.Count + " deferred config changes...");
            foreach(AutoConfigBinding k in stageDirtyInstances.Keys) {
                k.DeferredUpdateProperty(stageDirtyInstances[k].Item1, stageDirtyInstances[k].Item2);
            }
            stageDirtyInstances.Clear();
            if(isRunEnd) {
                TILER2Plugin._logger.LogDebug("Run ended; applying " + runDirtyInstances.Count + " deferred config changes...");
                foreach(AutoConfigBinding k in runDirtyInstances.Keys) {
                    k.DeferredUpdateProperty(runDirtyInstances[k], true);
                }
                runDirtyInstances.Clear();
            }
        }

        public AutoConfigContainer owner {get; internal set;}
        public object target {get; internal set;}
        public ConfigEntryBase configEntry {get; internal set;}
        public PropertyInfo boundProperty {get; internal set;}
        public string modName {get; internal set;}

        public AutoConfigUpdateEventInfoAttribute updateEventAttribute {get; internal set;}

        public MethodInfo propGetter {get; internal set;}
        public MethodInfo propSetter {get; internal set;}
        public Type propType {get; internal set;}

        public object boundKey {get; internal set;}
        public bool onDict {get; internal set;}

        public bool allowConCmd {get; internal set;}
        public bool allowNetMismatch {get; internal set;}
        public bool netMismatchCritical {get; internal set;}

        public object cachedValue {get; internal set;}

        internal bool isOverridden = false;

        public enum DeferType {
            UpdateImmediately, WaitForNextStage, WaitForRunEnd, NeverAutoUpdate
        }
        public DeferType deferType {get; internal set;}

        public string readablePath {
            get {return modName + "/" + configEntry.Definition.Section + "/" + configEntry.Definition.Key;}
        }

        internal AutoConfigBinding() {
            instances.Add(this);
        }

        ~AutoConfigBinding() {
            if(instances.Contains(this))
                instances.Remove(this);
        }

        internal void OverrideProperty(object newValue, bool silent = false) {
            if(!isOverridden) runDirtyInstances[this] = cachedValue;
            isOverridden = true;
            UpdateProperty(newValue, silent);
        }

        private void DeferredUpdateProperty(object newValue, bool silent = false) {
            var oldValue = propGetter.Invoke(target, onDict ? new[] {boundKey} : new object[]{ });
            propSetter.Invoke(target, onDict ? new[]{boundKey, newValue} : new[]{newValue});
            var flags = updateEventAttribute?.flags ?? AutoConfigUpdateEventFlags.None;
            if(updateEventAttribute?.ignoreDefault == false) flags |= owner.defaultEnabledUpdateFlags;
            cachedValue = newValue;
            owner.OnConfigChanged(new AutoConfigUpdateEventArgs{
                flags = flags,
                oldValue = oldValue,
                newValue = newValue,
                target = this,
                silent = silent});
        }

        internal void UpdateProperty(object newValue, bool silent = false) {
            if(NetworkServer.active && !this.allowNetMismatch) {
                NetConfig.EnsureOrchestrator();
                NetConfigOrchestrator.instance.ServerAICSyncOneToAll(this, newValue);
            }
            if(deferType == DeferType.UpdateImmediately || Run.instance == null || !Run.instance.enabled) {
                DeferredUpdateProperty(newValue, silent);
            } else if(deferType == DeferType.WaitForNextStage) {
                AutoConfigBinding.stageDirtyInstances[this] = (newValue, silent);
            } else if(deferType == DeferType.WaitForRunEnd) {
                AutoConfigBinding.runDirtyInstances[this] = newValue;
            } else {
                TILER2Plugin._logger.LogWarning("Something attempted to set the value of an AutoConfigBinding with the DeferForever flag: \"" + readablePath + "\"");
            }
        }
    }
}
