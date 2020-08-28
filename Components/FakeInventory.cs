using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API.Networking.Interfaces;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace TILER2 {
	/// <summary>
	/// Keeps track of temporary items. Temporary items cannot be spent at 3D Printers or Scrappers, nor removed by itemsteal; they are intended for removal by mod code only.
	/// Must be attached alongside a normal Inventory. Use GiveItem/RemoveItem/GetItemCount to work with temporary items; use GetRealItemCount to get non-temporary items in the sibling Inventory.
	/// </summary>
	[RequireComponent(typeof(Inventory))]
	public class FakeInventory : NetworkBehaviour {
		private int[] _itemStacks = ItemCatalog.RequestItemStackArray();
		public readonly ReadOnlyCollection<int> itemStacks;
		
		public FakeInventory() {
			itemStacks = new ReadOnlyCollection<int>(_itemStacks);
		}

		private void OnDestroy() {
			ItemCatalog.ReturnItemStackArray(_itemStacks);
		}

		private bool itemsDirty = false;

		private void DeltaItem(ItemIndex ind, int count) {
			_itemStacks[(int)ind] = Mathf.Max(_itemStacks[(int)ind]+count, 0);
			itemsDirty = true;
		}

		public void GiveItem(ItemIndex ind, int count = 1) {
			if(!NetworkServer.active) return;

			if(count <= 0) {
				if(count < 0) RemoveItem(ind, -count);
				return;
			}

			DeltaItem(ind, count);
		}

		public void RemoveItem(ItemIndex ind, int count = 1) {
			if(!NetworkServer.active) return;

			if(count <= 0) {
				if(count < 0) GiveItem(ind, -count);
				return;
			}

			DeltaItem(ind, -count);
		}

		public int GetItemCount(ItemIndex ind) {
			return _itemStacks[(int)ind];
		}

		public int GetRealItemCount(ItemIndex ind) {
			return GetComponent<Inventory>().itemStacks[(int)ind];
		}

		protected struct MsgSyncAll : INetMessage {
			private NetworkInstanceId _ownerNetId;
			private int[] _itemsToSync;
			
			public void Serialize(NetworkWriter writer) {
				writer.Write(_ownerNetId);
				writer.WriteItemStacks(_itemsToSync);
			}

			public void Deserialize(NetworkReader reader) {
				_ownerNetId = reader.ReadNetworkId();
				_itemsToSync = new int[ItemCatalog.itemCount];//ItemCatalog.RequestItemStackArray();
				reader.ReadItemStacks(_itemsToSync);
			}

			public void OnReceived() {
				var obj = Util.FindNetworkObject(_ownerNetId);
				if(!obj) {
					TILER2Plugin._logger.LogWarning($"FakeInventory.MsgSyncAll received for missing NetworkObject with ID {_ownerNetId}");
					return;
				}

				var fakeInv = obj.GetComponent<FakeInventory>();
				if(!fakeInv)
					fakeInv = obj.AddComponent<FakeInventory>();
				
				//haunted! do not use. TODO: exorcise
				//ItemCatalog.ReturnItemStackArray(fakeInv._itemStacks);
				fakeInv._itemStacks = _itemsToSync;

				var inv = fakeInv.GetComponent<Inventory>();
				
				if(NetworkServer.active) {
					inv.SetDirtyBit(1u);
					inv.SetDirtyBit(8u);
				}

				//= inventory.onInventoryChanged.Invoke();
				var multicast = (MulticastDelegate)typeof(Inventory).GetFieldCached(nameof(Inventory.onInventoryChanged)).GetValue(inv);
				foreach(var del in multicast.GetInvocationList()) {
					del.Method.Invoke(del.Target, null);
				}
			}

			public MsgSyncAll(NetworkInstanceId ownerNetId, int[] itemsToSync) {
				_ownerNetId = ownerNetId;
				_itemsToSync = itemsToSync;
			}
		}

		private void Awake() {
			new MsgSyncAll(GetComponent<NetworkIdentity>().netId, _itemStacks).Send(R2API.Networking.NetworkDestination.Clients);
		}

		private void Update() {
			if(itemsDirty && NetworkServer.active) {
				new MsgSyncAll(GetComponent<NetworkIdentity>().netId, _itemStacks).Send(R2API.Networking.NetworkDestination.Clients);
				itemsDirty = false;
			}
		}

		private static bool ignoreFakes = false;

		internal static void Setup() {
			R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSyncAll>();

			On.RoR2.Inventory.GetItemCount += On_InvGetItemCount;
			On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.IsAffordable += LunarItemOrEquipmentCostTypeHelper_IsAffordable;
			On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.PayCost += LunarItemOrEquipmentCostTypeHelper_PayCost;
			On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.PayOne += LunarItemOrEquipmentCostTypeHelper_PayOne;
			On.RoR2.Inventory.HasAtLeastXTotalItemsOfTier += Inventory_HasAtLeastXTotalItemsOfTier;
			On.RoR2.Inventory.GetTotalItemCountOfTier += Inventory_GetTotalItemCountOfTier;
			On.RoR2.ItemStealController.StolenInventoryInfo.StealItem += StolenInventoryInfo_StealItem;
			On.RoR2.RunReport.Generate += RunReport_Generate;
			On.RoR2.ScrapperController.BeginScrapping += ScrapperController_BeginScrapping;
			On.RoR2.ShrineCleanseBehavior.CleanseInventoryServer += ShrineCleanseBehavior_CleanseInventoryServer;
			On.RoR2.ShrineCleanseBehavior.InventoryIsCleansable += ShrineCleanseBehavior_InventoryIsCleansable;
			On.RoR2.Util.GetItemCountForTeam += Util_GetItemCountForTeam;
			IL.RoR2.PickupPickerController.SetOptionsFromInteractor += PickupPickerController_SetOptionsFromInteractor;
            On.RoR2.UI.ItemInventoryDisplay.UpdateDisplay += On_IIDUpdateDisplay;
			On.RoR2.UI.ItemInventoryDisplay.OnInventoryChanged += On_IIDInventoryChanged;

            var cClass = typeof(CostTypeCatalog).GetNestedType("<>c", BindingFlags.NonPublic);
			var subMethod = cClass.GetMethod("<Init>g__PayCostItems|5_1", BindingFlags.NonPublic | BindingFlags.Instance);
            MonoMod.RuntimeDetour.HookGen.HookEndpointManager.Modify(subMethod, (Action<ILContext>)gPayCostItemsHook);
		}

		private static void PickupPickerController_SetOptionsFromInteractor(ILContext il) {
			var c = new ILCursor(il);
			int locIndex = -1;
			c.GotoNext(MoveType.After,
				x => x.MatchLdloc(out locIndex),
				x => x.MatchLdfld<ItemDef>("canRemove"));
			c.Emit(OpCodes.Ldarg_1);
			c.Emit(OpCodes.Ldloc_S, (byte)locIndex);
			c.EmitDelegate<Func<bool,Interactor,ItemDef,bool>>((origDoContinue, iac, def) => {
				var retv = origDoContinue;
				ignoreFakes = true;
				if(iac.GetComponent<CharacterBody>().inventory.GetItemCount(def.itemIndex) <= 0) retv = false;
				ignoreFakes = false;
				return retv;
			});
		}

		private static void gPayCostItemsHook(ILContext il) {
			ILCursor c = new ILCursor(il);
			c.GotoNext(x => x.MatchCallvirt<Inventory>("GetItemCount"));
			c.EmitDelegate<Action>(() => {ignoreFakes = true;});
			c.GotoNext(MoveType.After, x => x.MatchCallvirt<Inventory>("GetItemCount"));
			c.EmitDelegate<Action>(() => {ignoreFakes = false;});
		}

		private static int Util_GetItemCountForTeam(On.RoR2.Util.orig_GetItemCountForTeam orig, TeamIndex teamIndex, ItemIndex itemIndex, bool requiresAlive, bool requiresConnected) {
			ignoreFakes = true;
			var retv = orig(teamIndex, itemIndex, requiresAlive, requiresConnected);
			ignoreFakes = false;
			return retv;
		}

		private static bool ShrineCleanseBehavior_InventoryIsCleansable(On.RoR2.ShrineCleanseBehavior.orig_InventoryIsCleansable orig, Inventory inventory) {
			ignoreFakes = true;
			var retv = orig(inventory);
			ignoreFakes = false;
			return retv;
		}

		private static int ShrineCleanseBehavior_CleanseInventoryServer(On.RoR2.ShrineCleanseBehavior.orig_CleanseInventoryServer orig, Inventory inventory) {
			ignoreFakes = true;
			var retv = orig(inventory);
			ignoreFakes = false;
			return retv;
		}

		private static void ScrapperController_BeginScrapping(On.RoR2.ScrapperController.orig_BeginScrapping orig, ScrapperController self, int intPickupIndex) {
			ignoreFakes = true;
			orig(self, intPickupIndex);
			ignoreFakes = false;
		}

		private static RunReport RunReport_Generate(On.RoR2.RunReport.orig_Generate orig, Run run, GameEndingDef gameEnding) {
			ignoreFakes = true;
			var retv = orig(run, gameEnding);
			ignoreFakes = false;
			return retv;
		}

		private static int StolenInventoryInfo_StealItem(On.RoR2.ItemStealController.StolenInventoryInfo.orig_StealItem orig, object self, ItemIndex itemIndex, int maxStackToSteal) {
			ignoreFakes = true;
			var retv = orig(self, itemIndex, maxStackToSteal);
			ignoreFakes = false;
			return retv;
		}

		private static int Inventory_GetTotalItemCountOfTier(On.RoR2.Inventory.orig_GetTotalItemCountOfTier orig, Inventory self, ItemTier itemTier) {
			ignoreFakes = true;
			var retv = orig(self, itemTier);
			ignoreFakes = false;
			return retv;
		}

		private static bool Inventory_HasAtLeastXTotalItemsOfTier(On.RoR2.Inventory.orig_HasAtLeastXTotalItemsOfTier orig, Inventory self, ItemTier itemTier, int x) {
			ignoreFakes = true;
			var retv = orig(self, itemTier, x);
			ignoreFakes = false;
			return retv;
		}

		private static void LunarItemOrEquipmentCostTypeHelper_PayOne(On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.orig_PayOne orig, Inventory inventory) {
			ignoreFakes = true;
			orig(inventory);
			ignoreFakes = false;
		}

		private static void LunarItemOrEquipmentCostTypeHelper_PayCost(On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.orig_PayCost orig, CostTypeDef costTypeDef, CostTypeDef.PayCostContext context) {
			ignoreFakes = true;
			orig(costTypeDef, context);
			ignoreFakes = false;
		}

		private static bool LunarItemOrEquipmentCostTypeHelper_IsAffordable(On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.orig_IsAffordable orig, CostTypeDef costTypeDef, CostTypeDef.IsAffordableContext context) {
			ignoreFakes = true;
			var retv = orig(costTypeDef, context);
			ignoreFakes = false;
			return retv;
		}
		
		private static int On_InvGetItemCount(On.RoR2.Inventory.orig_GetItemCount orig, Inventory self, ItemIndex itemIndex) {
			var origVal = orig(self, itemIndex);
			if(ignoreFakes || !self) return origVal;
			var fakeinv = self.gameObject.GetComponent<FakeInventory>();
			if(!fakeinv) return origVal;
			return origVal + fakeinv._itemStacks[(int)itemIndex];//fakeinv.GetItemCount(itemIndex);
		}
		
		private static void On_IIDInventoryChanged(On.RoR2.UI.ItemInventoryDisplay.orig_OnInventoryChanged orig, RoR2.UI.ItemInventoryDisplay self) {
			orig(self);
			if(!self || !self.isActiveAndEnabled || !self.inventory) return;
			var fakeInv = self.inventory.GetComponent<FakeInventory>();
			if(!fakeInv) return;
			List<ItemIndex> newAcqOrder = new List<ItemIndex>(self.inventory.itemAcquisitionOrder);
			for(int i = 0; i < self.itemStacks.Length; i++) {
				if(fakeInv._itemStacks[i] > 0 && self.itemStacks[i] == 0) {
					newAcqOrder.Add((ItemIndex)i);
				}
				self.itemStacks[i] += fakeInv._itemStacks[i];
			}
			newAcqOrder.CopyTo(self.itemOrder);
			self.itemOrderCount = newAcqOrder.Count;
		}

        private static void On_IIDUpdateDisplay(On.RoR2.UI.ItemInventoryDisplay.orig_UpdateDisplay orig, RoR2.UI.ItemInventoryDisplay self) {
            orig(self);
            Inventory inv = self.inventory;
			if(!inv) return;
            var fakeInv = inv.gameObject.GetComponent<FakeInventory>();
			if(!fakeInv) return;
            foreach(var icon in self.itemIcons) {
				var fakeCount = fakeInv.GetItemCount(icon.itemIndex);
				if(fakeCount == 0) continue;
				var realCount = fakeInv.GetRealItemCount(icon.itemIndex);
				icon.stackText.enabled = true;
				icon.stackText.text = $"x{realCount}\n<color=#C18FE0>+{fakeCount}</color>";
            }
        }
	}
}