// Zero-build console bootstrap. It deliberately uses only browser APIs so a
// fresh checkout can `dotnet run` without npm and still see the full control
// surface: contributions, dashboard, dependency graph, runtime bindings and
// recent events. The React SPA (when built) supersedes this view.
(async function () {
  const el = id => document.getElementById(id);
  const graphState = { view: 'declared' };

  try {
    await renderPages(el('pages'));
  } catch (err) { fail(el('pages'), err); }
  try {
    await renderDashboard(el('dashboard'));
  } catch (err) { fail(el('dashboard'), err); }
  try {
    await renderSnapshot(el('snapshot'));
  } catch (err) { fail(el('snapshot'), err); }
  try {
    await renderGraph(el('graph'), graphState);
  } catch (err) { fail(el('graph'), err); }
  try {
    await renderEvents(el('events'));
  } catch (err) { fail(el('events'), err); }

  async function query(name) {
    const response = await fetch('/api/query/' + name);
    if (!response.ok) throw new Error(name + ': HTTP ' + response.status);
    const envelope = await response.json();
    return envelope.data || {};
  }

  function fail(container, err) {
    container.textContent = 'Failed to load: ' + err;
  }

  function node(tag, text, className) {
    const item = document.createElement(tag);
    if (text !== undefined && text !== null) item.textContent = String(text);
    if (className) item.className = className;
    return item;
  }

  function card(container, title, ...children) {
    container.replaceChildren(node('h2', title), ...children);
  }

  function table(headers, rows) {
    const result = document.createElement('table');
    const head = document.createElement('tr');
    headers.forEach(title => head.appendChild(node('th', title)));
    result.appendChild(head);
    rows.forEach(row => {
      const line = document.createElement('tr');
      row.forEach(value => line.appendChild(node('td', value === undefined || value === null ? '—' : value)));
      result.appendChild(line);
    });
    return result;
  }

  async function renderPages(container) {
    const response = await fetch('/api/ui/pages');
    const contributed = (await response.json()).data || [];
    const list = contributed.length ? node('ul', undefined, 'items') : node('p', 'No UI contributions');
    contributed.forEach(page => list.appendChild(
      node('li', (page.title || page.name || page.id) + ' · ' + (page.route || ''))));
    card(container, 'Contributed pages', list);
  }

  async function renderDashboard(container) {
    const data = await query('framework/dashboard');
    const counts = data.counts || {};
    card(container, 'Dashboard', table(['Fibers', 'Active', 'Pending', 'Failed', 'Providers', 'Dependencies', 'Effects'], [[
      counts.fibers, counts.active, counts.pending, counts.failed,
      counts.providers, counts.dependencies, counts.effects,
    ]]));
  }

  async function renderSnapshot(container) {
    const fibers = (await query('runtime/snapshot')).fibers || [];
    card(container, 'Runtime snapshot', table(['Name', 'State', 'Generation', 'Requires', 'Provides'],
      fibers.map(fiber => [
        fiber.name, fiber.state, fiber.generation,
        (fiber.requires || []).join(', '), (fiber.provides || []).join(', '),
      ])));
  }

  async function renderGraph(container, state) {
    const data = await query('insight/graph');
    const view = state.view === 'runtime' ? 'runtime' : 'declared';
    const source = data[view] || { nodes: [], edges: [] };
    const rawNodes = source.nodes || [];
    const ids = rawNodes.map(item => String(item.fiberID ?? item.fiberId ?? item.id));
    const known = new Set(ids);
    const extra = new Set();
    (source.edges || []).forEach(edge => {
      [edge.from, edge.to, edge.consumerFiberID, edge.consumerFiberId,
        edge.providerFiberID, edge.providerFiberId].forEach(id => {
        if (id !== undefined && id !== null && !known.has(String(id))) extra.add(String(id));
      });
    });
    const labels = new Map();
    rawNodes.forEach(item => labels.set(
      String(item.fiberID ?? item.fiberId ?? item.id),
      item.name || String(item.fiberID ?? item.fiberId ?? item.id)));
    const allNodes = ids.concat([...extra]);
    const positions = new Map();
    const columns = Math.max(2, Math.min(4, Math.ceil(Math.sqrt(allNodes.length))));
    allNodes.forEach((id, index) => {
      positions.set(id, {
        x: 30 + (index % columns) * 230,
        y: 50 + Math.floor(index / columns) * 96,
      });
    });

    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', `0 0 ${30 + columns * 230} ${80 + Math.ceil(allNodes.length / columns) * 96}`);
    svg.setAttribute('class', 'graph');
    const ns = 'http://www.w3.org/2000/svg';
    const make = (tag, attrs, text) => {
      const item = document.createElementNS(ns, tag);
      Object.entries(attrs).forEach(([key, value]) => item.setAttribute(key, String(value)));
      if (text !== undefined) item.textContent = String(text);
      return item;
    };

    (source.edges || []).forEach(edge => {
      const from = String(edge.from ?? edge.consumerFiberID ?? edge.consumerFiberId ?? '');
      const to = String(edge.to ?? edge.providerFiberID ?? edge.providerFiberId ?? '');
      const a = positions.get(from);
      const b = positions.get(to);
      if (!a || !b) return;
      svg.appendChild(make('line', { x1: a.x + 95, y1: a.y + 18, x2: b.x + 95, y2: b.y + 18,
        stroke: edge.status === 'Satisfied' ? '#2da44e' : '#8c959f', 'stroke-width': 1.5 }));
      svg.appendChild(make('text', { x: (a.x + b.x) / 2 + 95, y: (a.y + b.y) / 2 + 12,
        fill: '#57606a', 'font-size': 10 }, edge.key || 'dependency'));
    });

    allNodes.forEach(id => {
      const p = positions.get(id);
      svg.appendChild(make('rect', { x: p.x, y: p.y, width: 190, height: 36, rx: 6,
        fill: labels.has(id) ? '#f6f8fa' : '#fff8c5', stroke: '#d0d7de' }));
      svg.appendChild(make('text', { x: p.x + 8, y: p.y + 23, 'font-size': 12 },
        labels.get(id) || id));
    });

    const toggle = node('button', view === 'declared' ? 'Show runtime graph' : 'Show declared graph');
    toggle.type = 'button';
    toggle.onclick = async () => {
      state.view = state.view === 'declared' ? 'runtime' : 'declared';
      await renderGraph(container, state).catch(err => fail(container, err));
    };
    card(container, 'Dependency graph (' + view + ')', toggle, svg);
  }

  async function renderEvents(container) {
    const events = (await query('runtime/events')).events || [];
    card(container, 'Recent events', table(['Sequence', 'Type', 'Fiber'], events.slice(-8).reverse().map(item => [
      item.sequence, item.type, item.fiberID,
    ])));
  }
})();
