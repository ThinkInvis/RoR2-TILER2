using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API.Networking.Interfaces;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
		internal class FakeInventoryModule : T2Module<FakeInventoryModule> {
			public override void SetupConfig() {
				base.SetupConfig();
				R2API.Networking.NetworkingAPI.RegisterMessageType<MsgSyncAll>();

				//Main itemcount handler
				On.RoR2.Inventory.GetItemCount_ItemIndex += On_InvGetItemCountByIndex;

				//Ignore fake items in:
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
                On.RoR2.Items.ContagiousItemManager.StepInventoryInfection += ContagiousItemManager_StepInventoryInfection;
                On.RoR2.Items.ContagiousItemManager.OnInventoryChangedGlobal += ContagiousItemManager_OnInventoryChangedGlobal;
                On.RoR2.Items.SuppressedItemManager.OnInventoryChangedGlobal += SuppressedItemManager_OnInventoryChangedGlobal;
                On.RoR2.Items.SuppressedItemManager.SuppressItem += SuppressedItemManager_SuppressItem;
                On.RoR2.Items.SuppressedItemManager.TransformItem += SuppressedItemManager_TransformItem;
                On.RoR2.CharacterMaster.TryCloverVoidUpgrades += CharacterMaster_TryCloverVoidUpgrades;
                On.RoR2.ArtifactTrialMissionController.RemoveAllMissionKeys += ArtifactTrialMissionController_RemoveAllMissionKeys;
                On.RoR2.ItemStealController.StolenInventoryInfo.TakeItemFromLendee += StolenInventoryInfo_TakeItemFromLendee;
                On.RoR2.ItemStealController.StolenInventoryInfo.TakeBackItemsFromLendee += StolenInventoryInfo_TakeBackItemsFromLendee;
                On.RoR2.LunarSunBehavior.FixedUpdate += LunarSunBehavior_FixedUpdate;

                //Stack checker
                On.RoR2.Run.FixedUpdate += Run_FixedUpdate;

				//Display hooks
				On.RoR2.UI.ItemInventoryDisplay.UpdateDisplay += On_IIDUpdateDisplay;
				On.RoR2.UI.ItemInventoryDisplay.OnInventoryChanged += On_IIDInventoryChanged;

				var cClass = typeof(CostTypeCatalog).GetNestedType("<>c", BindingFlags.NonPublic);
				var subMethod = cClass.GetMethod("<Init>g__PayCostItems|5_1", BindingFlags.NonPublic | BindingFlags.Instance);
				MonoMod.RuntimeDetour.HookGen.HookEndpointManager.Modify(subMethod, (Action<ILContext>)gPayCostItemsHook);
			}
        }

		private int[] _itemStacks = ItemCatalog.RequestItemStackArray();
		public readonly ReadOnlyCollection<int> itemStacks;
		
		///<summary>Items in this HashSet cannot be added to nor removed from a FakeInventory.</summary>
		public static HashSet<ItemDef> blacklist = new();

		public FakeInventory() {
			itemStacks = new ReadOnlyCollection<int>(_itemStacks);
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void OnDestroy() {
			ItemCatalog.ReturnItemStackArray(_itemStacks);
		}

		private bool itemsDirty = false;

		private void DeltaItem(ItemIndex ind, int count) {
			if(blacklist.Contains(ItemCatalog.GetItemDef(ind))) return;
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
			return HG.ArrayUtils.GetSafe(_itemStacks, (int)ind);
		}

		public int GetRealItemCount(ItemIndex ind) {
			return HG.ArrayUtils.GetSafe(GetComponent<Inventory>().itemStacks, (int)ind);
		}

		public int GetAdjustedItemCount(ItemIndex ind) {
			var tsfi = RoR2.Items.ContagiousItemManager.GetTransformedItemIndex(ind);
			//can't use GetOriginalItemIndex because some Void items transform many:one (e.g. bands)
			var utsfi = RoR2.Items.ContagiousItemManager.transformationInfos.Where(x => x.transformedItem == ind);
			var canTransform = tsfi != ItemIndex.None;
			var isTransformed = utsfi.Count() > 0;
			var origVal = GetRealItemCount(ind);
			if(canTransform && GetRealItemCount(tsfi) > 0) {
				//if player has transformed version of this item,
				//add nothing; will add to transformed item count instead.
				return origVal;
			} else if(isTransformed && origVal > 0) {
				//if player has this item and it has an untransformed version within this FakeInventory,
				//add latter to count of former.
				return origVal + utsfi.Sum(x => GetItemCount(x.originalItem)) + GetItemCount(ind);
			}
			//player does not have transformed or untransformed version of this item, or item is not part of a transform chain
			return origVal + GetItemCount(ind);
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

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void Awake() {
			if(NetworkServer.active) {
				var netId = GetComponent<NetworkIdentity>().netId;
				if(netId.Value == 0) return;
				new MsgSyncAll(netId, _itemStacks).Send(R2API.Networking.NetworkDestination.Clients);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine.")]
		private void Update() {
			if(itemsDirty && NetworkServer.active) {
				var netId = GetComponent<NetworkIdentity>().netId;
				if(netId.Value == 0) return;
				new MsgSyncAll(netId, _itemStacks).Send(R2API.Networking.NetworkDestination.Clients);
				itemsDirty = false;
			}
		}

		/// <summary>Increment when beginning a method using RemoveItem, or any other method where FakeInventory items should be ignored while calculating item count. Decrement when leaving the method.</summary>
		public static int ignoreFakes = 0;

        #region IgnoreFakes hooks
        private static void Run_FixedUpdate(On.RoR2.Run.orig_FixedUpdate orig, Run self) {
			orig(self);
			if(ignoreFakes != 0) {
				TILER2Plugin._logger.LogError($"FakeInventory ignoreFakes count = {ignoreFakes} on new frame (!= 0, very bad!), clearing");
				ignoreFakes = 0;
			}
		}

		private static void StolenInventoryInfo_TakeBackItemsFromLendee(On.RoR2.ItemStealController.StolenInventoryInfo.orig_TakeBackItemsFromLendee orig, object self) {
			ignoreFakes++;
			orig(self);
			ignoreFakes--;
		}

		private static void LunarSunBehavior_FixedUpdate(On.RoR2.LunarSunBehavior.orig_FixedUpdate orig, LunarSunBehavior self) {
			ignoreFakes++;
			orig(self);
			ignoreFakes--;
		}

		private static int StolenInventoryInfo_TakeItemFromLendee(On.RoR2.ItemStealController.StolenInventoryInfo.orig_TakeItemFromLendee orig, object self, ItemIndex itemIndex, int maxStackToTake) {
			ignoreFakes++;
			var retv = orig(self, itemIndex, maxStackToTake);
			ignoreFakes--;
			return retv;
		}

		private static void ArtifactTrialMissionController_RemoveAllMissionKeys(On.RoR2.ArtifactTrialMissionController.orig_RemoveAllMissionKeys orig) {
			ignoreFakes++;
			orig();
			ignoreFakes--;
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
				ignoreFakes++;
				if(iac.GetComponent<CharacterBody>().inventory.GetItemCount(def.itemIndex) <= 0) retv = false;
				ignoreFakes--;
				return retv;
			});
		}

		private static void gPayCostItemsHook(ILContext il) {
			ILCursor c = new(il);
			c.GotoNext(x => x.MatchCallvirt<Inventory>("GetItemCount"));
			c.EmitDelegate<Action>(() => {ignoreFakes++;});
			c.GotoNext(MoveType.After, x => x.MatchCallvirt<Inventory>("GetItemCount"));
			c.EmitDelegate<Action>(() => {ignoreFakes--;});
		}

		private static int Util_GetItemCountForTeam(On.RoR2.Util.orig_GetItemCountForTeam orig, TeamIndex teamIndex, ItemIndex itemIndex, bool requiresAlive, bool requiresConnected) {
			ignoreFakes++;
			var retv = orig(teamIndex, itemIndex, requiresAlive, requiresConnected);
			ignoreFakes--;
			return retv;
		}

		private static bool ShrineCleanseBehavior_InventoryIsCleansable(On.RoR2.ShrineCleanseBehavior.orig_InventoryIsCleansable orig, Inventory inventory) {
			ignoreFakes++;
			var retv = orig(inventory);
			ignoreFakes--;
			return retv;
		}

		private static int ShrineCleanseBehavior_CleanseInventoryServer(On.RoR2.ShrineCleanseBehavior.orig_CleanseInventoryServer orig, Inventory inventory) {
			ignoreFakes++;
			var retv = orig(inventory);
			ignoreFakes--;
			return retv;
		}

		private static void ScrapperController_BeginScrapping(On.RoR2.ScrapperController.orig_BeginScrapping orig, ScrapperController self, int intPickupIndex) {
			ignoreFakes++;
			orig(self, intPickupIndex);
			ignoreFakes--;
		}

		private static RunReport RunReport_Generate(On.RoR2.RunReport.orig_Generate orig, Run run, GameEndingDef gameEnding) {
			ignoreFakes++;
			var retv = orig(run, gameEnding);
			ignoreFakes--;
			return retv;
		}

		private static int StolenInventoryInfo_StealItem(On.RoR2.ItemStealController.StolenInventoryInfo.orig_StealItem orig, object self, ItemIndex itemIndex, int maxStackToSteal, bool? useOrbOverride) {
			ignoreFakes++;
			var retv = orig(self, itemIndex, maxStackToSteal, useOrbOverride);
			ignoreFakes--;
			return retv;
		}

		private static int Inventory_GetTotalItemCountOfTier(On.RoR2.Inventory.orig_GetTotalItemCountOfTier orig, Inventory self, ItemTier itemTier) {
			ignoreFakes++;
			var retv = orig(self, itemTier);
			ignoreFakes--;
			return retv;
		}

		private static bool Inventory_HasAtLeastXTotalItemsOfTier(On.RoR2.Inventory.orig_HasAtLeastXTotalItemsOfTier orig, Inventory self, ItemTier itemTier, int x) {
			ignoreFakes++;
			var retv = orig(self, itemTier, x);
			ignoreFakes--;
			return retv;
		}

		private static void LunarItemOrEquipmentCostTypeHelper_PayOne(On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.orig_PayOne orig, Inventory inventory) {
			ignoreFakes++;
			orig(inventory);
			ignoreFakes--;
		}

		private static void LunarItemOrEquipmentCostTypeHelper_PayCost(On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.orig_PayCost orig, CostTypeDef costTypeDef, CostTypeDef.PayCostContext context) {
			ignoreFakes++;
			orig(costTypeDef, context);
			ignoreFakes--;
		}

		private static bool LunarItemOrEquipmentCostTypeHelper_IsAffordable(On.RoR2.CostTypeCatalog.LunarItemOrEquipmentCostTypeHelper.orig_IsAffordable orig, CostTypeDef costTypeDef, CostTypeDef.IsAffordableContext context) {
			ignoreFakes++;
			var retv = orig(costTypeDef, context);
			ignoreFakes--;
			return retv;
		}

		private static bool ContagiousItemManager_StepInventoryInfection(On.RoR2.Items.ContagiousItemManager.orig_StepInventoryInfection orig, Inventory inventory, ItemIndex originalItem, int limit, bool isForced) {
			ignoreFakes++;
			var retv = orig(inventory, originalItem, limit, isForced);
			ignoreFakes--;
			return retv;
		}

		private static void ContagiousItemManager_OnInventoryChangedGlobal(On.RoR2.Items.ContagiousItemManager.orig_OnInventoryChangedGlobal orig, Inventory inventory) {
			ignoreFakes++;
			orig(inventory);
			ignoreFakes--;
		}

		private static void SuppressedItemManager_OnInventoryChangedGlobal(On.RoR2.Items.SuppressedItemManager.orig_OnInventoryChangedGlobal orig, Inventory inventory) {
			ignoreFakes++;
			orig(inventory);
			ignoreFakes--;
		}

		private static bool SuppressedItemManager_SuppressItem(On.RoR2.Items.SuppressedItemManager.orig_SuppressItem orig, ItemIndex suppressedIndex, ItemIndex transformedIndex) {
			ignoreFakes++;
			var retv = orig(suppressedIndex, transformedIndex);
			ignoreFakes--;
			return retv;
		}

		private static void SuppressedItemManager_TransformItem(On.RoR2.Items.SuppressedItemManager.orig_TransformItem orig, Inventory inventory, ItemIndex suppressedIndex, ItemIndex transformedIndex) {
			ignoreFakes++;
			orig(inventory, suppressedIndex, transformedIndex);
			ignoreFakes--;
		}

		private static void CharacterMaster_TryCloverVoidUpgrades(On.RoR2.CharacterMaster.orig_TryCloverVoidUpgrades orig, CharacterMaster self) {
			ignoreFakes++;
			orig(self);
			ignoreFakes--;
		}
		#endregion

		private static int On_InvGetItemCountByIndex(On.RoR2.Inventory.orig_GetItemCount_ItemIndex orig, Inventory self, ItemIndex itemIndex) {
			var origVal = orig(self, itemIndex);
			if(ignoreFakes > 0 || !self) return origVal;
			var fakeinv = self.gameObject.GetComponent<FakeInventory>();
			if(!fakeinv) return origVal;
			return fakeinv.GetAdjustedItemCount(itemIndex);
		}

		private static void On_IIDInventoryChanged(On.RoR2.UI.ItemInventoryDisplay.orig_OnInventoryChanged orig, RoR2.UI.ItemInventoryDisplay self) {
			orig(self);
			if(!self || !self.isActiveAndEnabled || !self.inventory) return;
			var fakeInv = self.inventory.GetComponent<FakeInventory>();
			if(!fakeInv) return;
			List<ItemIndex> newAcqOrder = self.itemOrder.Take(self.itemOrderCount).ToList();
			for(int i = 0; i < self.itemStacks.Length; i++) {
				var aic = fakeInv.GetAdjustedItemCount((ItemIndex)i);
				if(self.itemStacks[i] == 0) {
					if(aic > 0)
						newAcqOrder.Add((ItemIndex)i);
					else
						newAcqOrder.Remove((ItemIndex)i);
				}
				
				self.itemStacks[i] = aic;
			}
			newAcqOrder = newAcqOrder.Distinct().ToList();
			newAcqOrder.CopyTo(0, self.itemOrder, 0, Mathf.Min(self.itemOrder.Length,newAcqOrder.Count));
			self.itemOrderCount = newAcqOrder.Count;
		}

        private static void On_IIDUpdateDisplay(On.RoR2.UI.ItemInventoryDisplay.orig_UpdateDisplay orig, RoR2.UI.ItemInventoryDisplay self) {
            orig(self);
            Inventory inv = self.inventory;
			if(!inv) return;
            var fakeInv = inv.gameObject.GetComponent<FakeInventory>();
			if(!fakeInv) return;
            foreach(var icon in self.itemIcons) {
				var realCount = fakeInv.GetRealItemCount(icon.itemIndex);
				var fakeCount = fakeInv.GetAdjustedItemCount(icon.itemIndex) - realCount;
				if(fakeCount == 0) continue;
				icon.stackText.enabled = true;
				icon.stackText.text = $"x{realCount}\n<color=#C18FE0>+{fakeCount}</color>";
            }
		}
	}
}