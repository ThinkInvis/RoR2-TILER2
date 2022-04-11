using R2API;
using RoR2;
using System;
using UnityEngine;

namespace TILER2 {
    public abstract class Artifact<T>:Artifact where T : Artifact<T> {
        public static T instance {get;private set;}

        public Artifact() {
            if(instance != null) throw new InvalidOperationException("Singleton class \"" + typeof(T).Name + "\" inheriting CatalogBoilerplate/Artifact was instantiated twice");
            instance = this as T;
        }
    }

    public abstract class Artifact : CatalogBoilerplate {
        public override string configCategoryPrefix => "Artifacts.";
        protected override string GetLoreString(string langID = null) => null;
        protected override string GetPickupString(string langID = null) => null;

        public Sprite iconResourceDisabled {get; protected set;} = null;

        public ArtifactIndex catalogIndex => artifactDef.artifactIndex;
        public ArtifactDef artifactDef {get; private set;}
        
        public override void SetupConfig() {
            base.SetupConfig();
            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if(args.oldValue != args.newValue) {
                        if((bool)args.newValue == true) {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage(displayName + " is <color=#aaffaa>NO LONGER FORCE-DISABLED</color>, and it will now take effect if enabled ingame.");
                            artifactDef.descriptionToken = descToken;
                            artifactDef.smallIconDeselectedSprite = iconResourceDisabled;
                            artifactDef.smallIconSelectedSprite = iconResource;
                        } else {
                            if(Run.instance != null && Run.instance.enabled) Chat.AddMessage(displayName + " has been <color=#ffaaaa>FORCE-DISABLED</color>. If enabled ingame, it will not have any effect.");
                            artifactDef.descriptionToken = "TILER2_DISABLED_ARTIFACT";
                            artifactDef.smallIconDeselectedSprite = LegacyResourcesAPI.Load<Sprite>("textures/miscicons/texUnlockIcon");
                            artifactDef.smallIconSelectedSprite = LegacyResourcesAPI.Load<Sprite>("textures/miscicons/texUnlockIcon");
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
            artifactDef.smallIconDeselectedSprite = iconResourceDisabled;
            artifactDef.smallIconSelectedSprite = iconResource;

            SetupModifyArtifactDef();

            ContentAddition.AddArtifactDef(artifactDef);
        }

        public virtual void SetupModifyArtifactDef() {}

        public override void Install() {
            base.Install();
            artifactDef.smallIconDeselectedSprite = iconResourceDisabled;
            artifactDef.smallIconSelectedSprite = iconResource;
        }

        public override void Uninstall() {
            base.Uninstall();
            artifactDef.smallIconDeselectedSprite = CatalogBoilerplateModule.lockIcon;
            artifactDef.smallIconSelectedSprite = CatalogBoilerplateModule.lockIcon;
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
