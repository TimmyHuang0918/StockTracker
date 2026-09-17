const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const file = path.resolve(__dirname, '../docs/nightly-ranking/index.html');
const html = fs.readFileSync(file, 'utf8');
const scripts = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)];
for (const [index, match] of scripts.entries()) {
  if (!/\bsrc=/.test(match[1])) new vm.Script(match[2], { filename: 'inline-' + index + '.js' });
}
const overview = html.match(/<section class='panel market-overview'[\s\S]*?<\/section><script>([\s\S]*?)<\/script>/);
assert.ok(overview, 'overview section and interaction script');
const codes = [...overview[0].matchAll(/data-market-tab='([^']+)'/g)].map(m => m[1]);
assert.deepEqual(codes, ['TWSE', 'TPEX']);
const buttons = codes.map(code => ({
  dataset: { marketTab: code }, attributes: {}, handlers: {},
  setAttribute(key, value) { this.attributes[key] = value; },
  addEventListener(type, handler) { this.handlers[type] = handler; }
}));
const tables = codes.map((code, index) => ({ dataset: { marketTable: code }, hidden: index !== 0 }));
const host = { querySelectorAll(selector) { return selector === '[data-market-tab]' ? buttons : tables; } };
vm.runInNewContext(overview[1], { document: { getElementById: id => id === 'fullMarketOverview' ? host : null } });
buttons[1].handlers.click();
assert.equal(tables[0].hidden, true);
assert.equal(tables[1].hidden, false);
assert.equal(buttons[1].attributes['aria-pressed'], 'true');
buttons[0].handlers.click();
assert.equal(tables[0].hidden, false);
assert.equal(tables[1].hidden, true);
assert.equal(buttons[1].attributes['aria-pressed'], 'false');
for (const code of codes) {
  const table = overview[0].match(new RegExp("data-market-table='" + code + "'[\\s\\S]*?<tbody>([\\s\\S]*?)</tbody>"));
  assert.ok(table, code + ' table exists');
  assert.equal((table[1].match(/<tr>/g) || []).length, 5, code + ' five individual sessions');
}
const ids = [...html.matchAll(/\bid=['"]([^'"]+)['"]/g)].map(m => m[1]);
assert.equal(ids.filter(id => id === 'fullMarketOverview').length, 1);
assert.ok(html.indexOf("id='marketGroupCards'") < html.indexOf("id='searchInput'"), 'sectors before filters');
assert.ok(!html.includes("<section class='panel breadth-card'"), 'breadth not duplicated');
assert.ok(overview[0].includes('@media(max-width:700px)'), 'narrow layout rule');
assert.ok(overview[0].includes('.market-overview{display:block'), 'new overview overrides legacy grid layout');
assert.ok(overview[0].includes('overflow-x:auto'), 'tables scroll horizontally');
assert.ok(!overview[1].includes('fetch('), 'overview does not fetch or wait at page load');
for (const match of scripts.filter(m => /\bsrc=/.test(m[1]))) {
  const source = match[1].match(/src=['"]([^'"]+)/)?.[1];
  if (source && !/^https?:/.test(source)) {
    const scriptFile = path.resolve(path.dirname(file), source.split('?')[0]);
    new vm.Script(fs.readFileSync(scriptFile, 'utf8'), { filename: scriptFile });
  }
}
console.log('PASS Web scripts, market tab switching, five-day tables, layout rules and unique overview.');
