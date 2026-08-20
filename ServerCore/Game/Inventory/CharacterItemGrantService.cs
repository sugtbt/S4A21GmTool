using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.Currency;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // GM自有: 发放结果(替代旧 ItemGrantResult, 面向新背包模型重写)。
    internal sealed class ItemGrantResult
    {
        internal bool Success { get; set; }
        internal string Error { get; set; }
        internal int ItemTemplateId { get; set; }
        internal int RequestedCount { get; set; }
        internal int GrantedCount { get; set; }
        internal InventoryListType ListType { get; set; } = InventoryListType.Main;
        internal short AssignedSlot { get; set; } = -1;
        internal int ExpireTime { get; set; }
        internal List<short> AffectedSlots { get; } = new List<short>();
    }

    // GM重写: 发放改走服务端新背包 InventoryService + InventoryRewardGrantService,
    // 不再手写旧 character_items 插入/堆叠合并/空槽查找(全部由发放服务负责)。
    // 期限由服务端创建路径按 PVF 自动解析(InventoryCreateService.Resolve*ExpireTime),
    // 时装/宠物 detail 与 UID 也由发放服务创建并同事务落库。
    internal static class CharacterItemGrantService
    {
        internal static ItemGrantResult TryGrant(
            string connectionString,
            int characterId,
            int accountId,
            int itemTemplateId,
            int count)
        {
            var result = new ItemGrantResult
            {
                ItemTemplateId = itemTemplateId,
                RequestedCount = count,
            };

            if (count <= 0)
                return Fail(result, "数量必须大于 0");

            // 晶块/灵魂/复活币是账号/钱包特殊资产, 由调用方走专门通道, 不属于角色物品发放。
            if (CurrencyService.IsAccountWarehouseItem(itemTemplateId)
                || Game.ReviveCoin.ReviveCoinService.IsReviveCoinReward(itemTemplateId))
                return Fail(result, "该特殊资产不属于角色物品发放");

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var inventory = GmInventoryStore.Load(connection, characterId, accountId);
                if (inventory == null)
                    return Fail(result, "背包加载失败");

                if (!InventoryRewardGrantService.TryCreateAndInsert(
                        inventory,
                        itemTemplateId,
                        ItemCreateReason.AdminGrant,
                        count,
                        out var grant)
                    || !grant.Success)
                {
                    return Fail(result, GrantErrorText(grant != null ? grant.Error : default));
                }

                if (!GmInventoryStore.Save(connection, characterId, inventory))
                    return Fail(result, "背包保存失败");

                result.Success = true;
                result.GrantedCount = grant.GrantedCount;
                result.ListType = grant.ListType;
                result.AssignedSlot = grant.SlotIndex;
                result.ExpireTime = grant.Core != null ? grant.Core.ExpireTime : 0;
                foreach (var slot in grant.Changes.Slots)
                {
                    if (slot.ListType == grant.ListType
                        && !result.AffectedSlots.Contains(slot.SlotIndex))
                        result.AffectedSlots.Add(slot.SlotIndex);
                }

                return result;
            }
        }

        private static string GrantErrorText(InventoryRewardGrantError error)
        {
            switch (error)
            {
                case InventoryRewardGrantError.None:
                    return "发放失败";
                case InventoryRewardGrantError.InvalidInventory:
                    return "背包状态异常";
                case InventoryRewardGrantError.InvalidRequest:
                    return "发放请求无效";
                case InventoryRewardGrantError.InvalidItem:
                    return "物品 ID 无效";
                case InventoryRewardGrantError.InvalidCount:
                    return "数量无效";
                case InventoryRewardGrantError.InsertPlanFailed:
                    return "目标背包空间不足";
                case InventoryRewardGrantError.CreateFailed:
                case InventoryRewardGrantError.DetailCreateFailed:
                    return "物品实例创建失败";
                default:
                    return "发放失败(" + error + ")";
            }
        }

        private static ItemGrantResult Fail(ItemGrantResult result, string error)
        {
            result.Success = false;
            result.Error = error;
            result.GrantedCount = 0;
            result.AssignedSlot = -1;
            result.AffectedSlots.Clear();
            return result;
        }
    }
}
