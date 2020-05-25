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
            this.itemCodeName = typeof(T).Name;
            instance = this as T;
        }
    }

    public abstract class Artifact : ItemBoilerplate {
        protected override string NewLangLore(string langID = null) => null;
        protected override string NewLangPickup(string langID = null) => null;

        public string iconPathNameDisabled {get; protected set;} = null;

        public ArtifactIndex regIndex {get; private set;}
        public ArtifactDef regDef {get; private set;}
        
        protected event Action<ConfigFile> preConfig;
        protected event Action<ConfigFile> postConfig;
        protected event Action<string, string> onAttrib;
        protected event Action onBehav;

        public override void SetupConfig(ConfigFile cfl) {
            if(configDone) {
                TILER2Plugin._logger.LogError("Something tried to setup config for an artifact twice");
                return;
            }
            configDone = true;

            preConfig?.Invoke(cfl);

            this.BindAll(cfl, modName, "Artifacts." + itemCodeName);

            postConfig?.Invoke(cfl);
            
            ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(enabled)) {
                    if(args.oldValue != args.newValue) {
                        if((bool)args.newValue == true) {
                            LoadBehavior();
                            if(Run.instance?.enabled == true) Chat.AddMessage(displayName + " is <color=#aaffaa>NO LONGER FORCE-DISABLED</color>, and it will now take effect if enabled ingame.");
                            regDef.descriptionToken = descToken;
                            regDef.smallIconDeselectedSprite = Resources.Load<Sprite>(iconPathNameDisabled);
                            regDef.smallIconSelectedSprite = Resources.Load<Sprite>(iconPathName);
                        } else {
                            UnloadBehavior();
                            if(Run.instance?.enabled == true) Chat.AddMessage(displayName + " has been <color=#ffaaaa>FORCE-DISABLED</color>. If enabled ingame, it will not have any effect.");
                            regDef.descriptionToken = "TILER2_DISABLED_ARTIFACT";
                            regDef.smallIconDeselectedSprite = Resources.Load<Sprite>("textures/miscicons/texUnlockIcon");
                            regDef.smallIconSelectedSprite = Resources.Load<Sprite>("textures/miscicons/texUnlockIcon");
                        }
                    }
                }
            };
        }
        public override void SetupAttributes(string modTokenIdent, string modCNamePrefix = "") {
            if(attributesDone) {
                TILER2Plugin._logger.LogError("Something tried to setup attributes for an artifact twice");
                return;
            }
            attributesDone = true;

            nameToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_NAME";
            descToken = modTokenIdent + "_" + itemCodeName.ToUpper() + "_DESC";

            onAttrib?.Invoke(modTokenIdent, modCNamePrefix);

            RegLang();
            
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

        public override void SetupBehavior() {
            if(behaviorDone) {
                TILER2Plugin._logger.LogError("Something tried to setup behavior for an artifact twice");
                return;
            }
            behaviorDone = true;

            onBehav?.Invoke();

            if(enabled)
                LoadBehavior();
        }

        protected override void LoadBehavior() {}
        protected override void UnloadBehavior() {}

        public bool IsActiveAndEnabled() {
            return enabled && (RunArtifactManager.instance?.IsArtifactEnabled(regIndex) ?? false);
        }
    }
}
