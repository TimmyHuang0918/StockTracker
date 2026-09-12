(function () {
  if (typeof portfolio === 'undefined' || typeof renderPortfolio !== 'function') return;

  const number = value => {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  };
  const adjustments = () => (Array.isArray(portfolio.realizedAdjustments) ? portfolio.realizedAdjustments : [])
    .reduce((sum, item) => sum + number(item.amount), 0);
  const daysBetween = date => Math.max(0, Math.floor((Date.now() - new Date(date + 'T00:00:00').getTime()) / 86400000));
  const interest = lot => number(lot.principal) * number(lot.annualRate) * daysBetween(lot.openDate) / 365;
  const marginLots = symbol => portfolio.marginLots
    .filter(lot => String(lot.symbol).toUpperCase() === String(symbol).toUpperCase() && number(lot.remaining) > 0);

  function normalizePositions() {
    portfolio.holdings = Array.isArray(portfolio.holdings) ? portfolio.holdings : [];
    portfolio.marginLots = Array.isArray(portfolio.marginLots) ? portfolio.marginLots : [];
    portfolio.marginLots = portfolio.marginLots.filter(lot => lot && /^[0-9]{4,6}$/.test(String(lot.symbol || '')) && number(lot.remaining) > 0 && number(lot.principal) > 0);
    portfolio.holdings.forEach(holding => {
      if (!Object.prototype.hasOwnProperty.call(holding, 'cashShares')) {
        holding.cashShares = Math.max(0, number(holding.shares));
        holding.cashCost = Math.max(0, number(holding.cost));
      }
      holding.cashShares = Math.max(0, number(holding.cashShares));
      holding.cashCost = Math.max(0, number(holding.cashCost));
    });
    [...new Set(portfolio.marginLots.map(lot => String(lot.symbol).toUpperCase()))].forEach(symbol => {
      if (!portfolio.holdings.some(holding => String(holding.symbol).toUpperCase() === symbol)) {
        portfolio.holdings.push({ symbol, shares: 0, cost: 0, cashShares: 0, cashCost: 0 });
      }
    });
    portfolio.holdings = portfolio.holdings.filter(holding => {
      const lots = marginLots(holding.symbol);
      const marginShares = lots.reduce((sum, lot) => sum + number(lot.remaining), 0);
      const totalShares = number(holding.cashShares) + marginShares;
      if (totalShares <= 0) return false;
      const totalCost = number(holding.cashShares) * number(holding.cashCost) + lots.reduce((sum, lot) => sum + number(lot.cost) * number(lot.remaining), 0);
      holding.shares = totalShares;
      holding.cost = totalCost / totalShares;
      return true;
    });
  }

  function totals() {
    normalizePositions();
    const market = portfolio.holdings.reduce((sum, holding) => {
      const stock = findPortfolioStock(holding.symbol);
      return sum + (stock ? number(holding.shares) * stock.price : 0);
    }, 0);
    const debt = portfolio.marginLots.reduce((sum, lot) => sum + number(lot.principal), 0);
    const accruedInterest = portfolio.marginLots.reduce((sum, lot) => sum + interest(lot), 0);
    const adjustedCash = number(portfolio.cash) + adjustments();
    const marginMarketValue = portfolio.marginLots.reduce((sum, lot) => {
      const stock = findPortfolioStock(lot.symbol);
      return sum + (stock ? number(lot.remaining) * stock.price : 0);
    }, 0);
    return { market, debt, accruedInterest, adjustedCash, marginMarketValue, total: market + adjustedCash - debt - accruedInterest };
  }

  function setCash(value) {
    portfolio.cash = value;
    const input = document.getElementById('portfolioCash');
    if (input) input.value = Math.max(0, value);
  }

  function renderMarginSummary() {
    const values = totals();
    const netInvested = (Array.isArray(portfolio.cashFlows) ? portfolio.cashFlows : []).reduce((sum, item) => sum + number(item.amount), 0);
    const profit = values.total - netInvested;
    const rate = netInvested ? profit / netInvested * 100 : 0;
    const maintenance = values.debt > 0 ? values.marginMarketValue / (values.debt + values.accruedInterest) * 100 : null;
    const summary = document.getElementById('portfolioSummary');
    if (!summary) return;
    summary.innerHTML = `<div class='portfolio-stat'><span>總資產</span><strong>${values.total.toLocaleString(undefined,{maximumFractionDigits:0})}</strong></div>` +
      `<div class='portfolio-stat'><span>帳面現金</span><strong>${values.adjustedCash.toLocaleString(undefined,{maximumFractionDigits:0})}</strong></div>` +
      `<div class='portfolio-stat'><span>融資負債</span><strong>${values.debt.toLocaleString(undefined,{maximumFractionDigits:0})}</strong></div>` +
      `<div class='portfolio-stat'><span>應計利息</span><strong>${values.accruedInterest.toLocaleString(undefined,{maximumFractionDigits:0})}</strong></div>` +
      `<div class='portfolio-stat'><span>融資維持率</span><strong>${maintenance === null ? '—' : maintenance.toFixed(1) + '%'}</strong></div>` +
      `<div class='portfolio-stat'><span>累計損益</span><strong style='color:${portfolioColor(profit)}'>${profit.toLocaleString(undefined,{maximumFractionDigits:0})}</strong></div>` +
      `<div class='portfolio-stat'><span>損益率</span><strong style='color:${portfolioColor(rate)}'>${netInvested ? portfolioPercent(rate) : '--'}</strong></div>`;
    let note = document.getElementById('portfolioMarginNote');
    if (!note) {
      note = document.createElement('div');
      note.id = 'portfolioMarginNote';
      note.className = 'portfolio-muted';
      note.style.cssText = 'margin:8px 0;padding:9px;border:1px solid var(--border);border-radius:8px;background:rgba(31,111,235,.08)';
      summary.after(note);
    }
    note.textContent = `歷史損益已納入總資產；融資部位 ${portfolio.marginLots.length} 筆，負債與每日應計利息已扣除。${maintenance === null ? '' : ' 維持率以融資部位市值 ÷（本金＋應計利息）計算。'}`;
  }

  const baseRender = renderPortfolio;
  renderPortfolio = function () {
    normalizePositions();
    baseRender();
    renderMarginSummary();
    savePortfolio();
  };

  const type = document.getElementById('tradeType');
  if (!type) { renderPortfolio(); return; }
  if (![...type.options].some(option => option.value === 'marginBuy')) {
    const option = document.createElement('option');
    option.value = 'marginBuy'; option.textContent = '融資買入'; type.append(option);
  }
  const options = document.createElement('div');
  options.id = 'portfolioMarginInputs';
  options.style.cssText = 'display:grid;grid-template-columns:150px 150px 1fr;gap:8px;align-items:end;margin-top:8px';
  options.innerHTML = `<div><label class='portfolio-muted'>融資成數 %</label><input id='marginRatio' type='number' min='1' max='99' step='1' value='60'></div>` +
    `<div><label class='portfolio-muted'>年利率 %</label><input id='marginAnnualRate' type='number' min='0' step='.01' value='0'></div>` +
    `<div class='portfolio-muted'>僅「融資買入」使用。賣出會優先依最早融資批次沖銷、還本金並計入利息。</div>`;
  document.getElementById('tradeDate').parentElement.parentElement.after(options);

  document.getElementById('btnTradeAdd').addEventListener('click', event => {
    event.preventDefault(); event.stopImmediatePropagation();
    const date = document.getElementById('tradeDate').value;
    const tradeType = type.value;
    const symbol = document.getElementById('tradeSymbol').value.trim().toUpperCase();
    const quantity = number(document.getElementById('tradeQuantity').value);
    const price = number(document.getElementById('tradePrice').value);
    const fee = number(document.getElementById('tradeFee').value);
    const tax = number(document.getElementById('tradeTax').value);
    const ratio = number(document.getElementById('marginRatio').value);
    const annualRate = number(document.getElementById('marginAnnualRate').value);
    if (!date || !/^[0-9]{4,6}$/.test(symbol) || !Number.isInteger(quantity) || quantity <= 0 || price <= 0 || fee < 0 || tax < 0) {
      alert('請填寫有效交易資料。'); return;
    }
    if (tradeType === 'marginBuy' && (ratio <= 0 || ratio >= 100 || annualRate < 0)) {
      alert('融資成數請填 1 至 99，年利率請填大於或等於 0。'); return;
    }
    normalizePositions();
    let holding = portfolio.holdings.find(item => String(item.symbol).toUpperCase() === symbol);
    let realized = 0, paidPrincipal = 0, paidInterest = 0, cashImpact = 0;
    if (tradeType === 'sell') {
      if (!holding || number(holding.shares) < quantity) { alert('賣出股數不得超過目前持有股數。'); return; }
      let remaining = quantity;
      const sellCost = (fee + tax) / quantity;
      marginLots(symbol).sort((a, b) => String(a.openDate).localeCompare(String(b.openDate))).forEach(lot => {
        if (!remaining) return;
        const before = number(lot.remaining), sold = Math.min(before, remaining);
        const principal = number(lot.principal) * sold / before, accrued = interest(lot) * sold / before;
        realized += (price - number(lot.cost) - sellCost) * sold - accrued;
        lot.remaining = before - sold; lot.principal = number(lot.principal) - principal;
        paidPrincipal += principal; paidInterest += accrued; remaining -= sold;
      });
      if (remaining > 0) { realized += (price - number(holding.cashCost) - sellCost) * remaining; holding.cashShares -= remaining; }
      portfolio.marginLots = portfolio.marginLots.filter(lot => number(lot.remaining) > 0 && number(lot.principal) > 0);
      cashImpact = price * quantity - fee - tax - paidPrincipal - paidInterest;
      setCash(number(portfolio.cash) + cashImpact);
    } else {
      const gross = price * quantity + fee + tax;
      const selfFunded = tradeType === 'marginBuy' ? price * quantity * (1 - ratio / 100) + fee + tax : gross;
      if (number(portfolio.cash) + adjustments() < selfFunded) { alert('可用現金不足。'); return; }
      if (!holding) { holding = { symbol, shares: 0, cost: 0, cashShares: 0, cashCost: 0 }; portfolio.holdings.push(holding); }
      if (tradeType === 'marginBuy') {
        const principal = price * quantity * ratio / 100;
        portfolio.marginLots.push({ id: Date.now() + Math.random(), openDate: date, symbol, original: quantity, remaining: quantity, cost: gross / quantity, principal, annualRate: annualRate / 100 });
        paidPrincipal = principal;
      } else {
        holding.cashCost = (number(holding.cashCost) * number(holding.cashShares) + gross) / (number(holding.cashShares) + quantity);
        holding.cashShares = number(holding.cashShares) + quantity;
      }
      cashImpact = -selfFunded;
      setCash(number(portfolio.cash) + cashImpact);
    }
    portfolio.trades.push({ date, type: tradeType, symbol, quantity, price, fee, tax, realized, isMargin: tradeType === 'marginBuy' || paidPrincipal > 0, marginPrincipal: paidPrincipal, marginInterestPaid: paidInterest, cashImpact });
    ['tradeSymbol','tradeQuantity','tradePrice','tradeFee','tradeTax'].forEach(id => { document.getElementById(id).value = ''; });
    savePortfolio(); renderPortfolio();
  }, true);

  renderPortfolio();
})();
