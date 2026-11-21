/*
 * Embedded console bootstrap.
 *
 * Deliberately dependency-free: plain DOM APIs, no framework, no CDN. Every
 * dynamic value is written with textContent only, so component names,
 * capability keys and error strings coming from the runtime cannot inject
 * markup. Each query is loaded independently: if a query is not
 * registered yet, that section says so and the rest of the page still works.
 */
(function () {
  'use strict';

  var ENDPOINTS = {
    pages: '/api/ui/pages',
    snapshot: '/api/query/runtime/snapshot',
    dashboard: '/api/query/framework/dashboard'
  };

  function el(tag, text) {
    var node = document.createElement(tag);
    if (text !== undefined && text !== null) node.textContent = String(text);
    return node;
  }

  // Case-insensitive field lookup: the query DTOs may use Go field names
  // (Fibers) or lower-camel JSON (fibers). Accept both.
  function pick(obj, names) {
    if (!obj || typeof obj !== 'object') return undefined;
    var keys = Object.keys(obj);
    for (var i = 0; i < names.length; i++) {
      for (var j = 0; j < keys.length; j++) {
        if (keys[j].toLowerCase() === names[i].toLowerCase()) return obj[keys[j]];
      }
    }
    return undefined;
  }

  function list(value) {
    return Array.isArray(value) ? value : [];
  }

  function text(value) {
    if (value === undefined || value === null) return '';
    if (typeof value === 'object') {
      try { return JSON.stringify(value); } catch (e) { return String(value); }
    }
    return String(value);
  }

  function status(node, message, unavailable) {
    node.textContent = message;
    node.className = unavailable ? 'status warn' : 'status';
  }

  // Unwrap the shared {data: ...} envelope and surface {code, message} errors.
  function getJSON(url) {
    return fetch(url, { headers: { 'Accept': 'application/json' } })
      .then(function (res) {
        if (!res.ok) throw new Error('HTTP ' + res.status);
        return res.json();
      })
      .then(function (body) {
        if (body && Object.prototype.hasOwnProperty.call(body, 'data')) return body.data;
        if (body && body.code && body.message) throw new Error(text(body.message));
        return body;
      });
  }

  function table(caption, columns, rows) {
    var wrap = el('div', null);
    wrap.className = 'table-wrap';
    var tbl = el('table');
    tbl.appendChild(el('caption', caption));
    var head = el('thead');
    var hrow = el('tr');
    columns.forEach(function (c) { hrow.appendChild(el('th', c.label)); });
    head.appendChild(hrow);
    tbl.appendChild(head);
    var body = el('tbody');
    rows.forEach(function (row) {
      var tr = el('tr');
      columns.forEach(function (c) { tr.appendChild(el('td', text(pick(row, c.keys)))); });
      body.appendChild(tr);
    });
    tbl.appendChild(body);
    wrap.appendChild(tbl);
    return wrap;
  }

  var COLUMNS = {
    fibers: [
      { label: 'ID', keys: ['ID', 'id'] },
      { label: 'Name', keys: ['Name', 'name'] },
      { label: 'State', keys: ['State', 'state'] },
      { label: 'Intent', keys: ['Intent', 'intent'] },
      { label: 'Activation', keys: ['ActivationID', 'activationID', 'activationId'] },
      { label: 'Scope', keys: ['ScopeID', 'scopeID', 'scopeId'] },
      { label: 'Error', keys: ['Err', 'err', 'error'] }
    ],
    providers: [
      { label: 'Key', keys: ['Key', 'key'] },
      { label: 'State', keys: ['State', 'state'] },
      { label: 'Owner fiber', keys: ['OwnerFiberID', 'ownerFiberID', 'ownerFiberId'] },
      { label: 'Owner activation', keys: ['OwnerActivationID', 'ownerActivationID', 'ownerActivationId'] },
      { label: 'Scope', keys: ['ScopeID', 'scopeID', 'scopeId'] },
      { label: 'Retiring', keys: ['Retiring', 'retiring'] }
    ],
    dependencies: [
      { label: 'Key', keys: ['Key', 'key'] },
      { label: 'Consumer fiber', keys: ['ConsumerFiberID', 'consumerFiberID', 'consumerFiberId'] },
      { label: 'Provider fiber', keys: ['ProviderFiberID', 'providerFiberID', 'providerFiberId'] },
      { label: 'Status', keys: ['Status', 'status'] },
      { label: 'Reason', keys: ['Reason', 'reason'] }
    ],
    effects: [
      { label: 'Owner fiber', keys: ['OwnerFiberID', 'ownerFiberID', 'ownerFiberId'] },
      { label: 'Activation', keys: ['ActivationID', 'activationID', 'activationId'] },
      { label: 'Seq', keys: ['Seq', 'seq'] },
      { label: 'Kind', keys: ['Kind', 'kind'] },
      { label: 'Key', keys: ['Key', 'key'] },
      { label: 'State', keys: ['State', 'state'] }
    ]
  };

  function get(name) {
    return document.getElementById(name);
  }

  function clear(node) {
    while (node.firstChild) node.removeChild(node.firstChild);
  }

  function loadPages() {
    var out = get('pages-list');
    var note = get('pages-status');
    return getJSON(ENDPOINTS.pages).then(function (data) {
      var pages = list(data);
      if (pages.length === 0 && data && typeof data === 'object') {
        pages = list(pick(data, ['pages', 'Pages']));
      }
      clear(out);
      if (pages.length === 0) {
        status(note, 'No UI pages are declared yet.', false);
        return;
      }
      status(note, pages.length + ' page(s) declared:', false);
      pages.forEach(function (page) {
        var li = el('li');
        var link = el('a', text(pick(page, ['Title', 'title', 'ID', 'id'])) || '(untitled)');
        var route = text(pick(page, ['Route', 'route']));
        // Declared routes must be local paths; refuse javascript:/protocol-
        // relative values even if a malformed declaration slips through.
        var localRoute = route.indexOf('/') === 0 && route.indexOf('//') !== 0 &&
          route.indexOf('\\') === -1 && !/[\u0000-\u001f\u007f]/.test(route);
        link.href = localRoute ? route : '#';
        li.appendChild(link);
        var id = text(pick(page, ['ID', 'id']));
        if (id) li.appendChild(el('span', ' · ' + id));
        out.appendChild(li);
      });
    }).catch(function (err) {
      status(note, 'Pages unavailable: ' + err.message, true);
    });
  }

  function loadSnapshot() {
    var note = get('snapshot-status');
    var counts = get('snapshot-counts');
    var tables = get('snapshot-tables');
    return getJSON(ENDPOINTS.snapshot).then(function (data) {
      var sections = [
        { key: 'fibers', names: ['Fibers', 'fibers'], label: 'Fibers' },
        { key: 'providers', names: ['Providers', 'providers'], label: 'Providers' },
        { key: 'dependencies', names: ['Dependencies', 'dependencies', 'Deps', 'deps'], label: 'Dependencies' },
        { key: 'effects', names: ['Effects', 'effects'], label: 'Effects' }
      ];
      var rows = sections.map(function (s) {
        return { label: s.label, count: list(pick(data, s.names)).length };
      });
      clear(counts);
      clear(tables);
      counts.appendChild(table('Counts', [
        { label: 'Section', keys: ['label'] },
        { label: 'Count', keys: ['count'] }
      ], rows));
      sections.forEach(function (s) {
        var items = list(pick(data, s.names));
        tables.appendChild(table(s.label + ' (' + items.length + ')', COLUMNS[s.key], items));
      });
      status(note, 'Snapshot loaded.', false);
    }).catch(function (err) {
      clear(counts);
      clear(tables);
      status(note, 'Snapshot unavailable: ' + err.message + ' (is the query registered?)', true);
    });
  }

  function loadDashboard() {
    var note = get('dashboard-status');
    var content = get('dashboard-content');
    return getJSON(ENDPOINTS.dashboard).then(function (data) {
      clear(content);
      var nested = pick(data, ['Counts', 'counts', 'counters', 'Counters']);
      var src = (nested && typeof nested === 'object' && !Array.isArray(nested)) ? nested : data;
      var entries = [];
      if (src && typeof src === 'object' && !Array.isArray(src)) {
        Object.keys(src).forEach(function (k) {
          if (src[k] === null || typeof src[k] !== 'object') {
            entries.push({ name: k, value: text(src[k]) });
          }
        });
      }
      if (entries.length > 0) {
        content.appendChild(table('Framework counts', [
          { label: 'Metric', keys: ['name'] },
          { label: 'Value', keys: ['value'] }
        ], entries));
      } else {
        var pre = el('pre', text(data));
        content.appendChild(pre);
      }
      status(note, 'Dashboard loaded.', false);
    }).catch(function (err) {
      clear(content);
      status(note, 'Dashboard unavailable: ' + err.message + ' (is the query registered?)', true);
    });
  }

  function loadAll() {
    loadPages();
    loadSnapshot();
    loadDashboard();
  }

  get('refresh').addEventListener('click', loadAll);
  loadAll();
}());
