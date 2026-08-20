using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GmPvfLib;

namespace DfoGmTool.Services
{
    internal sealed class ExpertJobDefinition
    {
        public byte Type { get; set; }
        public string Name { get; set; }
        public IReadOnlyList<int> ExperienceThresholds { get; set; } = Array.Empty<int>();
        public IReadOnlyDictionary<int, int> AutoLearnRecipes { get; set; } =
            new Dictionary<int, int>();
        public IReadOnlyList<ExpertJobSkillGrant> SkillGrants { get; set; } =
            Array.Empty<ExpertJobSkillGrant>();
        public int InitialMachineGrade { get; set; }
        public int InitialEndurance { get; set; }
        public IReadOnlyList<int> RepairEnduranceCaps { get; set; } = Array.Empty<int>();

        public int MaxLevel => Math.Max(1, ExperienceThresholds.Count);

        public uint MaxExp => ExperienceThresholds.Count == 0
            ? 0
            : (uint)Math.Max(0, ExperienceThresholds[ExperienceThresholds.Count - 1]);

        public int GetLevel(uint experience)
        {
            var level = 1;
            foreach (var threshold in ExperienceThresholds)
            {
                if (experience < threshold)
                    break;
                level++;
            }
            return Math.Min(MaxLevel, level);
        }

        public uint GetExpForLevel(int level)
        {
            if (level <= 1 || ExperienceThresholds.Count == 0)
                return 0;
            if (level >= MaxLevel)
                return MaxExp;
            return (uint)Math.Max(0, ExperienceThresholds[level - 2]);
        }

        public IReadOnlyList<int> GetAutoLearnRecipeIds(uint experience)
        {
            var level = GetLevel(experience);
            return AutoLearnRecipes
                .Where(pair => pair.Key <= level)
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value)
                .ToArray();
        }

        public int MaxMachineGrade => RepairEnduranceCaps.Count > 0
            ? RepairEnduranceCaps.Count
            : InitialMachineGrade;

        public int GetEnduranceCap(int machineGrade)
        {
            var index = machineGrade - 1;
            if (index >= 0 && index < RepairEnduranceCaps.Count)
                return RepairEnduranceCaps[index];
            return Math.Max(0, InitialEndurance);
        }
    }

    internal readonly struct ExpertJobSkillGrant
    {
        public ExpertJobSkillGrant(ushort skillId, byte level)
        {
            SkillId = skillId;
            Level = level;
        }

        public ushort SkillId { get; }
        public byte Level { get; }
    }

    internal sealed class ExpertJobPvfData
    {
        internal const byte EnchanterType = 1;
        internal const byte AlchemistType = 2;
        internal const byte DisjointerType = 3;
        internal const byte DollControllerType = 4;

        private static readonly (byte Type, string Name, string Path, bool HasMachine)[] Jobs =
        {
            (EnchanterType, "附魔师", "character/expertjob/enchanter.exj", true),
            (AlchemistType, "炼金术师", "character/expertjob/alchemist.exj", false),
            (DisjointerType, "分解师", "character/expertjob/disjointer.exj", true),
            (DollControllerType, "控偶师", "character/expertjob/doll_controller.exj", false),
        };

        private readonly string _pvfPath;
        private readonly Lazy<IReadOnlyDictionary<byte, ExpertJobDefinition>> _definitions;

        public ExpertJobPvfData(string pvfPath)
        {
            if (string.IsNullOrWhiteSpace(pvfPath))
                throw new ArgumentException("PVF path cannot be null or empty.", nameof(pvfPath));

            _pvfPath = pvfPath;
            _definitions = new Lazy<IReadOnlyDictionary<byte, ExpertJobDefinition>>(Load);
        }

        public IReadOnlyList<ExpertJobDefinition> All
        {
            get { return _definitions.Value.Values.OrderBy(job => job.Type).ToArray(); }
        }

        public bool TryGet(int expertJobType, out ExpertJobDefinition definition)
        {
            definition = null;
            if (expertJobType <= 0 || expertJobType > byte.MaxValue)
                return false;
            return _definitions.Value.TryGetValue((byte)expertJobType, out definition);
        }

        private IReadOnlyDictionary<byte, ExpertJobDefinition> Load()
        {
            using (var archive = PvfArchive.Open(_pvfPath))
            {
                var result = new Dictionary<byte, ExpertJobDefinition>();
                foreach (var job in Jobs)
                {
                    var text = archive.GetFileContent(job.Path);
                    if (string.IsNullOrWhiteSpace(text))
                        throw new InvalidDataException("PVF 中缺少副职业定义: " + job.Path);

                    result[job.Type] = Parse(job.Type, job.Name, job.Path, text, job.HasMachine);
                }

                return result;
            }
        }

        private static ExpertJobDefinition Parse(
            byte type,
            string name,
            string pvfPath,
            string content,
            bool hasMachine)
        {
            var root = new ScriptParser().Parse(content);
            var thresholds = ParseExperienceThresholds(
                ReadTokens(root, content, "expertness exp"),
                pvfPath);
            var autoLearn = ParsePositivePairs(
                ReadTokens(root, content, "auto learn recipe"),
                allowEmpty: true);
            var skills = ParseSkillGrants(ReadTokens(root, content, "skill"));
            var initialEndurance = ReadFirstInt(root, content, "endurance initial value");
            var repairCaps = ParseRepairEnduranceCaps(
                ReadTokens(root, content, "endurance repair cost"));

            return new ExpertJobDefinition
            {
                Type = type,
                Name = name,
                ExperienceThresholds = thresholds,
                AutoLearnRecipes = autoLearn,
                SkillGrants = skills,
                InitialMachineGrade = hasMachine && type == DisjointerType ? 1 : 0,
                InitialEndurance = Math.Max(0, initialEndurance),
                RepairEnduranceCaps = repairCaps,
            };
        }

        private static List<int> ParseExperienceThresholds(string[] tokens, string pvfPath)
        {
            if (tokens.Length == 0 || tokens.Length % 3 != 0)
                throw new InvalidDataException("PVF " + pvfPath + " [expertness exp] 行宽不是 3");

            var thresholds = new List<int>();
            var previous = -1;
            for (var index = 0; index < tokens.Length; index += 3)
            {
                var threshold = ParseInt(tokens[index]);
                if (threshold <= previous)
                    throw new InvalidDataException("PVF " + pvfPath + " 副职业经验阈值无效");
                thresholds.Add(threshold);
                previous = threshold;
            }

            return thresholds;
        }

        private static Dictionary<int, int> ParsePositivePairs(string[] tokens, bool allowEmpty)
        {
            var result = new Dictionary<int, int>();
            if (tokens.Length == 0)
            {
                if (allowEmpty)
                    return result;
                return result;
            }

            if (tokens.Length % 2 != 0)
                return result;

            for (var index = 0; index < tokens.Length; index += 2)
            {
                var key = ParseInt(tokens[index]);
                var value = ParseInt(tokens[index + 1]);
                if (key <= 0 || value <= 0 || result.ContainsKey(key))
                    continue;
                result.Add(key, value);
            }

            return result;
        }

        private static List<ExpertJobSkillGrant> ParseSkillGrants(string[] tokens)
        {
            var result = new List<ExpertJobSkillGrant>();
            if (tokens.Length == 0 || tokens.Length % 2 != 0)
                return result;

            for (var index = 0; index < tokens.Length; index += 2)
            {
                var skillId = ParseInt(tokens[index]);
                var skillLevel = ParseInt(tokens[index + 1]);
                if (skillId <= 0 || skillId > ushort.MaxValue || skillLevel < 0 || skillLevel > byte.MaxValue)
                    continue;
                result.Add(new ExpertJobSkillGrant(
                    (ushort)skillId,
                    (byte)Math.Max(1, skillLevel)));
            }

            return result;
        }

        private static List<int> ParseRepairEnduranceCaps(string[] tokens)
        {
            var result = new List<int>();
            if (tokens.Length == 0 || tokens.Length % 2 != 0)
                return result;

            for (var index = 0; index < tokens.Length; index += 2)
            {
                var cap = ParseInt(tokens[index + 1]);
                if (cap <= 0)
                    continue;
                result.Add(cap);
            }

            return result;
        }

        private static int ReadFirstInt(ScriptNode root, string content, string tag)
        {
            var tokens = ReadTokens(root, content, tag);
            return tokens.Length == 0 ? 0 : Math.Max(0, ParseInt(tokens[0]));
        }

        private static string[] ReadTokens(ScriptNode root, string content, string tag)
        {
            var node = root?.Children.FirstOrDefault(child =>
                string.Equals(child.Tag, tag, StringComparison.OrdinalIgnoreCase));
            return node == null
                ? Array.Empty<string>()
                : node.GetFirstDataContent(content)
                    .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        private static int ParseInt(string value)
        {
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
