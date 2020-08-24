using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using System;
using System.Reflection;

namespace TILER2 {
	public class FakeInventory : Inventory {
		private static bool ignoreFakes = false;

		internal static void Setup() {
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
			if(self is FakeInventory || !ignoreFakes) return origVal;
			var fakeinv = self.gameObject.GetComponent<FakeInventory>();
			if(!fakeinv) return origVal;
			return origVal - fakeinv.GetItemCount(itemIndex);
		}
	}
}