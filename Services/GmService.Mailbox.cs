using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    public sealed partial class GmService
    {
        // GM 列表不走 LoadInboxPage: 那条路径会过期清理并只返回客户端一页。
        // 这里列出该角色未删除的收件箱/保管邮件(含已过期), 方便清理。
        public object ListMailbox(int characterId, PvfIndexService pvfIndex)
        {
            if (!TryGetAccountId(characterId, out _))
                return Error("角色不存在: " + characterId);

            var rows = new List<MailboxRow>();
            var attachmentsByMessage = new Dictionary<long, List<MailboxAttachmentRow>>();
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT
    m.message_id,
    m.sender_character_id,
    m.sender_name,
    m.title,
    m.body,
    m.gold,
    m.mail_type,
    m.unlimited_flag,
    m.expire_at,
    m.created_at,
    r.read_flag,
    r.saved_flag,
    r.received_gold_flag,
    CASE
        WHEN m.unlimited_flag != 0 OR m.expire_at >= '9999-01-01 00:00:00' THEN 0
        ELSE MIN(
            2147483647,
            MAX(0, CAST(strftime('%s', m.expire_at) AS INTEGER) - CAST(strftime('%s', 'now') AS INTEGER)))
    END AS remain_seconds,
    CASE
        WHEN m.unlimited_flag != 0 OR m.expire_at >= '9999-01-01 00:00:00' THEN 0
        WHEN m.expire_at <= CURRENT_TIMESTAMP THEN 1
        ELSE 0
    END AS expired
FROM mailbox_recipients r
JOIN mailbox_messages m ON m.message_id = r.message_id
WHERE r.character_id = @cid
  AND r.folder = 0
  AND r.deleted_flag = 0
ORDER BY datetime(m.created_at) DESC, m.message_id DESC;";
                    cmd.Parameters.AddWithValue("@cid", characterId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rows.Add(new MailboxRow
                            {
                                MessageId = reader.GetInt64(0),
                                SenderCharacterId = reader.GetInt32(1),
                                SenderName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                Title = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                Body = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                Gold = reader.GetInt32(5),
                                MailType = reader.GetInt32(6),
                                Unlimited = reader.GetInt32(7) != 0,
                                ExpireAt = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                                CreatedAt = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                                Read = reader.GetInt32(10) != 0,
                                Saved = reader.GetInt32(11) != 0,
                                GoldClaimed = reader.GetInt32(12) != 0,
                                RemainSeconds = reader.GetInt32(13),
                                Expired = reader.GetInt32(14) != 0,
                            });
                        }
                    }
                }

                if (rows.Count > 0)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT a.message_id, a.item_template_id, a.item_count, a.claimed_flag
FROM mailbox_attachments a
JOIN mailbox_recipients r ON r.message_id = a.message_id
WHERE r.character_id = @cid
  AND r.folder = 0
  AND r.deleted_flag = 0
ORDER BY a.message_id, a.ordinal, a.attachment_id;";
                        cmd.Parameters.AddWithValue("@cid", characterId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var messageId = reader.GetInt64(0);
                                List<MailboxAttachmentRow> attachments;
                                if (!attachmentsByMessage.TryGetValue(messageId, out attachments))
                                {
                                    attachments = new List<MailboxAttachmentRow>();
                                    attachmentsByMessage.Add(messageId, attachments);
                                }

                                attachments.Add(new MailboxAttachmentRow
                                {
                                    ItemTemplateId = reader.GetInt32(1),
                                    ItemCount = reader.GetInt32(2),
                                    ClaimedFlag = reader.GetInt32(3),
                                });
                            }
                        }
                    }
                }
            }

            var mails = new List<object>(rows.Count);
            foreach (var row in rows)
            {
                List<MailboxAttachmentRow> attachments;
                if (!attachmentsByMessage.TryGetValue(row.MessageId, out attachments))
                    attachments = new List<MailboxAttachmentRow>();

                var attachmentViews = new List<object>(attachments.Count);
                foreach (var attachment in attachments)
                {
                    attachmentViews.Add(new
                    {
                        itemId = attachment.ItemTemplateId,
                        name = pvfIndex.ResolveItemName(attachment.ItemTemplateId) ?? string.Empty,
                        count = attachment.ItemCount,
                        claimedFlag = attachment.ClaimedFlag,
                        claimed = attachment.ClaimedFlag != 0,
                    });
                }

                mails.Add(new
                {
                    messageId = row.MessageId,
                    senderCharacterId = row.SenderCharacterId,
                    senderName = row.SenderName,
                    title = row.Title,
                    body = row.Body,
                    gold = row.Gold,
                    goldClaimed = row.GoldClaimed,
                    mailType = row.MailType,
                    saved = row.Saved,
                    read = row.Read,
                    unlimited = row.Unlimited,
                    expireAt = row.ExpireAt,
                    createdAt = row.CreatedAt,
                    remainSeconds = row.RemainSeconds,
                    expired = row.Expired,
                    folder = row.Saved ? "保管" : "收件箱",
                    attachments = attachmentViews,
                });
            }

            return new { characterId, count = mails.Count, mails };
        }

        // GM 删除不要求先领附件: 只给收件人打删除标记, 与服务端 DeleteMail 落库位置相同。
        public object DeleteMailboxMessage(int characterId, long messageId)
        {
            if (!TryGetAccountId(characterId, out _))
                return Error("角色不存在: " + characterId);
            if (messageId <= 0)
                return Error("邮件 ID 无效");

            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    var deleted = MarkMailboxDeleted(conn, tx, characterId, messageId);
                    if (deleted <= 0)
                        return Error("邮件不存在或已删除");
                    tx.Commit();
                }
            }

            return new { success = true, characterId, messageId };
        }

        public object ClearMailbox(int characterId)
        {
            if (!TryGetAccountId(characterId, out _))
                return Error("角色不存在: " + characterId);

            int deleted;
            using (var conn = new SqliteConnection(_config.ConnectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    deleted = MarkMailboxDeleted(conn, tx, characterId, 0);
                    tx.Commit();
                }
            }

            return new { success = true, characterId, deleted };
        }

        private static int MarkMailboxDeleted(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            long messageId)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
UPDATE mailbox_recipients
SET deleted_flag = 1,
    read_flag = 1,
    read_at = COALESCE(read_at, CURRENT_TIMESTAMP),
    deleted_at = CURRENT_TIMESTAMP
WHERE character_id = @cid
  AND folder = 0
  AND deleted_flag = 0";
                if (messageId > 0)
                    cmd.CommandText += @"
  AND message_id = @messageId;";
                else
                    cmd.CommandText += ";";

                cmd.Parameters.AddWithValue("@cid", characterId);
                if (messageId > 0)
                    cmd.Parameters.AddWithValue("@messageId", messageId);
                return cmd.ExecuteNonQuery();
            }
        }

        private sealed class MailboxRow
        {
            public long MessageId;
            public int SenderCharacterId;
            public string SenderName;
            public string Title;
            public string Body;
            public int Gold;
            public int MailType;
            public bool Unlimited;
            public string ExpireAt;
            public string CreatedAt;
            public bool Read;
            public bool Saved;
            public bool GoldClaimed;
            public int RemainSeconds;
            public bool Expired;
        }

        private sealed class MailboxAttachmentRow
        {
            public int ItemTemplateId;
            public int ItemCount;
            public int ClaimedFlag;
        }
    }
}
