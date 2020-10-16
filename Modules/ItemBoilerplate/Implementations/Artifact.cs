using BepInEx.Configuration;
using R2API;
using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    public abstract class Artifact<T>:Artifact where T : Artifact<T> {
        public static T instance {get;private set;}

        public Artifact() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting ItemBoilerplate/Artifact was instantiated twice");
            instance = this as T;
        }
    }

    public abstract class Artifact : ItemBoilerplate {
        public string iconPathNameDisabled {get; protected set;} = null;

        public ArtifactIndex regIndex {get; private set;}
        public ArtifactDef regDef {get; private set;}
        
        public override void SetupConfig() {
            base.SetupConfig();
            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if(args.oldValue != args.newValue) {
                        if((bool)args.newValue == true) {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage(displayName + " is <color=#aaffaa>NO LONGER FORCE-DISABLED</color>, and it will now take effect if enabled ingame.");
                            regDef.descriptionToken = descToken;
                            regDef.smallIconDeselectedSprite = Resources.Load<Sprite>(iconPathNameDisabled);
                            regDef.smallIconSelectedSprite = Resources.Load<Sprite>(iconPathName);
                        } else {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage(displayName + " has been <color=#ffaaaa>FORCE-DISABLED</color>. If enabled ingame, it will not have any effect.");
                            regDef.descriptionToken = "TILER2_DISABLED_ARTIFACT";
                            regDef.smallIconDeselectedSprite = Resources.Load<Sprite>("textures/miscicons/texUnlockIcon");
                            regDef.smallIconSelectedSprite = Resources.Load<Sprite>("textures/miscicons/texUnlockIcon");
                        }
                    }
                }
            };
        }

        public override void SetupAttributes() {
            base.SetupAttributes();

            regDef = ScriptableObject.CreateInstance<ArtifactDef>();
            regDef.nameToken = nameToken;
            regDef.descriptionToken = descToken;
            regDef.smallIconDeselectedSprite = Resources.Load<Sprite>(iconPathNameDisabled);
            regDef.smallIconSelectedSprite = Resources.Load<Sprite>(iconPathName);
            ArtifactCatalog.getAdditionalEntries += (list) => {
                list.Add(regDef);
            };
            On.RoR2.ArtifactCatalog.SetArtifactDefs += (orig, self) => {
                orig(self);
                regIndex = regDef.artifactIndex;
            };
        }

        public bool IsActiveAndEnabled() {
            return enabled && (RunArtifactManager.instance != null ? RunArtifactManager.instance.IsArtifactEnabled(regIndex) : false);
        }
    }
}
