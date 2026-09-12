(function () {
  'use strict';

  const storageKey = 'stockTrackerTradePlans.v1';
  const byId = id => document.getElementById(id);
  let activeStock = null;

  function readPlans() {
    try {
      const parsed = JSON.parse(localStorage.getItem(storageKey) || '{}');
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch (_) {
      return {};
    }
  }

  function writePlans(plans) {
    localStorage.setItem(storageKey, JSON.stringify(plans));
  }

  function property(object, camelName, pascalName) {
    if (!object) return undefined;
    return object[camelName] !== undefined ? object[camelName] : object[pascalName];
  }

  function priceTick(price) {
    if (price < 10) return 0.01;
    if (price < 50) return 0.05;
    if (price < 100) return 0.1;
    if (price < 500) return 0.5;
    if (price < 1000) return 1;
    return 5;
  }

  function roundTick(price, up) {
    if (!(price > 0)) return 0;
    const tick = priceTick(price);
    return (up ? Math.ceil(price / tick) : Math.floor(price / tick)) * tick;
  }

  function nextWeekday() {
    const date = new Date();
    date.setHours(0, 0, 0, 0);
    date.setDate(date.getDate() + 1);
    while (date.getDay() === 0 || date.getDay() === 6) date.setDate(date.getDate() + 1);
    return date;
  }

  function dateText(date) {
    const yy = date.getFullYear();
    const mm = String(date.getMonth() + 1).padStart(2, '0');
    const dd = String(date.getDate()).padStart(2, '0');
    return yy + '/' + mm + '/' + dd;
  }

  function toNumber(value) {
    const number = Number.parseFloat(value);
    return Number.isFinite(number) ? number : 0;
  }

  function formatPrice(value) {
    return Number(value || 0).toFixed(2);
  }

  function getZones(structure, name) {
    const zones = property(structure, name, name.charAt(0).toUpperCase() + name.slice(1));
    return Array.isArray(zones) ? zones : [];
  }

  function zoneLow(zone) { return Number(property(zone, 'low', 'Low') || 0); }
  function zoneHigh(zone) { return Number(property(zone, 'high', 'High') || 0); }
  function zoneStrength(zone) { return Number(property(zone, 'strength', 'Strength') || 0); }
  function zoneTouches(zone) { return Number(property(zone, 'touches', 'Touches') || 0); }
  function zoneText(zone) {
    return zone ? formatPrice(zoneLow(zone)) + ' ～ ' + formatPrice(zoneHigh(zone)) + '（強度 ' + zoneStrength(zone) + '／觸及 ' + zoneTouches(zone) + ' 次）' : '—';
  }

  function zoneBuffer(stock, zone) {
    const structure = stock.priceStructure || stock.PriceStructure;
    const atr = Number(property(structure, 'atr14', 'Atr14') || 0);
    const width = Math.max(0, zoneHigh(zone) - zoneLow(zone));
    return Math.max(priceTick(Number(stock.price || 0)), Math.max(atr, width) * 0.15);
  }

  function emptyPlan(stock, strategy, quality, message) {
    return {
      symbol: stock.symbol,
      name: stock.name,
      strategy,
      entryLower: 0,
      entryUpper: 0,
      stop: 0,
      targetOne: 0,
      targetTwo: 0,
      riskBudget: 0,
      available: false,
      cancellation: '尚未產生結構化條件；若自行輸入價格，請自行確認支撐區下緣與壓力區上緣。',
      quality,
      structureText: message,
      initialStatus: message,
      validUntil: dateText(nextWeekday()),
      savedAt: ''
    };
  }

  function buildDefaultPlan(stock, strategy) {
    const isPullback = strategy === 'pullback';
    const structure = stock.priceStructure || stock.PriceStructure;
    const leadership = stock.leadershipLabel || '資料不足';
    const quality = '分數 ' + stock.score + '／風險 ' + stock.crash + '；' + leadership + '，外資敏感度 ' + (stock.foreignSensitivity || 0) + '／投信敏感度 ' + (stock.trustSensitivity || 0);
    const hasStructure = Boolean(property(structure, 'hasSufficientData', 'HasSufficientData'));
    if (!hasStructure) {
      return emptyPlan(stock, strategy, quality, '此掃描快照尚未含日 K 結構區。請重新執行全市場掃描後再自動預填；所有欄位仍可手動填寫。');
    }

    const supports = getZones(structure, 'supports');
    const resistances = getZones(structure, 'resistances');
    const support = supports[0];
    const firstResistance = resistances[0];
    if (!support || !firstResistance) {
      return emptyPlan(stock, strategy, quality, '目前找不到足夠的支撐或壓力區，不自動預填交易價格；請先手動判讀。');
    }

    const secondResistance = resistances[1];
    const thirdResistance = resistances[2];
    const supportText = '支撐一：' + zoneText(support) + (supports[1] ? '；支撐二：' + zoneText(supports[1]) : '');
    const resistanceText = '壓力一：' + zoneText(firstResistance) + (secondResistance ? '；壓力二：' + zoneText(secondResistance) : '') + (thirdResistance ? '；壓力三：' + zoneText(thirdResistance) : '');
    const buffer = zone => zoneBuffer(stock, zone);
    let entryLower;
    let entryUpper;
    let stop;
    let targetOne;
    let targetTwo;
    let cancellation;

    if (isPullback) {
      entryLower = zoneLow(support);
      entryUpper = zoneHigh(support);
      stop = roundTick(entryLower - buffer(support), false);
      targetOne = roundTick(zoneLow(firstResistance) - buffer(firstResistance), false);
      targetTwo = secondResistance ? roundTick(zoneLow(secondResistance) - buffer(secondResistance), false) : 0;
      cancellation = '僅在支撐區內止跌、隔日再突破反彈 K 高點時考慮；若收盤跌破支撐區下緣，取消買進。';
    } else {
      entryLower = roundTick(zoneHigh(firstResistance) + priceTick(zoneHigh(firstResistance)), true);
      const atr = Number(property(structure, 'atr14', 'Atr14') || 0);
      entryUpper = roundTick(entryLower + Math.max(atr * 0.25, priceTick(entryLower) * 2), true);
      stop = roundTick(zoneLow(firstResistance) - buffer(firstResistance), false);
      targetOne = secondResistance ? roundTick(zoneLow(secondResistance) - buffer(secondResistance), false) : 0;
      targetTwo = thirdResistance ? roundTick(zoneLow(thirdResistance) - buffer(thirdResistance), false) : 0;
      cancellation = '只在壓力區上緣有效突破後考慮；若突破後收盤回到壓力區內，視為假突破，取消或退出計畫。';
    }

    return {
      symbol: stock.symbol,
      name: stock.name,
      strategy: isPullback ? 'pullback' : 'breakout',
      entryLower,
      entryUpper,
      stop,
      targetOne,
      targetTwo,
      riskBudget: 0,
      available: true,
      cancellation,
      quality,
      structureText: supportText + '\n' + resistanceText,
      initialStatus: '計畫已依近 120 日日 K 的結構區預填；所有欄位皆可自行調整，不會送出委託。',
      validUntil: dateText(nextWeekday()),
      savedAt: ''
    };
  }

  function ensurePlanModal() {
    if (byId('tradePlanModal')) return;
    const overlay = document.createElement('div');
    overlay.id = 'tradePlanModal';
    overlay.className = 'modal-overlay';
    overlay.style.display = 'none';
    overlay.style.zIndex = '1100';
    overlay.innerHTML = `
      <div class="modal-box" style="max-width:680px" role="dialog" aria-modal="true" aria-label="建立隔日交易計畫">
        <div class="modal-header"><div><div style="font-size:20px;font-weight:700;color:#f0f6fc">建立隔日交易計畫</div><div id="tpStockTitle" class="muted" style="margin-top:4px"></div></div><button class="modal-close" type="button" id="tpClose" aria-label="關閉">✕</button></div>
        <div class="modal-body">
          <div style="border:1px solid #ffa657;background:rgba(255,166,87,.1);border-radius:7px;padding:10px 12px;color:#f0f6fc;font-size:12px;line-height:1.55;margin-bottom:14px">只會計算、儲存與複製交易備忘；不會連接券商、建立委託或自動下單。</div>
          <div class="detail-section"><div class="detail-section-title">策略與期限</div><div class="detail-grid"><div class="detail-item"><div class="detail-item-label">策略</div><select id="tpStrategy"><option value="breakout">突破買進</option><option value="pullback">拉回買進</option></select></div><div class="detail-item"><div class="detail-item-label">有效期限</div><div class="detail-item-value" id="tpValidUntil"></div></div></div><div id="tpQuality" class="muted" style="font-size:12px;margin-top:9px;line-height:1.5"></div><div id="tpStructure" class="reason-box" style="margin-top:9px;white-space:pre-line;font-size:12px"></div></div>
          <div class="detail-section"><div class="detail-section-title">可調整價格計畫</div><div class="detail-grid"><div class="detail-item"><div class="detail-item-label">買入區下緣</div><input id="tpEntryLower" type="number" min="0" step="0.01"></div><div class="detail-item"><div class="detail-item-label">買入區上緣</div><input id="tpEntryUpper" type="number" min="0" step="0.01"></div><div class="detail-item"><div class="detail-item-label">停損價</div><input id="tpStop" type="number" min="0" step="0.01"></div><div class="detail-item"><div class="detail-item-label">單股風險</div><div class="detail-item-value" id="tpRisk"></div></div><div class="detail-item"><div class="detail-item-label">目標一（下一層壓力前）</div><input id="tpTargetOne" type="number" min="0" step="0.01"></div><div class="detail-item"><div class="detail-item-label">目標二（再下一層壓力前）</div><input id="tpTargetTwo" type="number" min="0" step="0.01"></div></div><div class="muted" style="font-size:12px;margin-top:9px">停損幅度 <strong id="tpRiskPercent"></strong>　目標一 <strong id="tpR1"></strong>　目標二 <strong id="tpR2"></strong></div><div id="tpDecision" style="font-size:12px;line-height:1.5;margin-top:8px"></div></div>
          <div class="detail-section"><div class="detail-section-title">部位與放棄條件</div><label class="detail-item-label" for="tpRiskBudget">本次最多可承受損失（元）</label><input id="tpRiskBudget" type="number" min="0" step="1" placeholder="自行填寫" style="max-width:180px;display:block;margin:5px 0 7px"><div id="tpShares" style="font-weight:600;margin-bottom:10px"></div><div class="detail-item-label">放棄買進條件</div><div id="tpCancellation" class="reason-box" style="margin-top:5px"></div></div>
          <div class="detail-section"><label class="detail-section-title" for="tpNotes">自己的備註（選填）</label><textarea id="tpNotes" rows="3" style="width:100%;box-sizing:border-box;margin-top:6px"></textarea></div>
          <div id="tpStatus" class="muted" style="font-size:12px;margin:12px 0"></div><div style="display:flex;justify-content:flex-end;gap:8px;flex-wrap:wrap"><button class="btn-csv" type="button" id="tpCopy">複製交易備忘</button><button class="btn-csv" type="button" id="tpSave">儲存交易計畫</button></div>
        </div>
      </div>`;
    overlay.addEventListener('click', event => { if (event.target === overlay) closePlan(); });
    document.body.appendChild(overlay);
    byId('tpClose').addEventListener('click', closePlan);
    byId('tpStrategy').addEventListener('change', () => populatePlan(buildDefaultPlan(activeStock, byId('tpStrategy').value)));
    ['tpEntryLower', 'tpEntryUpper', 'tpStop', 'tpTargetOne', 'tpTargetTwo', 'tpRiskBudget'].forEach(id => byId(id).addEventListener('input', updateSummary));
    byId('tpSave').addEventListener('click', savePlan);
    byId('tpCopy').addEventListener('click', copyPlan);
  }

  function currentPlan() {
    return { symbol: activeStock.symbol, name: activeStock.name, strategy: byId('tpStrategy').value, entryLower: toNumber(byId('tpEntryLower').value), entryUpper: toNumber(byId('tpEntryUpper').value), stop: toNumber(byId('tpStop').value), targetOne: toNumber(byId('tpTargetOne').value), targetTwo: toNumber(byId('tpTargetTwo').value), riskBudget: toNumber(byId('tpRiskBudget').value), cancellation: byId('tpCancellation').textContent, quality: byId('tpQuality').textContent, structureText: byId('tpStructure').textContent, validUntil: byId('tpValidUntil').textContent, notes: byId('tpNotes').value.trim(), savedAt: new Date().toISOString() };
  }

  function setPrice(id, value) { byId(id).value = value > 0 ? formatPrice(value) : ''; }

  function populatePlan(plan) {
    byId('tpStrategy').value = plan.strategy || 'breakout';
    byId('tpValidUntil').textContent = plan.validUntil || dateText(nextWeekday());
    byId('tpQuality').textContent = plan.quality || '';
    byId('tpStructure').textContent = plan.structureText || '這份已儲存的計畫未附結構快照；請自行再次確認支撐與壓力。';
    setPrice('tpEntryLower', plan.entryLower);
    setPrice('tpEntryUpper', plan.entryUpper);
    setPrice('tpStop', plan.stop);
    setPrice('tpTargetOne', plan.targetOne);
    setPrice('tpTargetTwo', plan.targetTwo);
    byId('tpRiskBudget').value = plan.riskBudget || '';
    byId('tpCancellation').textContent = plan.cancellation || '';
    byId('tpNotes').value = plan.notes || '';
    byId('tpStatus').textContent = plan.savedAt ? '已載入本瀏覽器儲存的計畫；請自行確認價格後再使用。' : (plan.initialStatus || '所有欄位皆可自行調整。');
    updateSummary();
  }

  function updateSummary() {
    const plan = currentPlan();
    const risk = plan.entryLower - plan.stop;
    const riskRate = plan.entryLower > 0 && risk > 0 ? risk / plan.entryLower : 0;
    const rewardOne = risk > 0 ? (plan.targetOne - plan.entryLower) / risk : 0;
    const rewardTwo = risk > 0 ? (plan.targetTwo - plan.entryLower) / risk : 0;
    const shares = risk > 0 && plan.riskBudget > 0 ? Math.floor(plan.riskBudget / risk) : 0;
    byId('tpRisk').textContent = risk > 0 ? formatPrice(risk) : '—';
    byId('tpRiskPercent').textContent = riskRate > 0 ? (riskRate * 100).toFixed(2) + '%' : '—';
    byId('tpR1').textContent = risk > 0 ? rewardOne.toFixed(2) + 'R' : '—';
    byId('tpR2').textContent = risk > 0 ? rewardTwo.toFixed(2) + 'R' : '—';
    byId('tpShares').textContent = plan.riskBudget <= 0 ? '請輸入單筆最大可承受損失，再計算建議股數。' : shares > 0 ? '建議上限：' + shares.toLocaleString('zh-TW') + ' 股（約 ' + (shares / 1000).toFixed(2) + ' 張）' : '風險金額不足以買進一股。';
    let decision = '';
    let color = '#8b949e';
    if (!(plan.entryLower > 0 && plan.stop > 0 && plan.stop < plan.entryLower)) decision = '請先確認買入區與結構停損價。';
    else if (riskRate > 0.06) { decision = '不建議進場：結構停損距離超過 6%，等待更好的買點，不要硬縮停損。'; color = '#ff7b72'; }
    else if (!(plan.targetOne > plan.entryLower) || rewardOne < 1.5) { decision = '不建議進場：第一層壓力空間不足 1.5R，風報比不佳。'; color = '#ff7b72'; }
    else if (!(plan.targetTwo > plan.targetOne)) { decision = '尚未找到第二層壓力：可只將第一層作為分段停利，剩餘部位等待結構確認。'; color = '#ffa657'; }
    else { decision = '結構條件與風報比可供參考；仍請自行確認當日量價與大盤環境。'; color = '#7ee787'; }
    byId('tpDecision').textContent = decision;
    byId('tpDecision').style.color = color;
  }

  function validatePlan(plan) {
    return plan.entryLower > 0 && plan.entryUpper >= plan.entryLower && plan.stop > 0 && plan.stop < plan.entryLower && plan.targetOne > plan.entryLower && plan.targetTwo > plan.targetOne;
  }

  function savePlan() {
    const plan = currentPlan();
    if (!validatePlan(plan)) { byId('tpStatus').textContent = '請確認買入區、停損與兩個目標價的價格順序。'; return; }
    const plans = readPlans();
    plans[plan.symbol] = plan;
    writePlans(plans);
    byId('tpStatus').textContent = '交易計畫已儲存在此瀏覽器；它不會送出委託或自動下單。';
  }

  function memo(plan) {
    const risk = plan.entryLower - plan.stop;
    const shares = risk > 0 && plan.riskBudget > 0 ? Math.floor(plan.riskBudget / risk) : 0;
    return '【手動交易計畫｜非下單】\n' + plan.symbol + ' ' + plan.name + '\n策略：' + (plan.strategy === 'pullback' ? '拉回買進' : '突破買進') + '（有效至 ' + plan.validUntil + '）\n結構：' + plan.structureText + '\n可買區：' + formatPrice(plan.entryLower) + ' ～ ' + formatPrice(plan.entryUpper) + '\n停損：' + formatPrice(plan.stop) + '\n目標一：' + formatPrice(plan.targetOne) + '\n目標二：' + formatPrice(plan.targetTwo) + '\n放棄條件：' + plan.cancellation + '\n建議上限：' + (shares ? shares.toLocaleString('zh-TW') + ' 股' : '請先填入最大可承受損失') + '\n備註：' + plan.notes;
  }

  function copyPlan() {
    const plan = currentPlan();
    const text = memo(plan);
    const done = () => { byId('tpStatus').textContent = '交易計畫已複製到剪貼簿；請自行確認後再於券商端手動處理。'; };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done).catch(() => fallbackCopy(text, done));
    else fallbackCopy(text, done);
  }

  function fallbackCopy(text, done) {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy'); done(); } catch (_) { byId('tpStatus').textContent = '無法複製到剪貼簿，請手動查看計畫內容。'; }
    area.remove();
  }

  function closePlan() {
    const modal = byId('tradePlanModal');
    if (modal) modal.style.display = 'none';
  }

  function openPlan() {
    const symbol = (byId('md-symbol') || {}).textContent || '';
    const stocks = typeof rawData !== 'undefined' && Array.isArray(rawData) ? rawData : [];
    activeStock = stocks.find(item => String(item.symbol) === String(symbol));
    if (!activeStock) return;
    ensurePlanModal();
    byId('tpStockTitle').textContent = activeStock.symbol + ' ' + activeStock.name;
    populatePlan(readPlans()[activeStock.symbol] || buildDefaultPlan(activeStock, 'breakout'));
    byId('tradePlanModal').style.display = 'flex';
  }

  function addPlanButton() {
    const stockModal = byId('stockModal');
    if (!stockModal || byId('btnCreateTradePlan')) return;
    const body = stockModal.querySelector('.modal-body');
    if (!body) return;
    const section = document.createElement('div');
    section.className = 'detail-section';
    section.style.borderColor = '#388bfd';
    section.innerHTML = '<div style="display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap"><div><div class="detail-section-title" style="margin-bottom:3px">隔日交易計畫</div><div class="muted" style="font-size:12px">用支撐／壓力區做手動計算與記錄，不會自動下單。</div></div><button id="btnCreateTradePlan" class="btn-csv" type="button">建立交易計畫</button></div>';
    body.insertBefore(section, body.firstChild);
    byId('btnCreateTradePlan').addEventListener('click', openPlan);
  }

  document.addEventListener('DOMContentLoaded', addPlanButton);
  if (document.readyState !== 'loading') addPlanButton();
}());
