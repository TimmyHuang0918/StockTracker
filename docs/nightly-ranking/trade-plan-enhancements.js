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

  function priceTick(price) {
    if (price < 10) return 0.01;
    if (price < 50) return 0.05;
    if (price < 100) return 0.1;
    if (price < 500) return 0.5;
    if (price < 1000) return 1;
    return 5;
  }

  function roundTick(price, up) {
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

  function buildDefaultPlan(stock, strategy) {
    const price = Number(stock.price || 0);
    const isPullback = strategy === 'pullback';
    const entryLower = isPullback
      ? roundTick(price * 0.99, false)
      : roundTick(price * 1.001, true);
    const entryUpper = isPullback
      ? roundTick(price * 0.997, true)
      : roundTick(entryLower * 1.008, true);
    const stop = roundTick(entryLower * 0.94, false);
    const risk = Math.max(0, entryLower - stop);
    const leadership = stock.leadershipLabel || '資料不足';
    const cancellation = isPullback
      ? '跌破停損價時取消買進；若價格直接反彈高於買入區上緣，等待下一次拉回，不追價。'
      : '僅在突破買入區時考慮；若開盤或盤中直接高於買入區上緣，或回落跌破前一交易日低點，取消買進、不追價。';

    return {
      symbol: stock.symbol,
      name: stock.name,
      strategy: isPullback ? 'pullback' : 'breakout',
      entryLower,
      entryUpper,
      stop,
      targetOne: roundTick(entryLower + risk * 1.5, true),
      targetTwo: roundTick(entryLower + risk * 2.5, true),
      riskBudget: 0,
      cancellation,
      quality: '分數 ' + stock.score + '／風險 ' + stock.crash + '；' + leadership + '，外資敏感度 ' + (stock.foreignSensitivity || 0) + '／投信敏感度 ' + (stock.trustSensitivity || 0),
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
        <div class="modal-header">
          <div><div style="font-size:20px;font-weight:700;color:#f0f6fc">建立隔日交易計畫</div><div id="tpStockTitle" class="muted" style="margin-top:4px"></div></div>
          <button class="modal-close" type="button" id="tpClose" aria-label="關閉">✕</button>
        </div>
        <div class="modal-body">
          <div style="border:1px solid #ffa657;background:rgba(255,166,87,.1);border-radius:7px;padding:10px 12px;color:#f0f6fc;font-size:12px;line-height:1.55;margin-bottom:14px">只會計算、儲存與複製交易備忘；不會連接券商、建立委託或自動下單。</div>
          <div class="detail-section">
            <div class="detail-section-title">策略與期限</div>
            <div class="detail-grid">
              <div class="detail-item"><div class="detail-item-label">策略</div><select id="tpStrategy"><option value="breakout">突破買進</option><option value="pullback">拉回買進</option></select></div>
              <div class="detail-item"><div class="detail-item-label">有效期限</div><div class="detail-item-value" id="tpValidUntil"></div></div>
            </div>
            <div id="tpQuality" class="muted" style="font-size:12px;margin-top:9px;line-height:1.5"></div>
          </div>
          <div class="detail-section">
            <div class="detail-section-title">可調整價格計畫</div>
            <div class="detail-grid">
              <div class="detail-item"><div class="detail-item-label">買入區下緣</div><input id="tpEntryLower" type="number" min="0" step="0.01"></div>
              <div class="detail-item"><div class="detail-item-label">買入區上緣</div><input id="tpEntryUpper" type="number" min="0" step="0.01"></div>
              <div class="detail-item"><div class="detail-item-label">停損價</div><input id="tpStop" type="number" min="0" step="0.01"></div>
              <div class="detail-item"><div class="detail-item-label">單股風險</div><div class="detail-item-value" id="tpRisk"></div></div>
              <div class="detail-item"><div class="detail-item-label">目標一（分段停利）</div><input id="tpTargetOne" type="number" min="0" step="0.01"></div>
              <div class="detail-item"><div class="detail-item-label">目標二（追蹤停利）</div><input id="tpTargetTwo" type="number" min="0" step="0.01"></div>
            </div>
            <div class="muted" style="font-size:12px;margin-top:9px">停損幅度 <strong id="tpRiskPercent"></strong>　目標一 <strong id="tpR1"></strong>　目標二 <strong id="tpR2"></strong></div>
          </div>
          <div class="detail-section">
            <div class="detail-section-title">部位與放棄條件</div>
            <label class="detail-item-label" for="tpRiskBudget">本次最多可承受損失（元）</label>
            <input id="tpRiskBudget" type="number" min="0" step="1" placeholder="自行填寫" style="max-width:180px;display:block;margin:5px 0 7px">
            <div id="tpShares" style="font-weight:600;margin-bottom:10px"></div>
            <div class="detail-item-label">放棄買進條件</div><div id="tpCancellation" class="reason-box" style="margin-top:5px"></div>
          </div>
          <div class="detail-section">
            <label class="detail-section-title" for="tpNotes">自己的備註（選填）</label>
            <textarea id="tpNotes" rows="3" style="width:100%;box-sizing:border-box;margin-top:6px"></textarea>
          </div>
          <div id="tpStatus" class="muted" style="font-size:12px;margin:12px 0"></div>
          <div style="display:flex;justify-content:flex-end;gap:8px;flex-wrap:wrap"><button class="btn-csv" type="button" id="tpCopy">複製交易備忘</button><button class="btn-csv" type="button" id="tpSave">儲存交易計畫</button></div>
        </div>
      </div>`;
    overlay.addEventListener('click', event => { if (event.target === overlay) closePlan(); });
    document.body.appendChild(overlay);
    byId('tpClose').addEventListener('click', closePlan);
    byId('tpStrategy').addEventListener('change', () => populatePlan(buildDefaultPlan(activeStock, byId('tpStrategy').value)));
    [byId('tpEntryLower'), byId('tpStop')].forEach(input => input.addEventListener('input', updateDerivedTargets));
    byId('tpRiskBudget').addEventListener('input', updateSummary);
    byId('tpSave').addEventListener('click', savePlan);
    byId('tpCopy').addEventListener('click', copyPlan);
  }

  function currentPlan() {
    return {
      symbol: activeStock.symbol,
      name: activeStock.name,
      strategy: byId('tpStrategy').value,
      entryLower: toNumber(byId('tpEntryLower').value),
      entryUpper: toNumber(byId('tpEntryUpper').value),
      stop: toNumber(byId('tpStop').value),
      targetOne: toNumber(byId('tpTargetOne').value),
      targetTwo: toNumber(byId('tpTargetTwo').value),
      riskBudget: toNumber(byId('tpRiskBudget').value),
      cancellation: byId('tpCancellation').textContent,
      quality: byId('tpQuality').textContent,
      validUntil: byId('tpValidUntil').textContent,
      notes: byId('tpNotes').value.trim(),
      savedAt: new Date().toISOString()
    };
  }

  function populatePlan(plan) {
    byId('tpStrategy').value = plan.strategy || 'breakout';
    byId('tpValidUntil').textContent = plan.validUntil || dateText(nextWeekday());
    byId('tpQuality').textContent = plan.quality || '';
    byId('tpEntryLower').value = formatPrice(plan.entryLower);
    byId('tpEntryUpper').value = formatPrice(plan.entryUpper);
    byId('tpStop').value = formatPrice(plan.stop);
    byId('tpTargetOne').value = formatPrice(plan.targetOne);
    byId('tpTargetTwo').value = formatPrice(plan.targetTwo);
    byId('tpRiskBudget').value = plan.riskBudget || '';
    byId('tpCancellation').textContent = plan.cancellation || '';
    byId('tpNotes').value = plan.notes || '';
    byId('tpStatus').textContent = plan.savedAt ? '已載入本瀏覽器儲存的計畫；請自行確認價格後再使用。' : '已依目前掃描快照預填；所有欄位皆可自行調整。';
    updateSummary();
  }

  function updateDerivedTargets() {
    const entry = toNumber(byId('tpEntryLower').value);
    const stop = toNumber(byId('tpStop').value);
    const risk = entry - stop;
    if (risk > 0) {
      byId('tpTargetOne').value = formatPrice(roundTick(entry + risk * 1.5, true));
      byId('tpTargetTwo').value = formatPrice(roundTick(entry + risk * 2.5, true));
    }
    updateSummary();
  }

  function updateSummary() {
    const plan = currentPlan();
    const risk = plan.entryLower - plan.stop;
    const riskPercent = plan.entryLower > 0 && risk > 0 ? (risk / plan.entryLower * 100).toFixed(2) + '%' : '—';
    const r1 = risk > 0 ? ((plan.targetOne - plan.entryLower) / risk).toFixed(2) + 'R' : '—';
    const r2 = risk > 0 ? ((plan.targetTwo - plan.entryLower) / risk).toFixed(2) + 'R' : '—';
    const shares = risk > 0 && plan.riskBudget > 0 ? Math.floor(plan.riskBudget / risk) : 0;
    byId('tpRisk').textContent = risk > 0 ? formatPrice(risk) : '—';
    byId('tpRiskPercent').textContent = riskPercent;
    byId('tpR1').textContent = r1;
    byId('tpR2').textContent = r2;
    byId('tpShares').textContent = plan.riskBudget <= 0
      ? '請輸入單筆最大可承受損失，再計算建議股數。'
      : shares > 0 ? '建議上限：' + shares.toLocaleString('zh-TW') + ' 股（約 ' + (shares / 1000).toFixed(2) + ' 張）' : '風險金額不足以買進一股。';
  }

  function validatePlan(plan) {
    return plan.entryLower > 0 && plan.entryUpper >= plan.entryLower && plan.stop > 0 && plan.stop < plan.entryLower && plan.targetOne > plan.entryLower && plan.targetTwo > plan.targetOne;
  }

  function savePlan() {
    const plan = currentPlan();
    if (!validatePlan(plan)) {
      byId('tpStatus').textContent = '請確認買入區、停損與兩個目標價的價格順序。';
      return;
    }
    const plans = readPlans();
    plans[plan.symbol] = plan;
    writePlans(plans);
    byId('tpStatus').textContent = '交易計畫已儲存在此瀏覽器；它不會送出委託或自動下單。';
  }

  function memo(plan) {
    const risk = plan.entryLower - plan.stop;
    const shares = risk > 0 && plan.riskBudget > 0 ? Math.floor(plan.riskBudget / risk) : 0;
    return '【手動交易計畫｜非下單】\n' + plan.symbol + ' ' + plan.name + '\n策略：' + (plan.strategy === 'pullback' ? '拉回買進' : '突破買進') + '（有效至 ' + plan.validUntil + '）\n可買區：' + formatPrice(plan.entryLower) + ' ～ ' + formatPrice(plan.entryUpper) + '\n停損：' + formatPrice(plan.stop) + '\n目標一：' + formatPrice(plan.targetOne) + '\n目標二：' + formatPrice(plan.targetTwo) + '\n放棄條件：' + plan.cancellation + '\n建議上限：' + (shares ? shares.toLocaleString('zh-TW') + ' 股' : '請先填入最大可承受損失') + '\n備註：' + plan.notes;
  }

  function copyPlan() {
    const plan = currentPlan();
    const text = memo(plan);
    const done = () => { byId('tpStatus').textContent = '交易計畫已複製到剪貼簿；請自行確認後再於券商端手動處理。'; };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done).catch(() => fallbackCopy(text, done));
    } else {
      fallbackCopy(text, done);
    }
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
    const saved = readPlans()[activeStock.symbol];
    populatePlan(saved || buildDefaultPlan(activeStock, 'breakout'));
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
    section.innerHTML = '<div style="display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap"><div><div class="detail-section-title" style="margin-bottom:3px">隔日交易計畫</div><div class="muted" style="font-size:12px">僅做手動計算與記錄，不會自動下單。</div></div><button id="btnCreateTradePlan" class="btn-csv" type="button">建立交易計畫</button></div>';
    body.insertBefore(section, body.firstChild);
    byId('btnCreateTradePlan').addEventListener('click', openPlan);
  }

  document.addEventListener('DOMContentLoaded', addPlanButton);
  if (document.readyState !== 'loading') addPlanButton();
}());
