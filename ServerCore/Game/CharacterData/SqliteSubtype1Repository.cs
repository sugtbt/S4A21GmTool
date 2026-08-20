using System;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.CharacterData
{
    // GM瘦身拷贝: 只保留 GM 调用图可达的成员(保留成员逐字一致, 命名空间重写除外)。
    // 删除: Load/HasData(依赖 Noti2InventoryProjectionBuilder/QuestData/KnightShield*),
    //   UpdateSkillTreeIndex/LoadSkillTreeIndex(依赖技能树扩展状态模型/KnightShield),
    //   RecomputeAllCombatStats(GM 未调用), ClearEquippedSortLockForClient(无人调用)。
    public sealed class SqliteSubtype1Repository
    {
        /// <summary>
        /// CharacterStatComputer.BuildAdditionalInfo 输出的 82 字节 stat blob,
        /// 拆成 character_subtype1_fields 各 stat_* 列。偏移与 BuildAdditionalInfo 写入顺序一致。
        /// </summary>
        private readonly struct CombatStatFields
        {
            public readonly long HpMax, MpMax, InventoryLimit, MoveSpeed, Weight;
            public readonly int PhysicalAttack, PhysicalDefense, MagicalAttack, MagicalDefense;
            public readonly int FireRes, WaterRes, DarkRes, LightRes;
            public readonly int HpRegen, MpRegen, AttackSpeed, CastSpeed, HitRecovery, JumpPower;

            private CombatStatFields(byte[] b)
            {
                int o = 0;
                HpMax = (long)BitConverter.ToUInt32(b, o); o += 4;
                MpMax = (long)BitConverter.ToUInt32(b, o); o += 4;
                PhysicalAttack = BitConverter.ToInt16(b, o); o += 2;
                PhysicalDefense = BitConverter.ToInt16(b, o); o += 2;
                MagicalAttack = BitConverter.ToInt16(b, o); o += 2;
                MagicalDefense = BitConverter.ToInt16(b, o); o += 2;
                FireRes = BitConverter.ToInt16(b, o); o += 2;
                WaterRes = BitConverter.ToInt16(b, o); o += 2;
                DarkRes = BitConverter.ToInt16(b, o); o += 2;
                LightRes = BitConverter.ToInt16(b, o); o += 2;
                o += 34; // 17 × u16 占位, 与 BuildAdditionalInfo 的零占位对齐
                InventoryLimit = (long)BitConverter.ToUInt32(b, o); o += 4;
                HpRegen = BitConverter.ToUInt16(b, o); o += 2;
                MpRegen = BitConverter.ToUInt16(b, o); o += 2;
                MoveSpeed = (long)BitConverter.ToUInt32(b, o); o += 4;
                AttackSpeed = BitConverter.ToUInt16(b, o); o += 2;
                CastSpeed = BitConverter.ToUInt16(b, o); o += 2;
                HitRecovery = BitConverter.ToUInt16(b, o); o += 2;
                JumpPower = BitConverter.ToUInt16(b, o); o += 2;
                Weight = (long)BitConverter.ToUInt32(b, o);
            }

            public static CombatStatFields Parse(byte[] blob)
            {
                if (blob == null || blob.Length < 82)
                    throw new ArgumentException($"[Subtype1Repo] stat blob 长度不足: {blob?.Length ?? 0}/82");
                return new CombatStatFields(blob);
            }

            public void AddTo(SqliteCommand cmd)
            {
                cmd.Parameters.AddWithValue("@hp", HpMax);
                cmd.Parameters.AddWithValue("@mp", MpMax);
                cmd.Parameters.AddWithValue("@pa", PhysicalAttack);
                cmd.Parameters.AddWithValue("@pd", PhysicalDefense);
                cmd.Parameters.AddWithValue("@ma", MagicalAttack);
                cmd.Parameters.AddWithValue("@md", MagicalDefense);
                cmd.Parameters.AddWithValue("@fr", FireRes);
                cmd.Parameters.AddWithValue("@wr", WaterRes);
                cmd.Parameters.AddWithValue("@dr", DarkRes);
                cmd.Parameters.AddWithValue("@lr", LightRes);
                cmd.Parameters.AddWithValue("@il", InventoryLimit);
                cmd.Parameters.AddWithValue("@hr", HpRegen);
                cmd.Parameters.AddWithValue("@mr", MpRegen);
                cmd.Parameters.AddWithValue("@ms", MoveSpeed);
                cmd.Parameters.AddWithValue("@as2", AttackSpeed);
                cmd.Parameters.AddWithValue("@cs", CastSpeed);
                cmd.Parameters.AddWithValue("@hrc", HitRecovery);
                cmd.Parameters.AddWithValue("@jp", JumpPower);
                cmd.Parameters.AddWithValue("@wt", Weight);
            }
        }

        /// <summary>同连接版本, 供 RecomputeAllCombatStats 在单连接内顺序执行避免锁冲突;
        /// 传入 tx 可并入外部事务(等级与属性写同生共死)。</summary>
        internal static int UpdateCombatStatsOnConnection(SqliteConnection conn, int characterId, byte[] statBlob, SqliteTransaction tx = null)
        {
            var f = CombatStatFields.Parse(statBlob);
            using (var cmd = new SqliteCommand(@"
UPDATE character_subtype1_fields SET
    stat_hp_max=@hp, stat_mp_max=@mp,
    stat_physical_attack=@pa, stat_physical_defense=@pd,
    stat_magical_attack=@ma, stat_magical_defense=@md,
    stat_fire_resistance=@fr, stat_water_resistance=@wr,
    stat_dark_resistance=@dr, stat_light_resistance=@lr,
    stat_inventory_limit=@il,
    stat_hp_regen_speed=@hr, stat_mp_regen_speed=@mr,
    stat_move_speed=@ms, stat_attack_speed=@as2,
    stat_cast_speed=@cs, stat_hit_recovery=@hrc,
    stat_jump_power=@jp, stat_weight=@wt, stat_level=@sl
WHERE character_id=@cid;", conn))
            {
                cmd.Transaction = tx;
                f.AddTo(cmd);
                cmd.Parameters.AddWithValue("@cid", characterId);
                // stat_level 固定 100, 与种子创建(SqliteSelectCharacterDataSource 建号 INSERT)保持一致;
                // 该字段非角色等级锚点, 属性面板由上方各 stat_* 列直接驱动, 升级不修改。
                cmd.Parameters.AddWithValue("@sl", 100);
                return cmd.ExecuteNonQuery();
            }
        }
    }
}
