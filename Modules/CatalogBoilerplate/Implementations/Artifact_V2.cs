using BepInEx.Configuration;
using R2API;
using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    public abstract class Artifact_V2<T>:Artifact_V2 where T : Artifact_V2<T> {
        public static T instance {get;private set;}

        public Artifact_V2() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting ItemBoilerplate/Artifact was instantiated twice");
            instance = this as T;
        }
    }

    public abstract class Artifact_V2 : CatalogBoilerplate {
        public string iconPathNameDisabled {get; protected set;} = null;

        public ArtifactIndex catalogIndex {get; private set;}
        public ArtifactDef artifactDef {get; private set;}
        
        public override void SetupConfig() {
            base.SetupConfig();
            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if(args.oldValue != args.newValue) {
                        if((bool)args.newValue == true) {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage(displayName + " is <color=#aaffaa>NO LONGER FORCE-DISABLED</color>, and it will now take effect if enabled ingame.");
                            artifactDef.descriptionToken = descToken;
                            artifactDef.smallIconDeselectedSprite = Resources.Load<Sprite>(iconPathNameDisabled);
                            artifactDef.smallIconSelectedSprite = Resources.Load<Sprite>(iconResourcePath);
                        } else {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage(displayName + " has been <color=#ffaaaa>FORCE-DISABLED</color>. If enabled ingame, it will not have any effect.");
                            artifactDef.descriptionToken = "TILER2_DISABLED_ARTIFACT";
                            artifactDef.smallIconDeselectedSprite = Resources.Load<Sprite>("textures/miscicons/texUnlockIcon");
                            artifactDef.smallIconSelectedSprite = Resources.Load<Sprite>("textures/miscicons/texUnlockIcon");
                        }
                    }
                }
            };
        }

        public override void SetupAttributes() {
            base.SetupAttributes();

            artifactDef = ScriptableObject.CreateInstance<ArtifactDef>();
            artifactDef.nameToken = nameToken;
            artifactDef.descriptionToken = descToken;
            artifactDef.smallIconDeselectedSprite = Resources.Load<Sprite>(iconPathNameDisabled);
            artifactDef.smallIconSelectedSprite = Resources.Load<Sprite>(iconResourcePath);
            ArtifactCatalog.getAdditionalEntries += (list) => {
                list.Add(artifactDef);
            };
            On.RoR2.ArtifactCatalog.SetArtifactDefs += (orig, self) => {
                orig(self);
                catalogIndex = artifactDef.artifactIndex;
            };
        }

        public bool IsActiveAndEnabled() {
            return enabled && (RunArtifactManager.instance != null ? RunArtifactManager.instance.IsArtifactEnabled(catalogIndex) : false);
        }

        public override ConsoleStrings GetConsoleStrings() {
            return new ConsoleStrings {
                className = "Artifact",
                objectName = this.name,
                formattedIndex = ((int)this.catalogIndex).ToString()
            };
        }
    }
}
