// ---- 角色邮箱 ----

function mailboxSubject(mail) {
  const title = String(mail.title || '').trim();
  if (title) return title;
  return String(mail.body || '').trim() || '(无标题)';
}

function mailboxClaimLabel(flag) {
  if (flag === 2) return '领取中';
  return flag ? '已领' : '未领';
}

function mailboxGoldLabel(mail) {
  if (!mail.gold) return '—';
  return mail.gold.toLocaleString() + ' (' + mailboxClaimLabel(mail.goldClaimed ? 1 : 0) + ')';
}

function mailboxAttachmentLabel(mail) {
  if (!mail.attachments || mail.attachments.length === 0) return '—';
  return mail.attachments.map((item) => {
    const name = item.name || ('#' + item.itemId);
    return `${itemPreviewName(item.itemId, name, item.rarity)} ×${item.count} (${mailboxClaimLabel(item.claimedFlag)})`;
  }).join('<br>');
}

function mailboxStatusLabel(mail) {
  const parts = [mail.folder || '收件箱'];
  if (mail.expired) parts.push('已过期');
  parts.push(mail.read ? '已读' : '未读');
  return parts.join(' · ');
}

function mailboxExpireLabel(mail) {
  if (mail.unlimited) return '永久';
  if (mail.expired) return '已过期';
  if (mail.remainSeconds > 0) return formatRemainingTime(mail.remainSeconds);
  return mail.expireAt || '—';
}

function renderMailbox(data) {
  const body = $('#mail-table tbody');
  const mails = (data && data.mails) || [];
  $('#mail-count').textContent = mails.length + ' 封';
  body.innerHTML = '';
  if (mails.length === 0) {
    body.innerHTML = '<tr><td colspan="8" class="hint">邮箱为空</td></tr>';
    return;
  }

  for (const mail of mails) {
    const tr = document.createElement('tr');
    const subject = mailboxSubject(mail);
    const bodyText = String(mail.body || '').trim();
    tr.innerHTML = `<td>${mail.messageId}</td>
      <td>${escapeHtml(mail.senderName || '系统')}</td>
      <td title="${escapeHtml(bodyText || subject)}">${escapeHtml(subject)}</td>
      <td>${mailboxGoldLabel(mail)}</td>
      <td class="mail-attachments">${mailboxAttachmentLabel(mail)}</td>
      <td>${escapeHtml(mailboxStatusLabel(mail))}</td>
      <td>${escapeHtml(mailboxExpireLabel(mail))}</td>
      <td><button class="mini danger">删除</button></td>`;
    tr.querySelector('button').onclick = () => deleteMailboxMessage(mail.messageId);
    body.appendChild(tr);
  }
}

async function loadMailbox() {
  if (!currentChar) return;
  const epoch = selectEpoch;
  const body = $('#mail-table tbody');
  try {
    const data = await api(`/api/characters/${currentChar.characterId}/mail`);
    if (epoch !== selectEpoch) return;
    renderMailbox(data);
  } catch (e) {
    if (epoch !== selectEpoch) return;
    $('#mail-count').textContent = '';
    body.innerHTML = `<tr><td colspan="8" class="hint">${escapeHtml(e.message)}</td></tr>`;
    toast(e.message, true);
  }
}

async function deleteMailboxMessage(messageId) {
  if (!currentChar) return;
  try {
    await post(`/api/characters/${currentChar.characterId}/mail/delete`, { messageId });
    toast('已删除邮件 #' + messageId);
    loadMailbox();
  } catch (e) {
    toast(e.message, true);
  }
}

async function clearMailbox() {
  if (!currentChar) return;
  const countText = $('#mail-count').textContent || '';
  const count = parseInt(countText, 10);
  if (!count)
    return toast('邮箱已是空的', true);
  if (!confirm(`清空该角色邮箱共 ${count} 封邮件？未领取附件不会进背包，此操作不可撤销。`))
    return;
  try {
    const result = await post(`/api/characters/${currentChar.characterId}/mail/clear`);
    toast(`已清空邮箱 (${result.deleted} 封)`);
    loadMailbox();
  } catch (e) {
    toast(e.message, true);
  }
}
