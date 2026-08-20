using System;
using System.Collections.Generic;
using DfoGmTool.ServerCore.Game.ItemUpgrade;

namespace DfoGmTool.Services
{
    public sealed class EquipmentGrantOptions
    {
        public string State { get; set; }

        public int UpgradeLevel { get; set; }

        public int AmplifyType { get; set; }

        public int ForgingLevel { get; set; }

        public string QualityMode { get; set; }
    }

    internal readonly struct EquipmentGrantCapabilities
    {
        public EquipmentGrantCapabilities(
            bool canReinforce,
            bool canHaveAmplifyState,
            bool canAmplifyLevel,
            bool isWeapon)
        {
            CanReinforce = canReinforce;
            CanHaveAmplifyState = canHaveAmplifyState;
            CanAmplifyLevel = canAmplifyLevel;
            IsWeapon = isWeapon;
        }

        public bool CanReinforce { get; }

        public bool CanHaveAmplifyState { get; }

        public bool CanAmplifyLevel { get; }

        public bool IsWeapon { get; }
    }

    internal static class EquipmentGrantPolicy
    {
        internal static EquipmentGrantCapabilities Evaluate(
            string equipmentType,
            int rarity,
            int minimumLevel,
            IReadOnlyList<string> impossibleContents,
            int amplifyMinimumLevel)
        {
            var type = EquipmentTypeInfo.ParseOrUnknown(equipmentType);
            var isUpgradeTarget = EquipmentTypeInfo.IsUpgradeTargetType(type);
            var canHaveAmplifyState = isUpgradeTarget
                && rarity >= 2
                && minimumLevel >= amplifyMinimumLevel;

            return new EquipmentGrantCapabilities(
                isUpgradeTarget && !Contains(impossibleContents, "upgrade"),
                canHaveAmplifyState,
                canHaveAmplifyState && !Contains(impossibleContents, "amplify upgrade"),
                EquipmentTypeInfo.IsWeapon(type));
        }

        private static bool Contains(IReadOnlyList<string> values, string expected)
        {
            if (values == null)
                return false;

            foreach (var value in values)
            {
                if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
