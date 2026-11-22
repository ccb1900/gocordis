// Console bootstrap: pages and runtime state are discovered through the
// contribution API; the transport has no built-in authentication.
(async function () {
  const pages = document.getElementById('pages');
  const snapshot = document.getElementById('snapshot');
  try {
    const pageResponse = await fetch('/api/ui/pages');
    const contributed = await pageResponse.json();
    pages.innerHTML = contributed.length
      ? '<ul>' + contributed.map(page => '<li>' + escapeHtml(page.name) + '</li>').join('') + '</ul>'
      : '<p>No UI contributions</p>';
  } catch (err) {
    pages.textContent = 'Failed to load pages: ' + err;
  }

  try {
    const response = await fetch('/api/query/runtime/snapshot');
    const data = await response.json();
    const fibers = data.fibers || [];
    snapshot.innerHTML = '<table><tr><th>Name</th><th>State</th><th>Generation</th></tr>' +
      fibers.map(f => '<tr><td>' + escapeHtml(f.name) + '</td><td>' + escapeHtml(f.state) +
        '</td><td>' + f.generation + '</td></tr>').join('') + '</table>';
  } catch (err) {
    snapshot.textContent = 'Failed to load snapshot: ' + err;
  }

  function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }
})();
