using R2API;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Orbs;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace TILER2 {
	[RequireComponent(typeof(NetworkIdentity))]
    public class ItemWard : NetworkBehaviour {
		public enum DisplayPerformanceMode {
			None, OnePerItemIndex, All
		}

		public static GameObject stockIndicatorPrefab;
		public static GameObject displayPrefab;

		public float displayRadiusFracH = 0.5f;
		public float displayRadiusFracV = 0.3f;
		public Vector3 displayIndivScale = Vector3.one;
		public Vector3 displayRadiusOffset = new(0f, 0f, 0f);
		public Transform rangeIndicator;
		public Dictionary<ItemIndex, int> itemcounts = new();

		private const float updateTickRate = 1f;
		private float stopwatch = 0f;
		private TeamFilter teamFilter;
		private TeamComponent teamComponent;
		private float rangeIndicatorScaleVelocity;

		private readonly List<GameObject> displays = new(); //client & server
		private readonly List<Vector3> displayVelocities = new(); //client
		private readonly List<ItemIndex> displayItems = new(); //server
		private readonly List<Inventory> trackedInventories = new(); //server

		private float _radius = 10f;
		public float radius {
			get => _radius;
			set {
				_radius = value;
				radSq = _radius * _radius;
				if(NetworkServer.active)
					new MsgSyncRadius(this, value).Send(R2API.Networking.NetworkDestination.Clients);
			}
		}
		public float radSq { get; private set; } = 100f;
		public TeamIndex currentTeam =>
			teamFilter ? teamFilter.teamIndex
			: (teamComponent ? teamComponent.teamIndex
			: TeamIndex.None);

		internal class ItemWardModule : T2Module<ItemWardModule> {
			[AutoConfigRoOChoice()]
            [AutoConfig("Controls how many item displays are created on ItemWards.", AutoConfigFlags.DeferUntilEndGame)]
			public DisplayPerformanceMode displayPerformanceMode { get; private set; } = DisplayPerformanceMode.All;

			public override void SetupConfig() {
				base.SetupConfig();
				R2API.Networking.NetworkingAPI.RegisterMessageType<MsgDeltaDisplay>();
				R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSyncRadius>();
				
				var displayPrefabPrefab = GameObject.Instantiate(LegacyResourcesAPI.Load<GameObject>("prefabs/effects/orbeffects/ItemTransferOrbEffect"));
				displayPrefabPrefab.GetComponent<EffectComponent>().enabled = false;
				displayPrefabPrefab.GetComponent<OrbEffect>().enabled = false;
				displayPrefabPrefab.GetComponent<ItemTakenOrbEffect>().enabled = false;

				displayPrefab = displayPrefabPrefab.InstantiateClone("ItemWardDisplay", false);

				var indicPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/WarbannerWard").InstantiateClone("TILER2TempSetupPrefab", false);

				var subPrefab = indicPrefab.transform.Find("Indicator").gameObject;
				subPrefab.transform.SetParent(null);
				var ren = subPrefab.transform.Find("IndicatorSphere").gameObject.GetComponent<MeshRenderer>();
				ren.material.SetTexture("_RemapTex",
					Addressables.LoadAssetAsync<Texture2D>("RoR2/Base/Common/ColorRamps/texRampDefault.png")
					.WaitForCompletion());
				/*ren.material.SetTexture("_Cloud2Tex",
					Addressables.LoadAssetAsync<Texture2D>("RoR2/Base/Common/texCloudGradient.png")
					.WaitForCompletion());
				ren.material.SetFloat("_AlphaBoost", 0.5f);*/
				ren.material.SetColor("_CutoffScroll", new Color(0.8f, 0.8f, 0.85f));
				ren.material.SetColor("_RimColor", new Color(0.8f, 0.8f, 0.85f));

				stockIndicatorPrefab = subPrefab.InstantiateClone("ItemWardStockIndicator", false);

				GameObject.Destroy(indicPrefab);
				GameObject.Destroy(subPrefab);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void Awake() {
			teamFilter = base.GetComponent<TeamFilter>();
			teamComponent = base.GetComponent<TeamComponent>();
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void OnDestroy() {
			if(!NetworkServer.active) return;
			foreach(var display in displays) {
				GameObject.Destroy(display);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void OnEnable() {
			if(rangeIndicator)
				rangeIndicator.gameObject.SetActive(true);

			foreach(var display in displays) {
				display.SetActive(true);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void OnDisable() {
			if(this.rangeIndicator)
				this.rangeIndicator.gameObject.SetActive(false);

			foreach(var display in displays) {
				display.SetActive(false);
			}
			
			trackedInventories.RemoveAll(x => !x || !x.gameObject);
			for(var i = trackedInventories.Count - 1; i >= 0; i--) {
				DeregInv(trackedInventories[i]);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void Update() {
			if(!NetworkClient.active) return;
			if(this.rangeIndicator) {
				float num = Mathf.SmoothDamp(rangeIndicator.localScale.x, radius, ref rangeIndicatorScaleVelocity, 0.2f);
				this.rangeIndicator.localScale = new Vector3(num, num, num);
			}

			var totalRotateAmount = -0.125f * (2f * Mathf.PI * Time.time);
			var countAngle = 2f*Mathf.PI/displays.Count;
			var displayRadius = radius * displayRadiusFracH;
			var displayHeight = Mathf.Max(radius * displayRadiusFracV, 1f);
			for(int i = displays.Count - 1; i >= 0; i--) {
				var target = new Vector3(Mathf.Cos(countAngle*i+totalRotateAmount)*displayRadius, displayHeight, Mathf.Sin(countAngle*i+totalRotateAmount)*displayRadius)
					+ displayRadiusOffset;
				var dspv = displayVelocities[i];
				displays[i].transform.localPosition = Vector3.SmoothDamp(displays[i].transform.localPosition, target, ref dspv, 1f);
				displays[i].transform.localScale = displayIndivScale;
				displayVelocities[i] = dspv;
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void FixedUpdate() {
			stopwatch += Time.fixedDeltaTime;
			if(stopwatch > updateTickRate) {
				stopwatch = 0f;
				trackedInventories.RemoveAll(x => !x || !x.gameObject);
				var bodies = (CharacterBody[])UnityEngine.GameObject.FindObjectsOfType<CharacterBody>();
				foreach(var body in bodies) {
					if(body.teamComponent.teamIndex != currentTeam) continue;
					if((body.transform.position - transform.position).sqrMagnitude <= radSq)
						RegObject(body.gameObject);
					else
						DeregObject(body.gameObject);
				}
			}
		}

		private void RegObject(GameObject go) {
			var cb = go.GetComponent<CharacterBody>();
			if(!cb) return;
			var inv = cb.inventory;
			if(inv && !trackedInventories.Contains(inv)) {
				trackedInventories.Add(inv);
				var fakeInv = inv.gameObject.GetComponent<FakeInventory>();
				if(!fakeInv) fakeInv = inv.gameObject.AddComponent<FakeInventory>();
				foreach(var kvp in itemcounts) {
					fakeInv.GiveItem(kvp.Key, kvp.Value);
				}
			}
		}

		private void DeregObject(GameObject go) {
			var cb = go.GetComponent<CharacterBody>();
			if(!cb) return;
			var inv = cb.inventory;
			if(!inv) return;
			DeregInv(inv);
		}

		private void DeregInv(Inventory inv) {
			if(trackedInventories.Contains(inv)) {
				var fakeInv = inv.gameObject.GetComponent<FakeInventory>();
				foreach(var kvp in itemcounts) {
					fakeInv.RemoveItem(kvp.Key, kvp.Value);
				}
				trackedInventories.Remove(inv);
			}
		}

		public void ServerAddItem(ItemIndex ind) {
			if(!NetworkServer.active) return;
			if(!itemcounts.ContainsKey(ind)) itemcounts[ind] = 1;
			else itemcounts[ind]++;
			trackedInventories.RemoveAll(x => !x);
			foreach(var inv in trackedInventories) {
				var fakeInv = inv.gameObject.GetComponent<FakeInventory>();
				fakeInv.GiveItem(ind);
			}
			
			new MsgDeltaDisplay(GetComponent<NetworkIdentity>().netId, ind, true).Send(R2API.Networking.NetworkDestination.Clients);
		}

		public void ServerRemoveItem(ItemIndex ind) {
			if(!NetworkServer.active) return;
			if(!itemcounts.ContainsKey(ind)) return;
			else itemcounts[ind]--;
			if(itemcounts[ind] == 0) itemcounts.Remove(ind);

			new MsgDeltaDisplay(GetComponent<NetworkIdentity>().netId, ind, false).Send(R2API.Networking.NetworkDestination.Clients);

			trackedInventories.RemoveAll(x => !x || !x.gameObject);
			foreach(var inv in trackedInventories) {
				var fakeInv = inv.gameObject.GetComponent<FakeInventory>();
				fakeInv.RemoveItem(ind);
			}
		}

		internal void ClientAddItemDisplay(ItemIndex ind) {
			if(!NetworkServer.active) {
				if(!itemcounts.ContainsKey(ind)) itemcounts[ind] = 1;
				else itemcounts[ind]++;
			}
			if(ItemWardModule.instance.displayPerformanceMode == DisplayPerformanceMode.None) return;
			if(ItemWardModule.instance.displayPerformanceMode == DisplayPerformanceMode.OnePerItemIndex && displayItems.Contains(ind)) return;
			var display = UnityEngine.Object.Instantiate(displayPrefab, transform.position, transform.rotation);
			display.transform.Find("BillboardBase").Find("PickupSprite").GetComponent<SpriteRenderer>().sprite = ItemCatalog.GetItemDef(ind).pickupIconSprite;
			display.transform.parent = this.transform;
			displays.Add(display);
			displayItems.Add(ind);
			displayVelocities.Add(new Vector3(0, 0, 0));
		}

		internal void ClientRemoveItemDisplay(ItemIndex ind) {
			if(!NetworkServer.active) {
				if(!itemcounts.ContainsKey(ind)) return;
				else itemcounts[ind]--;
				if(itemcounts[ind] == 0) itemcounts.Remove(ind);
			}
			if(ItemWardModule.instance.displayPerformanceMode == DisplayPerformanceMode.None) return;
			if(ItemWardModule.instance.displayPerformanceMode == DisplayPerformanceMode.OnePerItemIndex && itemcounts[ind] != 0) return;
			var listInd = displayItems.IndexOf(ind);
			GameObject.Destroy(displays[listInd]);
			displays.RemoveAt(listInd);
			displayItems.RemoveAt(listInd);
			displayVelocities.RemoveAt(listInd);
		}

		protected struct MsgSyncRadius : INetMessage {
			private ItemWard _targetWard;
			private float _newRadius;

			public void Serialize(NetworkWriter writer) {
				writer.Write(_targetWard.gameObject);
				writer.Write(_newRadius);
			}

			public void Deserialize(NetworkReader reader) {
				_targetWard = reader.ReadGameObject().GetComponent<ItemWard>();
				_newRadius = reader.ReadSingle();
			}

			public void OnReceived() {
				_targetWard._radius = _newRadius;
				_targetWard.radSq = _newRadius * _newRadius;
			}

			public MsgSyncRadius(ItemWard targetWard, float newRadius) {
				_targetWard = targetWard;
				_newRadius = newRadius;
			}
		}

		protected struct MsgDeltaDisplay : INetMessage {
			private NetworkInstanceId _ownerNetId;
			private ItemIndex _itemIndex;
			private bool _isAdd;

			public void Serialize(NetworkWriter writer) {
				writer.Write(_ownerNetId);
				writer.Write((int)_itemIndex);
				writer.Write(_isAdd);
			}

			public void Deserialize(NetworkReader reader) {
				_ownerNetId = reader.ReadNetworkId();
				_itemIndex = (ItemIndex)reader.ReadInt32();
				_isAdd = reader.ReadBoolean();
			}

			public void OnReceived() {
				var targetWard = Util.FindNetworkObject(_ownerNetId).GetComponent<ItemWard>();
				if(_isAdd)
					targetWard.ClientAddItemDisplay(_itemIndex);
				else
					targetWard.ClientRemoveItemDisplay(_itemIndex);
			}

			public MsgDeltaDisplay(NetworkInstanceId ownerNetId, ItemIndex itemIndex, bool isAdd) {
				_ownerNetId = ownerNetId;
				_itemIndex = itemIndex;
				_isAdd = isAdd;
			}
		}
    }
}