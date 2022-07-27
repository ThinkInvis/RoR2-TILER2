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
                            if(Run.instance && Run.instance.enabled)
                                Chat.AddMessage(Language.GetStringFormatted("TILER2_CHAT_ARTIFACT_ENABLED", Language.GetString(nameToken + "_RENDERED")));
                            artifactDef.descriptionToken = descToken;
                            artifactDef.smallIconDeselectedSprite = iconResourceDisabled;
                            artifactDef.smallIconSelectedSprite = iconResource;
                        } else {
                            if(Run.instance && Run.instance.enabled)
                                Chat.AddMessage(Language.GetStringFormatted("TILER2_CHAT_ARTIFACT_DISABLED", Language.GetString(nameToken + "_RENDERED")));
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
            artifactDef.nameToken = nameToken + "_RENDERED";
            artifactDef.descriptionToken = descToken + "_RENDERED";
            artifactDef.smallIconDeselectedSprite = iconResourceDisabled;
            artifactDef.smallIconSelectedSprite = iconResource;

            SetupModifyArtifactDef();

            ContentAddition.AddArtifactDef(artifactDef);

            ArtifactCatalog.availability.CallWhenAvailable(this.SetupCatalogReady);
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
            return enabled && RunArtifactManager.instance && RunArtifactManager.instance.IsArtifactEnabled(catalogIndex);
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
