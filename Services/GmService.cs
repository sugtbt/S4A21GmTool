using System;
using System.Collections.Generic;
using System.Linq;
using DfoGmTool.ServerCore.Game.TitleBook;
using DfoGmTool.ServerCore.Game.Characters;
using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.Game.Dungeon;
using DfoGmTool.ServerCore.Game.Inventory;
using DfoGmTool.ServerCore.Game.Mailbox;
using DfoGmTool.ServerCore.Game.Quests;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // 背包写走 InventoryRewardGrantService / InventoryDeleteService / InventoryPersistenceService，
    // 不重新引入 SqliteAssetService / 旧 DTO。其它域走对应仓储与进度服务。
    // partial 按域拆分: Accounts(账号/货币/晶块/金库) Characters(角色/属性/转职)
    // Inventory(背包/发放/删除/钱包) Mailbox(角色邮箱查看/删除/清空)
    // Quests(任务总览/完成链) TitleBook(称号簿)
    public sealed partial class GmService
    {
        private static readonly string[] JobNames =
        {
            "鬼剑士(男)", "格斗家(女)", "神枪手(男)", "魔法师(女)", "圣职者",
            "神枪手(女)", "暗夜使者", "格斗家(男)", "魔法师(男)", "黑暗武士",
            "缔造者", "鬼剑士(女)", "守护者",
        };

        private readonly GmConfig _config;
        private readonly PvfIndexService _pvfIndex;
        private readonly SupplementalItemExpirationService _supplementalItemExpiration;
        private readonly Lazy<TitleBookMutationService> _titleBookMutation;
        private readonly AccountProgressService _accountProgress;
        private readonly ExpertJobProgressService _expertJob;
        private readonly MailboxRepository _mailboxRepository;

        internal static void ResetPvfStaticData()
        {
            lock (_titleBookLock)
                _titleBookSlots = null;
        }

        public GmService(GmConfig config, PvfIndexService pvfIndex)
        {
            _config = config;
            _pvfIndex = pvfIndex;
            _supplementalItemExpiration = new SupplementalItemExpirationService(config.ConnectionString);
            _titleBookMutation = new Lazy<TitleBookMutationService>(
                () => new TitleBookMutationService(config.ConnectionString));
            _accountProgress = new AccountProgressService(config.DatabasePath, config.SchemaPath, config.PvfPath);
            _expertJob = new ExpertJobProgressService(config.ConnectionString, config.PvfPath);
            // 单次构造: ctor 内含 schema/迁移 bootstrap, 每次发放都 new 会全链路重跑
            _mailboxRepository = new MailboxRepository(config.DatabasePath, config.SchemaPath);
        }

        // 最终职业名(觉醒>转职>基础), PVF 索引没就绪时回退基础职业表
        private string DisplayJobName(int job, int growType)
        {
            var resolved = _pvfIndex.ResolveJobName(job, growType);
            if (!string.IsNullOrEmpty(resolved))
                return resolved;
            return job >= 0 && job < JobNames.Length ? JobNames[job] : "职业" + job;
        }

        private bool TryGetFirstCharacterId(int accountId, out int characterId)
        {
            characterId = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT character_id FROM characters WHERE account_id = @aid ORDER BY character_id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@aid", accountId);
                    var value = cmd.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                        return false;
                    characterId = Convert.ToInt32(value);
                    return true;
                }
            }
        }

        private bool TryGetAccountId(int characterId, out int accountId)
        {
            accountId = 0;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT account_id FROM characters WHERE character_id = @cid;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    var value = cmd.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                        return false;
                    accountId = Convert.ToInt32(value);
                    return true;
                }
            }
        }

        private static object Error(string message)
        {
            return new { success = false, error = message };
        }
    }
}
