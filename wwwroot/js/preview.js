// ---- 物品预览：名称旁图标 + 悬浮卡片 ----

const ITEM_PREVIEW_CACHE = new Map();
const ITEM_PREVIEW_DELAY = 160;
let itemPreviewCacheEpoch = -1;
let itemIconEpoch = 0;
let itemPreviewCard = null;
let itemPreviewShowTimer = 0;
let itemPreviewHideTimer = 0;
let itemPreviewSeq = 0;

function resetItemPreviewCache() {
  if (itemPreviewCacheEpoch === runtimeSourceEpoch) return;
  ITEM_PREVIEW_CACHE.clear();
  itemPreviewCacheEpoch = runtimeSourceEpoch;
}

function canPreviewIcons() {
  return Boolean(runtimeStatus && runtimeStatus.hasImagePacks);
}

function itemIconUrl(itemId) {
  resetItemPreviewCache();
  return `/api/items/${Number(itemId)}/icon?v=${runtimeSourceEpoch || 0}.${itemIconEpoch || 0}`;
}

function itemIconMarkup(itemId) {
  if (!canPreviewIcons())
    return '';
  return `<img class="item-icon" src="${itemIconUrl(itemId)}" alt="" width="28" height="28" loading="lazy" ` +
    `onerror="this.classList.add('missing')">`;
}

function refreshItemIcons() {
  itemIconEpoch++;
  ITEM_PREVIEW_CACHE.clear();
  itemPreviewCacheEpoch = runtimeSourceEpoch;
  document.querySelectorAll('.item-preview-host').forEach((host) => {
    const itemId = Number(host.dataset.itemId);
    const old = host.querySelector('.item-icon');
    if (!itemId) {
      if (old) old.remove();
      return;
    }
    const html = itemIconMarkup(itemId);
    if (old) {
      if (html)
        old.insertAdjacentHTML('afterend', html);
      old.remove();
      return;
    }
    if (html)
      host.insertAdjacentHTML('afterbegin', html);
  });
}

function itemPreviewName(itemId, name, rarity) {
  const id = Number(itemId);
  const label = name || (id ? '#' + id : '');
  const rarityClass = rarity >= 0 && rarity <= 6 ? ` rarity-${rarity}` : '';
  if (!id) return `<span>${escapeHtml(label)}</span>`;
  return `<span class="item-preview-host" data-item-id="${id}">` +
    itemIconMarkup(id) +
    `<span class="item-preview-label${rarityClass}">${escapeHtml(label)}</span></span>`;
}

function initItemPreview() {
  if (itemPreviewCard) return;
  itemPreviewCard = document.createElement('div');
  itemPreviewCard.id = 'item-preview';
  itemPreviewCard.className = 'item-preview hidden';
  itemPreviewCard.setAttribute('role', 'tooltip');
  document.body.appendChild(itemPreviewCard);

  document.addEventListener('pointerover', onItemPreviewPointerOver);
  document.addEventListener('pointerout', onItemPreviewPointerOut);
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') hideItemPreview(true);
  });
  window.addEventListener('scroll', () => hideItemPreview(true), true);
}

function onItemPreviewPointerOver(event) {
  const host = event.target && event.target.closest && event.target.closest('.item-preview-host');
  if (!host || itemPreviewCard.contains(event.target)) return;
  const itemId = Number(host.dataset.itemId);
  if (!itemId) return;
  clearTimeout(itemPreviewHideTimer);
  clearTimeout(itemPreviewShowTimer);
  itemPreviewShowTimer = setTimeout(() => showItemPreview(host, itemId), ITEM_PREVIEW_DELAY);
}

function onItemPreviewPointerOut(event) {
  const host = event.target && event.target.closest && event.target.closest('.item-preview-host');
  const fromCard = itemPreviewCard && itemPreviewCard.contains(event.target);
  if (!host && !fromCard) return;
  const next = event.relatedTarget;
  if (next && ((host && host.contains(next)) || itemPreviewCard.contains(next))) return;
  clearTimeout(itemPreviewShowTimer);
  itemPreviewHideTimer = setTimeout(() => hideItemPreview(false), 120);
}

function hideItemPreview(immediate) {
  clearTimeout(itemPreviewShowTimer);
  clearTimeout(itemPreviewHideTimer);
  itemPreviewSeq += 1;
  if (!itemPreviewCard) return;
  itemPreviewCard.classList.add('hidden');
  itemPreviewCard.innerHTML = '';
  if (immediate) itemPreviewCard.style.visibility = '';
}

async function showItemPreview(host, itemId) {
  if (!itemPreviewCard || !document.body.contains(host)) return;
  resetItemPreviewCache();
  const seq = ++itemPreviewSeq;
  let data = ITEM_PREVIEW_CACHE.get(itemId);
  if (!data) {
    itemPreviewCard.innerHTML = '<div class="item-preview-loading">加载预览…</div>';
    itemPreviewCard.classList.remove('hidden');
    placeItemPreview(host);
    try {
      data = await api(`/api/items/${itemId}/preview`);
      ITEM_PREVIEW_CACHE.set(itemId, data);
    } catch (error) {
      data = { success: false, error: error.message, itemId };
    }
  }
  if (seq !== itemPreviewSeq) return;
  applyItemPreviewChrome();
  itemPreviewCard.innerHTML = renderItemPreview(data);
  itemPreviewCard.classList.remove('hidden');
  placeItemPreview(host);
}

const DNF_NAMED_COLORS = {
  TRED: '#ff4040',
  TBLUE: '#4aa0ff',
  TGREEN: '#6bdc6b',
  TYELLOW: '#ffe27a',
  TPURPLE: '#b36bff',
  TPINK: '#ff66cc',
  TORANGE: '#ffb400',
  TGRAY: '#9a9a9a',
  TGREY: '#9a9a9a',
  TWHITE: '#ffffff',
  TBLACK: '#202020',
};

function previewChromeUrl() {
  return `/api/preview/chrome/window?v=${runtimeSourceEpoch || 0}.${itemIconEpoch || 0}`;
}

function applyItemPreviewChrome() {
  if (!itemPreviewCard) return;
  itemPreviewCard.classList.toggle('has-chrome', canPreviewIcons());
  if (canPreviewIcons())
    itemPreviewCard.style.borderImageSource = `url("${previewChromeUrl()}")`;
  else
    itemPreviewCard.style.borderImageSource = '';
}

function formatDnfText(text) {
  if (!text) return '';
  const escaped = escapeHtml(text);
  return escaped.replace(/\{#([0-9A-Fa-f]{3,8}|[A-Za-z][A-Za-z0-9 ]*)\}/g, (_, code) => {
    const named = DNF_NAMED_COLORS[code.toUpperCase()];
    if (named) return `<span style="color:${named}">`;
    if (/^[0-9A-Fa-f]{3}$|^[0-9A-Fa-f]{6}$|^[0-9A-Fa-f]{8}$/.test(code))
      return `<span style="color:#${code.length === 8 ? code.slice(0, 6) : code}">`;
    return '';
  }).replace(/\n/g, '<br>');
}

function renderItemPreview(data) {
  if (!data || data.success === false) {
    return `<div class="item-preview-loading">${escapeHtml((data && data.error) || '无法加载预览')}</div>`;
  }

  const rarityClass = data.rarity >= 0 && data.rarity <= 6 ? ` rarity-${data.rarity}` : '';
  const rarityText = data.special
    ? (SPECIAL_LABELS[data.special] || data.special)
    : (RARITY_LABELS[data.rarity] || '');
  const typeText = data.kind === 'stackable'
    ? (data.segment || tagLabel(data.tag))
    : tagLabel(data.tag);
  const sideMeta = [
    typeText,
    data.minLevel > 0 ? '需要等级 ' + data.minLevel : '',
    data.usableJob || '',
  ].filter(Boolean);

  const icon = data.hasIcon && canPreviewIcons()
    ? `<span class="item-preview-slot"><img class="item-preview-icon" src="${itemIconUrl(data.itemId)}" alt="" width="28" height="28" onerror="this.classList.add('missing')"></span>`
    : '';

  const sections = [];
  if (data.basicExplain) sections.push(previewSection(data.basicExplain));
  if (data.explain && data.explain !== data.basicExplain) sections.push(previewSection(data.explain));
  if (data.stats && data.stats.length)
    sections.push(`<div class="item-preview-stats">${data.stats.map((line) => formatDnfText(line)).join('<br>')}</div>`);
  if (data.detailExplain) sections.push(previewSection(data.detailExplain, 'item-preview-detail'));
  if (data.flavorText) sections.push(previewSection(data.flavorText, 'item-preview-flavor'));
  sections.push(`<div class="item-preview-expire">${templateExpirationLabel(data)}</div>`);
  sections.push(`<div class="item-preview-id">ID ${data.itemId}</div>`);

  return `<div class="item-preview-name${rarityClass}">${escapeHtml(data.name || ('#' + data.itemId))}</div>` +
    (rarityText ? `<div class="item-preview-grade${rarityClass}">${escapeHtml(rarityText)}</div>` : '') +
    `<div class="item-preview-head">` +
    icon +
    `<div class="item-preview-titles">` +
    sideMeta.map((line) => `<div class="item-preview-meta">${escapeHtml(line)}</div>`).join('') +
    `</div></div>` +
    sections.join('');
}

function previewSection(text, extraClass) {
  return `<div class="item-preview-text${extraClass ? ' ' + extraClass : ''}">${formatDnfText(text)}</div>`;
}

function placeItemPreview(host) {
  const card = itemPreviewCard;
  const rect = host.getBoundingClientRect();
  const margin = 10;
  const width = card.offsetWidth || 280;
  const height = card.offsetHeight || 120;
  let left = rect.right + margin;
  let top = rect.top;
  if (left + width > window.innerWidth - 8)
    left = rect.left - width - margin;
  if (left < 8) left = 8;
  if (top + height > window.innerHeight - 8)
    top = window.innerHeight - height - 8;
  if (top < 8) top = 8;
  card.style.left = Math.round(left) + 'px';
  card.style.top = Math.round(top) + 'px';
}
