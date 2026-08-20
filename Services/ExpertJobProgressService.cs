using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.CharacterData;
using DfoGmTool.ServerCore.Game.Skills;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    internal sealed class ExpertJobProgressSnapshot
    {
        public byte Type { get; set; }
        public string TypeName { get; set; }
        public uint Exp { get; set; }
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        public uint MaxExp { get; set; }
        public uint CurrentLevelExp { get; set; }
        public int LearnedRecipeCount { get; set; }
        public int MachineGrade { get; set; }
        public int MachineEndurance { get; set; }
        public int MaxMachineGrade { get; set; }
        public List<object> Options { get; set; }
    }

    internal sealed class ExpertJobProgressService
    {
        private readonly string _connectionString;
        private readonly ExpertJobPvfData _pvfData;

        public ExpertJobProgressService(string connectionString, string pvfPath)
        {
            _connectionString = connectionString;
            _pvfData = new ExpertJobPvfData(pvfPath);
        }

        public bool TryLoad(int characterId, out ExpertJobProgressSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (!TryLoadRecord(characterId, out var type, out var exp, out var recipes, out var grade, out var endurance, out error))
                return false;

            snapshot = BuildSnapshot(type, exp, recipes, grade, endurance);
            return true;
        }

        public bool TrySet(
            int characterId,
            int requestedType,
            int? requestedLevel,
            long? requestedExp,
            bool maxLevel,
            out ExpertJobProgressSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (requestedType < 0 || requestedType > byte.MaxValue)
            {
                error = "副职业类型无效。";
                return false;
            }

            ExpertJobDefinition definition = null;
            if (requestedType > 0 && !_pvfData.TryGet(requestedType, out definition))
            {
                error = "未知的副职业类型。";
                return false;
            }

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    if (!TryLoadRecordInTransaction(
                            connection,
                            transaction,
                            characterId,
                            out var currentType,
                            out var currentExp,
                            out _,
                            out _,
                            out _,
                            out error))
                        return false;

                    var nextType = (byte)requestedType;
                    if (maxLevel && nextType == 0)
                    {
                        if (currentType == 0)
                        {
                            error = "请先选择副职业再一键满级。";
                            return false;
                        }

                        nextType = currentType;
                        if (!_pvfData.TryGet(nextType, out definition))
                        {
                            error = "未知的副职业类型。";
                            return false;
                        }
                    }

                    uint nextExp = 0;
                    if (nextType == 0)
                    {
                        nextExp = 0;
                    }
                    else if (definition == null)
                    {
                        error = "未知的副职业类型。";
                        return false;
                    }
                    else if (maxLevel)
                    {
                        nextExp = definition.MaxExp;
                    }
                    else if (requestedLevel.HasValue)
                    {
                        if (requestedLevel.Value < 1 || requestedLevel.Value > definition.MaxLevel)
                        {
                            error = "副职业等级范围 1-" + definition.MaxLevel + "。";
                            return false;
                        }

                        nextExp = definition.GetExpForLevel(requestedLevel.Value);
                    }
                    else if (requestedExp.HasValue)
                    {
                        if (requestedExp.Value < 0)
                        {
                            error = "副职业经验不能为负数。";
                            return false;
                        }

                        var capped = requestedExp.Value > definition.MaxExp
                            ? definition.MaxExp
                            : (uint)requestedExp.Value;
                        nextExp = capped;
                    }
                    else if (nextType == currentType)
                    {
                        nextExp = currentExp > definition.MaxExp ? definition.MaxExp : currentExp;
                    }

                    if (!WriteSubtype0(connection, transaction, characterId, nextType, nextExp))
                    {
                        error = "写入副职业类型/经验失败。";
                        return false;
                    }

                    WriteDomainState(
                        connection,
                        transaction,
                        characterId,
                        definition,
                        nextType,
                        nextExp,
                        typeChanged: nextType != currentType,
                        maxLevel);

                    if (nextType != currentType)
                    {
                        if (currentType > 0
                            && _pvfData.TryGet(currentType, out var previous)
                            && !RemoveSkills(connection, transaction, characterId, previous.SkillGrants))
                        {
                            error = "清除旧副职业技能失败。";
                            return false;
                        }

                        if (nextType > 0
                            && !GrantSkills(connection, transaction, characterId, definition.SkillGrants))
                        {
                            error = "写入副职业技能失败。";
                            return false;
                        }
                    }

                    transaction.Commit();
                }
            }

            return TryLoad(characterId, out snapshot, out error);
        }

        private ExpertJobProgressSnapshot BuildSnapshot(
            byte type,
            uint exp,
            int recipes,
            int grade,
            int endurance)
        {
            _pvfData.TryGet(type, out var definition);
            var level = definition != null ? definition.GetLevel(exp) : 0;
            var maxLevel = definition != null ? definition.MaxLevel : 0;
            var maxExp = definition != null ? definition.MaxExp : 0u;
            var currentLevelExp = 0u;
            if (definition != null && level > 1)
                currentLevelExp = exp;

            var options = new List<object>
            {
                new { type = 0, name = "无副职业", maxLevel = 0, maxExp = 0 },
            };
            foreach (var job in _pvfData.All)
            {
                options.Add(new
                {
                    type = (int)job.Type,
                    name = job.Name,
                    maxLevel = job.MaxLevel,
                    maxExp = job.MaxExp,
                });
            }

            return new ExpertJobProgressSnapshot
            {
                Type = type,
                TypeName = definition != null ? definition.Name : "无副职业",
                Exp = type == 0 ? 0 : exp,
                Level = type == 0 ? 0 : level,
                MaxLevel = maxLevel,
                MaxExp = maxExp,
                CurrentLevelExp = currentLevelExp,
                LearnedRecipeCount = recipes,
                MachineGrade = grade,
                MachineEndurance = endurance,
                MaxMachineGrade = definition != null ? definition.MaxMachineGrade : 0,
                Options = options,
            };
        }

        private bool TryLoadRecord(
            int characterId,
            out byte type,
            out uint exp,
            out int recipes,
            out int grade,
            out int endurance,
            out string error)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                return TryLoadRecordInTransaction(
                    connection,
                    null,
                    characterId,
                    out type,
                    out exp,
                    out recipes,
                    out grade,
                    out endurance,
                    out error);
            }
        }

        private static bool TryLoadRecordInTransaction(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            out byte type,
            out uint exp,
            out int recipes,
            out int grade,
            out int endurance,
            out string error)
        {
            type = 0;
            exp = 0;
            recipes = 0;
            grade = 0;
            endurance = 0;
            error = null;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT c.character_id,
       COALESCE(f.expert_job_type, 0),
       COALESCE(f.expert_job_exp, 0),
       COALESCE(e.disjoint_machine_grade, 0),
       COALESCE(e.disjoint_machine_endurance, 0),
       COALESCE(e.enchanter_endurance, 0)
FROM characters c
LEFT JOIN character_subtype0_fields f ON f.character_id = c.character_id
LEFT JOIN character_expert_job e ON e.character_id = c.character_id
WHERE c.character_id = @cid AND c.delete_flag = 0;";
                command.Parameters.AddWithValue("@cid", characterId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        error = "角色不存在: " + characterId;
                        return false;
                    }

                    var storedType = reader.GetInt32(1);
                    type = storedType > 0 && storedType <= byte.MaxValue ? (byte)storedType : (byte)0;
                    var storedExp = reader.GetInt64(2);
                    exp = storedExp < 0 ? 0 : storedExp > uint.MaxValue ? uint.MaxValue : (uint)storedExp;
                    grade = Math.Max(0, reader.GetInt32(3));
                    endurance = type == ExpertJobPvfData.EnchanterType
                        ? Math.Max(0, reader.GetInt32(5))
                        : Math.Max(0, reader.GetInt32(4));
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(*)
FROM character_expert_job_recipes
WHERE character_id = @cid;";
                command.Parameters.AddWithValue("@cid", characterId);
                recipes = Convert.ToInt32(command.ExecuteScalar());
            }

            return true;
        }

        private static bool WriteSubtype0(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            byte type,
            uint exp)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO character_subtype0_fields (character_id, expert_job_type, expert_job_exp)
VALUES (@cid, @type, @exp)
ON CONFLICT(character_id) DO UPDATE SET
    expert_job_type=excluded.expert_job_type,
    expert_job_exp=excluded.expert_job_exp;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@type", (int)type);
                command.Parameters.AddWithValue("@exp", (long)exp);
                return command.ExecuteNonQuery() == 1;
            }
        }

        private static void WriteDomainState(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            ExpertJobDefinition definition,
            byte type,
            uint exp,
            bool typeChanged,
            bool maxLevel)
        {
            var resetMachine = typeChanged || maxLevel || type == 0;
            var grade = 0;
            var disjointEndurance = 0;
            var enchanterEndurance = 0;
            if (type == ExpertJobPvfData.DisjointerType && definition != null)
            {
                grade = maxLevel ? Math.Max(1, definition.MaxMachineGrade) : Math.Max(1, definition.InitialMachineGrade);
                disjointEndurance = definition.GetEnduranceCap(grade);
            }
            else if (type == ExpertJobPvfData.EnchanterType && definition != null)
            {
                enchanterEndurance = maxLevel
                    ? definition.GetEnduranceCap(Math.Max(1, definition.MaxMachineGrade))
                    : definition.InitialEndurance;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO character_expert_job (
    character_id, giveup_count,
    disjoint_machine_grade, disjoint_machine_endurance,
    enchanter_endurance, updated_at)
VALUES (@cid, 0, @grade, @endurance, @enchanterEndurance, CURRENT_TIMESTAMP)
ON CONFLICT(character_id) DO UPDATE SET
    disjoint_machine_grade=CASE WHEN @resetMachine=1 THEN excluded.disjoint_machine_grade ELSE character_expert_job.disjoint_machine_grade END,
    disjoint_machine_endurance=CASE WHEN @resetMachine=1 THEN excluded.disjoint_machine_endurance ELSE character_expert_job.disjoint_machine_endurance END,
    enchanter_endurance=CASE WHEN @resetMachine=1 THEN excluded.enchanter_endurance ELSE character_expert_job.enchanter_endurance END,
    updated_at=CURRENT_TIMESTAMP;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.Parameters.AddWithValue("@grade", grade);
                command.Parameters.AddWithValue("@endurance", disjointEndurance);
                command.Parameters.AddWithValue("@enchanterEndurance", enchanterEndurance);
                command.Parameters.AddWithValue("@resetMachine", resetMachine ? 1 : 0);
                command.ExecuteNonQuery();
            }

            var expected = type > 0 && definition != null
                ? definition.GetAutoLearnRecipeIds(exp)
                : Array.Empty<int>();

            if (type == 0 || typeChanged)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM character_expert_job_recipes WHERE character_id=@cid;";
                    command.Parameters.AddWithValue("@cid", characterId);
                    command.ExecuteNonQuery();
                }
            }

            foreach (var recipeId in expected)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT OR IGNORE INTO character_expert_job_recipes (character_id, recipe_id)
VALUES (@cid, @recipe);";
                    command.Parameters.AddWithValue("@cid", characterId);
                    command.Parameters.AddWithValue("@recipe", recipeId);
                    command.ExecuteNonQuery();
                }
            }
        }

        private bool GrantSkills(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            IReadOnlyList<ExpertJobSkillGrant> grants)
        {
            if (grants == null || grants.Count == 0)
                return true;

            byte job;
            byte level;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT job, level FROM characters WHERE character_id=@cid;";
                command.Parameters.AddWithValue("@cid", characterId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return false;
                    job = (byte)reader.GetInt32(0);
                    level = (byte)Math.Max(1, Math.Min(255, reader.GetInt32(1)));
                }
            }

            var repository = SqliteCharacterProgressRepository.FromConnectionString(_connectionString);
            var skills = repository.LoadSkills(connection, transaction, characterId);
            var skillGrants = new List<CharacterSkillProfile.SkillGrant>(grants.Count);
            foreach (var grant in grants)
            {
                skillGrants.Add(new CharacterSkillProfile.SkillGrant
                {
                    SkillIndex = grant.SkillId,
                    Level = grant.Level,
                });
            }

            CharacterSkillProfile.MergeGrants(skills, skillGrants, job, level);
            repository.SaveSkillProgress(connection, transaction, characterId, skills);
            return true;
        }

        private static bool RemoveSkills(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            IReadOnlyList<ExpertJobSkillGrant> grants)
        {
            if (grants == null || grants.Count == 0)
                return true;

            foreach (var grant in grants)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
DELETE FROM character_skills
WHERE character_id=@cid AND skill_id=@sid;";
                    command.Parameters.AddWithValue("@cid", characterId);
                    command.Parameters.AddWithValue("@sid", (int)grant.SkillId);
                    command.ExecuteNonQuery();
                }
            }

            return true;
        }
    }
}
