using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.ItemUpgrade;
using DfoGmTool.ServerCore.Game.Mailbox;
using DfoGmTool.ServerCore.Game.ReviveCoin;
using DfoGmTool.ServerCore.Infrastructure;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        // 读侧走服务端在线背包模型(InventoryService.LoadFromDb, 离线/诊断允许),
        // 覆盖全部容器和 99B ItemCore 语义, 不再裸读 character_items / 旧 DTO。
        public object ListItems(int characterId, PvfIndexService pvfIndex)
        {
            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                var inventory = GmInventoryStore.Load(conn, characterId, accountId);
                if (inventory == null)
                    return Error("背包加载失败: " + characterId);

                var rentalExpireTimes = _supplementalItemExpiration.LoadRentalExpireTimes(characterId);
                var items = new List<object>();

                // 主背包虚拟槽(金币/复活币/胜点)单独列出, 不可删除
                foreach (var virtualItem in inventory.GetMainVirtualCounts())
                {
                    if (virtualItem.SlotIndex > 2)
                        continue;

                    items.Add(new
                    {
                        container = "主背包",
                        category = "货币",
                        listType = (int)InventoryListType.Main,
                        slot = (int)virtualItem.SlotIndex,
                        templateId = virtualItem.ItemId,
                        name = pvfIndex.ResolveItemName(virtualItem.ItemId),
                        kind = "special",
                        rarity = 0,
                        count = virtualItem.Count,
                        instanceValue = virtualItem.Count,
                        durability = 0,
                        expireTime = 0,
                        supplementalExpiration = (object)null,
                        templateExpiration = CreateTemplateExpiration(pvfIndex, virtualItem.ItemId),
                        seal = 0,
                        deletable = false,
                    });
                }

                AppendCoreItems(items, "主背包", InventoryListType.Main, inventory, pvfIndex, rentalExpireTimes);
                AppendCoreItems(items, "个人仓库", InventoryListType.PersonalCargo, inventory, pvfIndex, rentalExpireTimes);
                AppendCoreItems(items, "账号金库", InventoryListType.AccountCargo, inventory, pvfIndex, rentalExpireTimes);
                AppendCoreItems(items, "穿戴栏", InventoryListType.Equipment, inventory, pvfIndex, rentalExpireTimes);
                AppendCoreItems(items, "时装", InventoryListType.Avatar, inventory, pvfIndex, rentalExpireTimes);
                AppendCoreItems(items, "宠物", InventoryListType.Pet, inventory, pvfIndex, rentalExpireTimes);
                AppendCoreItems(items, "公会勋章", InventoryListType.GuildMedal, inventory, pvfIndex, rentalExpireTimes);

                // 晶块/灵魂是账号级货币(accounts.cube_* / soul_*), 不在物品主表, 仅展示, 在账号面板调整
                foreach (var cube in CurrencyService.LoadCubeFragments(conn, null, accountId))
                {
                    items.Add(CreateAccountWarehouseRow(
                        "账号晶块",
                        cube.Slot,
                        cube.ItemId,
                        cube.Count,
                        pvfIndex));
                }

                foreach (var soul in CurrencyService.LoadSoulWarehouseCounts(conn, null, accountId))
                {
                    items.Add(CreateAccountWarehouseRow(
                        "灵魂仓库",
                        soul.Slot,
                        soul.ItemId,
                        soul.Count,
                        pvfIndex));
                }

                foreach (var piece in inventory.EpicPieces.BuildEntries())
                {
                    items.Add(new
                    {
                        container = "史诗碎片",
                        category = "史诗碎片",
                        listType = EpicPieceDisplayListType,
                        slot = piece.Index,
                        templateId = piece.ItemId,
                        name = pvfIndex.ResolveItemName(piece.ItemId),
                        kind = "epic-piece",
                        rarity = pvfIndex.ResolveItemRarity(piece.ItemId),
                        count = piece.Count,
                        instanceValue = piece.Count,
                        durability = 0,
                        expireTime = 0,
                        supplementalExpiration = (object)null,
                        templateExpiration = CreateTemplateExpiration(pvfIndex, piece.ItemId),
                        seal = 0,
                        deletable = false,
                    });
                }

                return new { characterId, count = items.Count, items };
            }
        }

        private static void AppendCoreItems(
            List<object> items,
            string container,
            InventoryListType listType,
            InventoryService inventory,
            PvfIndexService pvfIndex,
            IReadOnlyDictionary<int, int> rentalExpireTimes)
        {
            foreach (var pair in inventory.GetItems(listType))
            {
                var slot = pair.Key;
                var core = pair.Value;
                if (core == null || core.IsEmpty)
                    continue;

                // 主背包 0-2 虚拟槽由虚拟槽通道单独展示
                if (listType == InventoryListType.Main && slot <= 2)
                    continue;

                var kind = pvfIndex.ResolveItemKind(core.ItemId);
                var expireTime = core.ExpireTime;
                if (listType == InventoryListType.Avatar
                    && inventory.AvatarDetails.TryGetDetail(core.Uid, out var avatarDetail)
                    && avatarDetail != null)
                {
                    expireTime = avatarDetail.ExpireDate;
                }
                else if (listType == InventoryListType.Pet
                    && inventory.CreatureDetails.TryGetDetail(core.Uid, out var creatureDetail)
                    && creatureDetail != null
                    && creatureDetail.ExpireDate > 0)
                {
                    expireTime = creatureDetail.ExpireDate;
                }

                items.Add(new
                {
                    container,
                    category = ResolveItemCategory(container, listType, slot),
                    listType = (int)listType,
                    slot = (int)slot,
                    templateId = core.ItemId,
                    name = pvfIndex.ResolveItemName(core.ItemId),
                    kind,
                    rarity = pvfIndex.ResolveItemRarity(core.ItemId),
                    count = kind == "equipment" ? 1 : core.Count,
                    instanceValue = core.InstanceValue,
                    durability = (int)core.Durability,
                    expireTime,
                    supplementalExpiration = CreateSupplementalExpiration(rentalExpireTimes, core.ItemId, expireTime),
                    templateExpiration = CreateTemplateExpiration(pvfIndex, core.ItemId),
                    seal = (int)core.SealFlag,
                    deletable = IsDeletable(listType, slot),
                });
            }
        }

        private static object CreateTemplateExpiration(PvfIndexService pvfIndex, int itemTemplateId)
        {
            var expiration = pvfIndex.ResolveItemExpiration(itemTemplateId);
            return new
            {
                known = expiration.IsKnown,
                absoluteExpireTime = expiration.AbsoluteExpirationUnixTime,
                usablePeriodDays = expiration.UsablePeriodDays,
                dailyDeleteItem = expiration.DailyDeleteItem,
                invalid = expiration.HasInvalidDefinition,
            };
        }

        private static object CreateSupplementalExpiration(
            IReadOnlyDictionary<int, int> rentalExpireTimes,
            int itemTemplateId,
            int instanceExpireTime)
        {
            if (instanceExpireTime <= 0
                && rentalExpireTimes != null
                && rentalExpireTimes.TryGetValue(itemTemplateId, out var expireTime)
                && expireTime > 0)
            {
                return new
                {
                    expireTime,
                    source = "rental",
                };
            }

            return null;
        }

        // 史诗碎片不是 InventoryListType 槽，只用于背包页只读展示（图鉴下标）。
        private const int EpicPieceDisplayListType = -1;

        private static bool TryParseInventoryListType(int listType, out InventoryListType list)
        {
            list = default;
            if (listType < 0 || listType > byte.MaxValue)
                return false;

            var parsed = (InventoryListType)(byte)listType;
            if (!Enum.IsDefined(typeof(InventoryListType), parsed))
                return false;

            list = parsed;
            return true;
        }

        // 货币行(主背包 slot 0-2)删行会打坏钱包; 晶块(354-359)/灵魂(360-364)和账号金库是账号共享, 在账号面板管理
        private static bool IsDeletable(InventoryListType listType, int slot)
        {
            if (listType == InventoryListType.AccountCargo)
                return false;
            if (listType == InventoryListType.Main && slot <= 2)
                return false;
            if (listType == InventoryListType.Main && CurrencyService.IsAccountWarehouseSlot(slot))
                return false;
            return true;
        }

        private static object CreateAccountWarehouseRow(
            string category,
            int slot,
            int itemId,
            int count,
            PvfIndexService pvfIndex)
        {
            return new
            {
                container = "主背包",
                category,
                listType = (int)InventoryListType.Main,
                slot,
                templateId = itemId,
                name = pvfIndex.ResolveItemName(itemId),
                kind = "special",
                rarity = pvfIndex.ResolveItemRarity(itemId),
                count,
                instanceValue = count,
                durability = 0,
                expireTime = 0,
                supplementalExpiration = (object)null,
                templateExpiration = CreateTemplateExpiration(pvfIndex, itemId),
                seal = 0,
                deletable = false,
            };
        }

        private static string ResolveItemCategory(string container, InventoryListType listType, int slot)
        {
            if (listType == InventoryListType.Main)
                return ResolveMainSegment(slot);
            if (listType == InventoryListType.GuildMedal)
                return slot <= ItemSlotBoundService.GuildMedalInventorySlotEnd ? "公会勋章" : "守护珠";
            if (listType == InventoryListType.Equipment && slot == (int)EquipmentType.GuildMedal)
                return "穿戴勋章";
            return container;
        }

        // 整槽删除走 TryRemoveSlot（含时装/未知物品）；部分数量仍走客户端删除入口。
        public object DeleteItemAt(int characterId, int listType, int slot, int count)
        {
            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            if (!TryParseInventoryListType(listType, out var list))
                return Error("不是物品槽，无法删除");
            if (!IsDeletable(list, slot))
                return Error("该槽位不允许删除(货币行或账号金库)");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                var inventory = GmInventoryStore.Load(conn, characterId, accountId);
                if (inventory == null)
                    return Error("背包加载失败");

                InventoryMutationResult result = null;
                var removed = count <= 0
                    ? InventoryDeleteService.TryRemoveSlot(inventory, list, (short)slot, out _)
                    : InventoryDeleteService.TryDeleteForClient(
                        inventory, list, (short)slot, count, out result);
                if (!removed)
                    return Error("删除失败(槽位为空或该列表不支持删除)");

                if (!GmInventoryStore.Save(conn, characterId, inventory))
                    return Error("背包保存失败");

                return new
                {
                    success = true,
                    characterId,
                    listType,
                    slot,
                    remaining = result != null ? result.RemainingStackCount : 0,
                };
            }
        }

        public object BatchDeleteItems(int characterId, List<BatchDeleteEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return Error("没有要删除的条目");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            var deleted = 0;
            var failed = new List<object>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                var inventory = GmInventoryStore.Load(conn, characterId, accountId);
                if (inventory == null)
                    return Error("背包加载失败");

                foreach (var entry in entries)
                {
                    if (!TryParseInventoryListType(entry.ListType, out var list)
                        || !IsDeletable(list, entry.Slot))
                    {
                        failed.Add(new { entry.ListType, entry.Slot, reason = "受保护槽位" });
                        continue;
                    }

                    if (InventoryDeleteService.TryRemoveSlot(
                            inventory, list, (short)entry.Slot, out _))
                        deleted++;
                    else
                        failed.Add(new { entry.ListType, entry.Slot, reason = "删除失败" });
                }

                if (!GmInventoryStore.Save(conn, characterId, inventory))
                    return Error("背包保存失败");
            }

            return new { success = true, characterId, deleted, failedCount = failed.Count, failed };
        }

        // 主背包 slot 分段，与 ItemSlotBoundService / InventoryService 虚拟槽一致。
        private static string ResolveMainSegment(int slot)
        {
            if (slot <= 2) return "货币";        // 0金币 1复活币 2胜点
            if (slot <= 8) return "快捷栏";      // QuickSlot 3-8
            if (slot <= 64) return "装备";       // 9-64 (含租赁)
            if (slot <= 120) return "消耗品";    // 65-120
            if (slot <= 176) return "材料";      // 121-176
            if (slot <= 232) return "任务品";    // 177-232
            if (slot <= 288) return "副职业材料"; // 233-288
            if (slot <= ItemSlotBoundService.AvatarEmblemSlotEnd) return "徽章"; // 289-351
            if (slot <= InventoryService.MainReservedSlotEnd) return "其他";    // 352-353 预留
            if (slot <= 359) return "账号晶块";   // 354-359 账号共享(accounts表列), 在账号面板调整
            if (slot <= 364) return "灵魂仓库";   // 360-364 账号共享(accounts.soul_*), 在账号面板调整
            return "其他";
        }

        // GM 系统邮件发件人固定 ID(正数即可, sender 无 FK; 收件箱显示发件人名 "GM")
        private const int GmMailSenderCharacterId = 1999999999;
        private const int MailAttachmentLimit = 10;
        private const int EquipmentUpgradeLevelMax = 31;
        private const int WeaponForgingLevelMax = 8;
        private const byte UnidentifiedAmplifyFlag = 0x80;

        // 默认经游戏内邮件发放: 物品走服务端 SendSystemMail 落邮件表,
        // 领取由服务端自身 handler 完成——在线角色也能安全收, 不再直写背包
        // (在线角色的背包真源在服务端内存, 直改 DB 会被内存态覆盖)。
        // direct=true 退居旧的直写背包路径, 仅用于离线角色维护。
        public object GiveItem(
            int characterId,
            int itemTemplateId,
            int count,
            PvfIndexService pvfIndex,
            bool direct = false,
            EquipmentGrantOptions equipmentOptions = null,
            bool sendSet = false)
        {
            if (itemTemplateId <= 0)
                return Error("itemTemplateId 无效");
            if (count <= 0)
                return Error("数量必须大于 0");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            // 名字解析不到通常意味着 ID 不存在, 直接发下去客户端会异常, 先拦住
            var name = pvfIndex.ResolveItemName(itemTemplateId);
            if (name == null && pvfIndex.IsReady)
                return Error("物品 ID " + itemTemplateId + " 在 PVF 中不存在(装备/堆叠表都没有)");

            var isEquipment = string.Equals(
                pvfIndex.ResolveItemKind(itemTemplateId),
                "equipment",
                StringComparison.Ordinal);
            if (sendSet)
            {
                if (direct)
                    return Error("套装发放只支持邮件");
                if (!isEquipment)
                    return Error("只有装备和装扮可以按套装发放");
                return GiveItemSetViaMail(characterId, accountId, itemTemplateId, name, pvfIndex, equipmentOptions);
            }

            EquipmentMailConfiguration equipment = null;
            if (isEquipment)
            {
                if (!TryResolveEquipmentMailConfiguration(itemTemplateId, equipmentOptions, out equipment, out var equipmentError))
                    return Error(equipmentError);
                if (!direct && count > MailAttachmentLimit)
                    return Error("装备发送数量不能超过邮件附件上限 " + MailAttachmentLimit);
            }
            else if (equipmentOptions != null)
            {
                return Error("只有装备可以设置净化、强化、增幅或锻造属性");
            }

            // 晶块/灵魂是账号级货币, 走 accounts.cube_* / soul_* 字段
            if (CurrencyService.IsCubeFragment(itemTemplateId))
            {
                using (var conn = new SqliteConnection(_config.ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        CurrencyService.AddCubeFragment(conn, tx, accountId, itemTemplateId, count);
                        tx.Commit();
                    }
                }
                return new { success = true, characterId, itemTemplateId, name, count, slot = CurrencyService.GetCubeFragmentSlot(itemTemplateId) };
            }

            if (CurrencyService.IsSoulWarehouseItem(itemTemplateId))
            {
                using (var conn = new SqliteConnection(_config.ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        CurrencyService.AddSoulWarehouseCount(conn, tx, accountId, itemTemplateId, count);
                        tx.Commit();
                    }
                }
                return new { success = true, characterId, itemTemplateId, name, count, slot = CurrencyService.GetSoulWarehouseSlot(itemTemplateId) };
            }

            // 史诗碎片是账号图鉴数量，写 accounts.epic_piece_counts，不造 ItemCore、不进邮件。
            if (EpicPieceCatalogService.IsEpicPieceId(itemTemplateId))
            {
                if (equipmentOptions != null)
                    return Error("史诗碎片不能设置装备属性");
                return GiveItemDirect(characterId, accountId, itemTemplateId, count, name);
            }

            // 复活币走主背包 1 号虚拟槽
            if (ReviveCoinService.IsReviveCoinReward(itemTemplateId))
            {
                using (var conn = new SqliteConnection(_config.ConnectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        InventoryMainVirtualCountRepository.GrantCurrency(
                            conn, tx, characterId, ReviveCoinService.WalletSlot, count, int.MaxValue);
                        tx.Commit();
                    }
                }
                return new { success = true, characterId, itemTemplateId, name, count, slot = (int)ReviveCoinService.WalletSlot };
            }

            if (direct)
            {
                if (equipment != null && equipment.IsCustomized)
                    return Error("装备属性配置仅支持通过邮件发放");
                return GiveItemDirect(characterId, accountId, itemTemplateId, count, name);
            }

            return GiveItemViaMail(characterId, accountId, itemTemplateId, count, name, equipment);
        }

        private object GiveItemSetViaMail(
            int characterId,
            int accountId,
            int itemTemplateId,
            string seedName,
            PvfIndexService pvfIndex,
            EquipmentGrantOptions equipmentOptions)
        {
            string receiverName = null;
            int receiverLevel = 0;
            int receiverJob = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name, level, job FROM characters WHERE character_id = @cid AND delete_flag = 0;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("角色不存在或已删除: " + characterId);
                        receiverName = reader.GetString(0);
                        receiverLevel = reader.GetInt32(1);
                        receiverJob = reader.GetInt32(2);
                    }
                }
            }

            var jobLabel = pvfIndex.ResolveJobBaseName(receiverJob);
            if (!pvfIndex.TryResolveSendableSet(itemTemplateId, jobLabel, out var memberIds, out var setName, out var setError))
                return Error(setError);

            var attachments = new List<MailboxSendAttachmentRequest>(memberIds.Count);
            foreach (var memberId in memberIds)
            {
                if (!TryResolveSetPieceMailConfiguration(memberId, equipmentOptions, out var equipment, out var equipmentError))
                    return Error((pvfIndex.ResolveItemName(memberId) ?? ("物品 " + memberId)) + ": " + equipmentError);
                if (!TryCreateMailAttachments(memberId, 1, equipment, out var pieceAttachments, out var attachmentError))
                    return Error(attachmentError);
                attachments.AddRange(pieceAttachments);
            }

            var mailCount = (attachments.Count + MailAttachmentLimit - 1) / MailAttachmentLimit;
            if (mailCount > 2)
                return Error("套装部件超过两封邮件上限");

            var messageIds = new List<long>(mailCount);
            var displayName = string.IsNullOrWhiteSpace(setName) ? seedName : setName;
            for (var offset = 0; offset < attachments.Count; offset += MailAttachmentLimit)
            {
                var chunk = attachments.Skip(offset).Take(MailAttachmentLimit).ToList();
                var part = (offset / MailAttachmentLimit) + 1;
                var text = mailCount == 1
                    ? "GM 发放套装：" + displayName
                    : "GM 发放套装：" + displayName + "（" + part + "/" + mailCount + "）";
                var request = new MailboxSendRequest
                {
                    SenderCharacterId = GmMailSenderCharacterId,
                    SenderAccountId = 0,
                    SenderName = "GM",
                    SenderLevel = 86,
                    ReceiverCharacterId = characterId,
                    ReceiverAccountId = accountId,
                    ReceiverName = receiverName ?? string.Empty,
                    ReceiverLevel = receiverLevel,
                    Gold = 0,
                    Text = text,
                    MailType = 1,
                    SourceProtocol = 0,
                    Unlimited = true,
                    IdempotencyKey = "gm:" + Guid.NewGuid().ToString("N"),
                    AuditActor = "DfoGmTool",
                    AuditReason = "GM 发放套装",
                    Attachments = chunk,
                };

                var result = _mailboxRepository.SendSystemMail(request);
                if (!result.Success)
                {
                    if (messageIds.Count == 0)
                        return Error("套装邮件发放失败: " + MailErrorText(result.Error));
                    return new
                    {
                        success = true,
                        partial = true,
                        characterId,
                        itemTemplateId,
                        name = displayName,
                        count = attachments.Count,
                        itemCount = attachments.Count,
                        viaMail = true,
                        sendSet = true,
                        mailCount = messageIds.Count,
                        messageIds = messageIds.ToArray(),
                        error = "后续邮件发送失败: " + MailErrorText(result.Error),
                    };
                }

                messageIds.Add(result.MessageId);
            }

            return new
            {
                success = true,
                characterId,
                itemTemplateId,
                name = displayName,
                count = attachments.Count,
                itemCount = attachments.Count,
                viaMail = true,
                sendSet = true,
                mailCount = messageIds.Count,
                messageIds = messageIds.ToArray(),
                itemIds = memberIds,
            };
        }

        private static bool TryResolveSetPieceMailConfiguration(
            int itemTemplateId,
            EquipmentGrantOptions options,
            out EquipmentMailConfiguration configuration,
            out string error)
        {
            if (TryResolveEquipmentMailConfiguration(itemTemplateId, options, out configuration, out error))
                return true;
            if (options != null)
            {
                var fallback = new EquipmentGrantOptions
                {
                    State = "normal",
                    QualityMode = options.QualityMode,
                };
                if (TryResolveEquipmentMailConfiguration(itemTemplateId, fallback, out configuration, out error))
                    return true;
            }

            return TryResolveEquipmentMailConfiguration(itemTemplateId, null, out configuration, out error);
        }

        private object GiveItemViaMail(
            int characterId,
            int accountId,
            int itemTemplateId,
            int count,
            string name,
            EquipmentMailConfiguration equipment)
        {
            string receiverName = null;
            int receiverLevel = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name, level FROM characters WHERE character_id = @cid AND delete_flag = 0;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            return Error("角色不存在或已删除: " + characterId);
                        receiverName = reader.GetString(0);
                        receiverLevel = reader.GetInt32(1);
                    }
                }
            }

            if (!TryCreateMailAttachments(itemTemplateId, count, equipment, out var attachments, out var attachmentError))
                return Error(attachmentError);

            var request = new MailboxSendRequest
            {
                SenderCharacterId = GmMailSenderCharacterId,
                SenderAccountId = 0,
                SenderName = "GM",
                SenderLevel = 86,
                ReceiverCharacterId = characterId,
                ReceiverAccountId = accountId,
                ReceiverName = receiverName ?? string.Empty,
                ReceiverLevel = receiverLevel,
                Gold = 0,
                Text = "GM 发放",
                MailType = 1,
                SourceProtocol = 0,
                Unlimited = true,
                IdempotencyKey = "gm:" + Guid.NewGuid().ToString("N"),
                AuditActor = "DfoGmTool",
                AuditReason = "GM 发放",
                Attachments = attachments,
            };

            var result = _mailboxRepository.SendSystemMail(request);
            if (!result.Success)
                return Error("邮件发放失败: " + MailErrorText(result.Error));

            return new
            {
                success = true,
                characterId,
                itemTemplateId,
                name,
                count,
                viaMail = true,
                messageId = result.MessageId,
                attachmentCount = attachments.Count,
            };
        }

        private static bool TryResolveEquipmentMailConfiguration(
            int itemTemplateId,
            EquipmentGrantOptions options,
            out EquipmentMailConfiguration configuration,
            out string error)
        {
            configuration = null;
            error = null;

            var metadata = ItemMetadataResolver.Resolve(itemTemplateId);
            if (metadata == null || !string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
            {
                error = "物品不是装备: " + itemTemplateId;
                return false;
            }

            var capabilities = EquipmentGrantPolicy.Evaluate(
                metadata.EquipmentType,
                metadata.Rarity,
                metadata.MinimumLevel,
                metadata.ImpossibleContents,
                ItemUpgradeTableProvider.GetAmplifyEquipLevelConst());
            var state = (options?.State ?? "normal").Trim().ToLowerInvariant();
            if (state.Length == 0)
                state = "normal";

            var upgradeLevel = options?.UpgradeLevel ?? 0;
            var amplifyType = options?.AmplifyType ?? 0;
            var forgingLevel = options?.ForgingLevel ?? 0;
            var qualityMode = (options?.QualityMode ?? "top").Trim().ToLowerInvariant();
            if (upgradeLevel < 0 || upgradeLevel > EquipmentUpgradeLevelMax)
            {
                error = "强化/增幅等级必须在 0-" + EquipmentUpgradeLevelMax + " 之间";
                return false;
            }
            if (forgingLevel < 0 || forgingLevel > WeaponForgingLevelMax)
            {
                error = "锻造等级必须在 0-" + WeaponForgingLevelMax + " 之间";
                return false;
            }
            if (!capabilities.IsWeapon && forgingLevel != 0)
            {
                error = "只有武器可以设置锻造等级";
                return false;
            }

            int? qualitySeed;
            switch (qualityMode)
            {
                case "top":
                    qualitySeed = unchecked((int)ItemQuality.TopQualitySeed);
                    break;
                case "random":
                    qualitySeed = null;
                    break;
                default:
                    error = "未知装备品级模式: " + qualityMode;
                    return false;
            }

            byte resolvedAmplifyType;
            ushort resolvedAmplifyValue;
            switch (state)
            {
                case "normal":
                    if (amplifyType != 0)
                    {
                        error = "普通强化装备不能设置增幅属性";
                        return false;
                    }
                    if (!capabilities.CanReinforce && upgradeLevel != 0)
                    {
                        error = "该装备禁止强化";
                        return false;
                    }
                    resolvedAmplifyType = 0;
                    resolvedAmplifyValue = 0;
                    break;

                case "unpurified":
                    if (!capabilities.CanHaveAmplifyState)
                    {
                        error = "该装备不支持异界气息";
                        return false;
                    }
                    if (upgradeLevel != 0 || amplifyType != 0)
                    {
                        error = "未净化装备不能设置强化、增幅等级或增幅属性";
                        return false;
                    }
                    resolvedAmplifyType = UnidentifiedAmplifyFlag;
                    resolvedAmplifyValue = 0;
                    break;

                case "purified":
                case "amplified":
                    if (!capabilities.CanHaveAmplifyState)
                    {
                        error = "该装备不支持净化或增幅";
                        return false;
                    }
                    if (amplifyType < (int)AmplifyAttributeType.Vitality
                        || amplifyType > (int)AmplifyAttributeType.Intelligence)
                    {
                        error = "增幅属性必须是体力、精神、力量或智力";
                        return false;
                    }
                    if (!capabilities.CanAmplifyLevel && upgradeLevel != 0)
                    {
                        error = "该装备禁止增幅";
                        return false;
                    }
                    resolvedAmplifyType = (byte)amplifyType;
                    resolvedAmplifyValue = ItemAmplifier.CalculateInitialAttributeValue(
                        metadata.Rarity,
                        (AmplifyAttributeType)amplifyType);
                    if (resolvedAmplifyValue == 0)
                    {
                        error = "无法从当前 PVF 计算增幅属性初始值";
                        return false;
                    }
                    state = "amplified";
                    break;

                default:
                    error = "未知装备状态: " + state;
                    return false;
            }

            configuration = new EquipmentMailConfiguration
            {
                UpgradeLevel = (byte)upgradeLevel,
                AmplifyType = resolvedAmplifyType,
                AmplifyValue = resolvedAmplifyValue,
                ForgingLevel = (byte)forgingLevel,
                QualitySeed = qualitySeed,
                IsCustomized = state != "normal"
                    || upgradeLevel != 0
                    || forgingLevel != 0
                    || (options != null && qualitySeed.HasValue),
            };
            return true;
        }

        private static bool TryCreateMailAttachments(
            int itemTemplateId,
            int count,
            EquipmentMailConfiguration equipment,
            out IReadOnlyList<MailboxSendAttachmentRequest> attachments,
            out string error)
        {
            error = null;
            if (EpicPieceCatalogService.IsEpicPieceId(itemTemplateId)
                || CurrencyService.IsAccountWarehouseItem(itemTemplateId))
            {
                attachments = Array.Empty<MailboxSendAttachmentRequest>();
                error = "该物品不能作为邮件附件";
                return false;
            }

            if (equipment == null)
            {
                attachments = new[]
                {
                    new MailboxSendAttachmentRequest
                    {
                        ItemId = itemTemplateId,
                        ItemCount = count,
                    },
                };
                return true;
            }

            var equipmentAttachments = new List<MailboxSendAttachmentRequest>(count);
            for (var i = 0; i < count; i++)
            {
                if (!InventoryRewardGrantService.TryCreateOnly(
                        itemTemplateId,
                        ItemCreateReason.MailAttachment,
                        1,
                        out var createResult)
                    || createResult.Kind != InventoryRewardGrantKind.InventoryItem
                    || createResult.Core == null)
                {
                    attachments = Array.Empty<MailboxSendAttachmentRequest>();
                    error = "装备附件创建失败: " + itemTemplateId;
                    return false;
                }

                var core = createResult.Core;
                core.Upgrade = equipment.UpgradeLevel;
                core.AmplifyType = equipment.AmplifyType;
                core.AmplifyValue = equipment.AmplifyValue;
                core.GenuineUpgrade = equipment.ForgingLevel;
                core.InstanceValue = equipment.QualitySeed
                    ?? ServerRandom.Next(1, unchecked((int)ItemQuality.TopQualitySeed));
                equipmentAttachments.Add(new MailboxSendAttachmentRequest
                {
                    ItemId = itemTemplateId,
                    ItemCount = 1,
                    ItemCoreData = core.ToBytes(),
                });
            }

            attachments = equipmentAttachments;
            return true;
        }

        private sealed class EquipmentMailConfiguration
        {
            public byte UpgradeLevel { get; set; }
            public byte AmplifyType { get; set; }
            public ushort AmplifyValue { get; set; }
            public byte ForgingLevel { get; set; }
            public int? QualitySeed { get; set; }
            public bool IsCustomized { get; set; }
        }

        private object GiveItemDirect(int characterId, int accountId, int itemTemplateId, int count, string name)
        {
            var grant = CharacterItemGrantService.TryGrant(
                _config.ConnectionString, characterId, accountId, itemTemplateId, count);
            if (!grant.Success)
                return Error(grant.Error ?? "发放失败(背包可能已满)");

            var isEpicPiece = EpicPieceCatalogService.IsEpicPieceId(itemTemplateId);
            return new
            {
                success = true,
                characterId,
                itemTemplateId,
                name,
                count = grant.GrantedCount,
                slot = isEpicPiece ? -1 : (int)grant.AssignedSlot,
                expireTime = grant.ExpireTime,
                slots = grant.AffectedSlots,
                epicPiece = isEpicPiece,
            };
        }

        private static string MailErrorText(MailboxSendError error)
        {
            switch (error)
            {
                case MailboxSendError.None: return "未知错误";
                case MailboxSendError.InvalidRequest: return "请求无效";
                case MailboxSendError.ReceiverNotFound: return "收件角色不存在";
                case MailboxSendError.ReceiverDeleted: return "收件角色已删除";
                case MailboxSendError.InvalidAttachment: return "附件无效(物品不可邮或创建失败)";
                case MailboxSendError.TooManyAttachments: return "附件数量超限";
                case MailboxSendError.NotTradable: return "该物品不可交易";
                case MailboxSendError.AccountBound: return "该物品为账号绑定";
                default: return error.ToString();
            }
        }

        public object RemoveItem(int characterId, int itemTemplateId, int count)
        {
            if (count <= 0)
                count = 1;

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                var inventory = GmInventoryStore.Load(conn, characterId, accountId);
                if (inventory == null)
                    return Error("背包加载失败");

                InventoryMainItemConsumeResult result;
                if (!inventory.TryConsumeMainItem(itemTemplateId, count, out result)
                    || !result.Success)
                    return Error("移除失败(角色没有该物品或数量不足)");

                if (!GmInventoryStore.Save(conn, characterId, inventory))
                    return Error("背包保存失败");

                var slot = result.Changes.Slots.Count > 0 ? (int)result.Changes.Slots[0].SlotIndex : -1;
                return new
                {
                    success = true,
                    characterId,
                    itemTemplateId,
                    count,
                    slot,
                    remaining = inventory.CountMainItem(itemTemplateId),
                };
            }
        }

        public object AdjustGold(int characterId, int amount)
        {
            if (amount == 0)
                return Error("amount 不能为 0");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (amount > 0)
                    {
                        CurrencyService.GrantGold(conn, tx, characterId, amount);
                    }
                    else if (!CurrencyService.TrySpendGold(conn, tx, characterId, -amount))
                    {
                        return Error("扣款失败(金币不足)");
                    }

                    tx.Commit();
                }

                var wallet = CurrencyService.LoadWallet(conn, null, characterId);
                return new { success = true, characterId, amount, gold = wallet.Gold };
            }
        }

        // 三种角色货币覆写: 金币走 CurrencyService 按差额加扣;
        // 复活币(slot1)/胜点(slot2)是虚拟槽, 走服务端虚拟槽仓储同语义直写
        public object SetWalletValue(int characterId, string type, int value)
        {
            if (value < 0)
                return Error("数值不能为负");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            type = (type ?? string.Empty).Trim().ToLowerInvariant();

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();

                if (type == "gold")
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        var wallet = CurrencyService.LoadWallet(conn, tx, characterId);
                        var delta = value - wallet.Gold;
                        if (delta > 0)
                            CurrencyService.GrantGold(conn, tx, characterId, delta);
                        else if (delta < 0 && !CurrencyService.TrySpendGold(conn, tx, characterId, -delta))
                            return Error("扣减失败");
                        tx.Commit();
                    }
                    return new { success = true, characterId, type, value };
                }

                short slot;
                switch (type)
                {
                    case "revive": slot = 1; break;
                    case "sp": slot = 2; break;
                    default: return Error("不支持的类型: " + type + " (可用: gold/revive/sp)");
                }

                using (var tx = conn.BeginTransaction())
                {
                    InventoryMainVirtualCountRepository.UpsertCurrencySlot(
                        conn, tx, characterId, slot, value);
                    tx.Commit();
                }
            }
            return new { success = true, characterId, type, value };
        }

        // 点券是账号级余额, 服务端接口按角色定位账号
        public object AdjustCera(int characterId, int amount, string type)
        {
            if (amount == 0)
                return Error("amount 不能为 0");

            int accountId;
            if (!TryGetAccountId(characterId, out accountId))
                return Error("角色不存在: " + characterId);

            var useToken = string.Equals(type, "token", StringComparison.OrdinalIgnoreCase);
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    if (amount > 0)
                    {
                        if (useToken)
                            CurrencyService.GrantTokenCera(conn, tx, characterId, amount);
                        else
                            CurrencyService.GrantCera(conn, tx, characterId, amount);
                    }
                    else
                    {
                        var ok = useToken
                            ? CurrencyService.TrySpendTokenCera(conn, tx, characterId, -amount)
                            : CurrencyService.TrySpendCera(conn, tx, characterId, -amount);
                        if (!ok)
                            return Error("扣减失败(余额不足)");
                    }

                    tx.Commit();
                }

                var wallet = CurrencyService.LoadWallet(conn, null, characterId);
                return new { success = true, characterId, accountId, amount, cera = wallet.Cera, tokenCera = wallet.TokenCera };
            }
        }
    }

    public sealed class BatchDeleteEntry
    {
        public int ListType { get; set; }
        public int Slot { get; set; }
    }
}
