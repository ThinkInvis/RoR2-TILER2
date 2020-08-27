using R2API;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Orbs;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace TILER2 {
	[RequireComponent(typeof(TeamFilter), typeof(NetworkIdentity))]
    public class ItemWard : NetworkBehaviour {
		public static GameObject displayPrefab;
		internal static void Setup() {
			R2API.Networking.NetworkingAPI.RegisterMessageType<MsgDeltaDisplay>();
			R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSyncRadius>();
			
			var displayPrefabPrefab = GameObject.Instantiate(Resources.Load<GameObject>("prefabs/effects/orbeffects/ItemTransferOrbEffect"));
			displayPrefabPrefab.GetComponent<EffectComponent>().enabled = false;
			displayPrefabPrefab.GetComponent<OrbEffect>().enabled = false;
			displayPrefabPrefab.GetComponent<ItemTakenOrbEffect>().enabled = false;

			displayPrefab = displayPrefabPrefab.InstantiateClone("ItemWardDisplay", false);
		}

		private void Awake() {
			teamFilter = base.GetComponent<TeamFilter>();
		}

		private void OnDestroy() {
			if(!NetworkServer.active) return;
			foreach(var display in displays) {
				GameObject.Destroy(display);
			}
		}

		private void OnEnable() {
			if(rangeIndicator)
				rangeIndicator.gameObject.SetActive(true);

			foreach(var display in displays) {
				display.SetActive(true);
			}
		}

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

		private void Update() {
			if(!NetworkClient.active) return;
			if(this.rangeIndicator) {
				float num = Mathf.SmoothDamp(rangeIndicator.localScale.x, radius, ref rangeIndicatorScaleVelocity, 0.2f);
				this.rangeIndicator.localScale = new Vector3(num, num, num);
			}

			var totalRotateAmount = -0.125f * (2f * Mathf.PI * Time.time);
			var countAngle = 2f*Mathf.PI/displays.Count;
			var displayRadius = radius/2f;
			var displayHeight = Mathf.Max(radius/3f, 1f);
			for(int i = displays.Count - 1; i >= 0; i--) {
				var target = new Vector3(Mathf.Cos(countAngle*i+totalRotateAmount)*displayRadius, displayHeight, Mathf.Sin(countAngle*i+totalRotateAmount)*displayRadius);
				var dspv = displayVelocities[i];
				displays[i].transform.localPosition = Vector3.SmoothDamp(displays[i].transform.localPosition, target, ref dspv, 1f);
				displayVelocities[i] = dspv;
			}
		}

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
			var inv = go.GetComponent<CharacterBody>()?.inventory;
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
			var inv = go.GetComponent<CharacterBody>()?.inventory;
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
			var display = UnityEngine.Object.Instantiate(displayPrefab, transform.position, transform.rotation);
			display.transform.Find("BillboardBase").Find("PickupSprite").GetComponent<SpriteRenderer>().sprite = ItemCatalog.GetItemDef(ind).pickupIconSprite;
			display.transform.parent = this.transform;
			displays.Add(display);
			displayItems.Add(ind);
			displayVelocities.Add(new Vector3(0, 0, 0));
		}

		internal void ClientRemoveItemDisplay(ItemIndex ind) {
			var listInd = displayItems.IndexOf(ind);
			GameObject.Destroy(displays[listInd]);
			displays.RemoveAt(listInd);
			displayItems.RemoveAt(listInd);
			displayVelocities.RemoveAt(listInd);
		}

		private const float updateTickRate = 1f;
		private float stopwatch = 0f;

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
		public float radSq {get; private set;} = 100f;

		public Transform rangeIndicator;
		private TeamFilter teamFilter;
		public TeamIndex currentTeam => teamFilter.teamIndex;

		public Dictionary<ItemIndex, int> itemcounts = new Dictionary<ItemIndex, int>();
		private float rangeIndicatorScaleVelocity;

		private List<GameObject> displays = new List<GameObject>(); //client & server
		private List<Vector3> displayVelocities = new List<Vector3>(); //client
		private List<ItemIndex> displayItems = new List<ItemIndex>(); //server
		private List<Inventory> trackedInventories = new List<Inventory>(); //server

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
				_targetWard.radius = _newRadius;
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