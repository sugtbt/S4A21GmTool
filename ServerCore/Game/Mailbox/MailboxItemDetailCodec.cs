using DfoGmTool.ServerCore.Game.Inventory;
using System;
using System.Text.Json;

namespace DfoGmTool.ServerCore.Game.Mailbox
{
    internal static class MailboxItemDetailCodec
    {
        internal static string Capture(InventoryService inventory, ItemCore core)
        {
            if (inventory == null || core == null)
                return string.Empty;

            if (core.ItemKind == ItemCore.KindAvatar
                && inventory.AvatarDetails.TryGetDetail(core.AvatarUid, out var avatar))
            {
                return JsonSerializer.Serialize(new DetailEnvelope
                {
                    Kind = "avatar",
                    Avatar = new AvatarPayload
                    {
                        ExpireDate = avatar.ExpireDate,
                        ClearAvatarId = avatar.ClearAvatarId,
                        JewelSocket = avatar.JewelSocket,
                        Color1 = avatar.Color1,
                        Color2 = avatar.Color2,
                        DeleteDate = avatar.DeleteDate,
                    }
                });
            }

            if (core.ItemKind == ItemCore.KindCreature
                && inventory.CreatureDetails.TryGetDetail(core.CreatureUid, out var creature))
            {
                return JsonSerializer.Serialize(new DetailEnvelope
                {
                    Kind = "creature",
                    Creature = new CreaturePayload
                    {
                        NameBytes = creature.NameBytes,
                        Field04 = creature.Field04,
                        ModeFlag = creature.ModeFlag,
                        Mode1Field0A = creature.Mode1Field0A,
                        Mode1Field0B = creature.Mode1Field0B,
                        ProgressValue32 = creature.ProgressValue32,
                        FieldAfterValue32 = creature.FieldAfterValue32,
                        ExpireDate = creature.ExpireDate,
                        TailFlag = creature.TailFlag,
                    }
                });
            }

            return string.Empty;
        }

        internal static InventoryCreateOptions BuildCreateOptions(string detailJson)
        {
            if (string.IsNullOrWhiteSpace(detailJson))
                return null;

            try
            {
                var envelope = JsonSerializer.Deserialize<DetailEnvelope>(detailJson);
                if (envelope?.Avatar != null)
                {
                    return new InventoryCreateOptions
                    {
                        AvatarDetailTemplate = new AvatarDetail
                        {
                            ExpireDate = envelope.Avatar.ExpireDate,
                            ClearAvatarId = envelope.Avatar.ClearAvatarId,
                            JewelSocket = envelope.Avatar.JewelSocket ?? Array.Empty<byte>(),
                            Color1 = envelope.Avatar.Color1,
                            Color2 = envelope.Avatar.Color2,
                            DeleteDate = envelope.Avatar.DeleteDate,
                        }
                    };
                }

                if (envelope?.Creature != null)
                {
                    return new InventoryCreateOptions
                    {
                        CreatureDetailTemplate = new CreatureDetail
                        {
                            NameBytes = envelope.Creature.NameBytes ?? Array.Empty<byte>(),
                            Field04 = envelope.Creature.Field04,
                            ModeFlag = envelope.Creature.ModeFlag,
                            Mode1Field0A = envelope.Creature.Mode1Field0A,
                            Mode1Field0B = envelope.Creature.Mode1Field0B,
                            ProgressValue32 = envelope.Creature.ProgressValue32,
                            FieldAfterValue32 = envelope.Creature.FieldAfterValue32,
                            ExpireDate = envelope.Creature.ExpireDate,
                            TailFlag = envelope.Creature.TailFlag,
                        }
                    };
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }

        private sealed class DetailEnvelope
        {
            public string Kind { get; set; } = string.Empty;
            public AvatarPayload Avatar { get; set; }
            public CreaturePayload Creature { get; set; }
        }

        private sealed class AvatarPayload
        {
            public int ExpireDate { get; set; }
            public int ClearAvatarId { get; set; }
            public byte[] JewelSocket { get; set; } = Array.Empty<byte>();
            public ushort Color1 { get; set; }
            public ushort Color2 { get; set; }
            public int DeleteDate { get; set; }
        }

        private sealed class CreaturePayload
        {
            public byte[] NameBytes { get; set; } = Array.Empty<byte>();
            public byte Field04 { get; set; }
            public byte ModeFlag { get; set; }
            public byte Mode1Field0A { get; set; }
            public byte Mode1Field0B { get; set; }
            public int ProgressValue32 { get; set; }
            public int FieldAfterValue32 { get; set; }
            public int ExpireDate { get; set; }
            public byte TailFlag { get; set; }
        }
    }
}
