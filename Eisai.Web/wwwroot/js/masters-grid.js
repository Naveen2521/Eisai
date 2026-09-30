const columns = [
  { key: "name", read: row => row.MasterName },
  { key: "prefix", read: row => row.UniqueIdPrefix },
  { key: "table", read: row => row.TableName },
  { key: "heading", read: row => row.MenuHeading || "" },
  { key: "sub", read: row => row.MenuSubHeading || "" },
  { key: "parent", read: row => row.Parent?.MasterName || "" }
];

const state = { rows: [], page: 1, sortKey: "name", sortDir: 1 };

const body = document.querySelector("#master-grid");
const search = document.querySelector("#master-search");
const pageSize = document.querySelector("#page-size");
const pageLabel = document.querySelector("#page-label");

search.addEventListener("input", () => { state.page = 1; render(); });
pageSize.addEventListener("change", () => { state.page = 1; render(); });
document.querySelectorAll("[data-filter]").forEach(input => {
  input.addEventListener("input", () => { state.page = 1; render(); });
  input.addEventListener("change", () => { state.page = 1; render(); });
});
document.querySelectorAll("[data-sort]").forEach(button => {
  button.addEventListener("click", () => {
    const key = button.dataset.sort;
    state.sortDir = state.sortKey === key ? -state.sortDir : 1;
    state.sortKey = key;
    render();
  });
});
document.querySelector("#page-prev").addEventListener("click", () => { state.page -= 1; render(); });
document.querySelector("#page-next").addEventListener("click", () => { state.page += 1; render(); });
const exportMenu = document.querySelector("#export-menu");
const exportBox = document.querySelector(".export-box");
document.querySelector("#export-toggle").addEventListener("click", () => {
  exportMenu.hidden = !exportMenu.hidden;
});
document.addEventListener("click", event => {
  if (!exportBox.contains(event.target)) {
    exportMenu.hidden = true;
  }
});
document.querySelectorAll("[data-export]").forEach(button => {
  button.addEventListener("click", () => {
    exportMenu.hidden = true;
    const rows = filtered();
    const task = button.dataset.export === "excel" ? downloadExcel(rows) : downloadPdf(rows);
    Promise.resolve(task).catch(error => dar.fail(error.message || "Could not export."));
  });
});

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[char]));
}

function cell(row, key) {
  return columns.find(column => column.key === key).read(row);
}

function filtered() {
  const common = search.value.trim().toLowerCase();
  const filters = {};
  document.querySelectorAll("[data-filter]").forEach(input => {
    filters[input.dataset.filter] = input.value.trim().toLowerCase();
  });
  const rows = state.rows.filter(row => {
    const values = columns.map(column => String(column.read(row) ?? "").toLowerCase());
    if (common && !values.some(value => value.includes(common))) {
      return false;
    }
    return columns.every(column => {
      const filter = filters[column.key];
      if (!filter) {
        return true;
      }
      const value = String(column.read(row) ?? "").toLowerCase();
      return column.key === "heading" || column.key === "sub" || column.key === "parent"
        ? value === filter
        : value.includes(filter);
    });
  });
  return rows.sort((left, right) => {
    const a = String(cell(left, state.sortKey) ?? "").toLowerCase();
    const b = String(cell(right, state.sortKey) ?? "").toLowerCase();
    return a.localeCompare(b) * state.sortDir;
  });
}

function tone(name) {
  const total = [...name].reduce((sum, char) => sum + char.charCodeAt(0), 0);
  return ["tone-a", "tone-b", "tone-c"][total % 3];
}

function masterIcon(name) {
  const icons = [
    `<svg viewBox="0 0 24 24"><path d="M4 20V9l8-5 8 5v11H4z"/><path d="M10 20v-6h4v6"/></svg>`,
    `<svg viewBox="0 0 24 24"><path d="M5 4v16"/><path d="M5 5h10l-2 4 2 4H5"/></svg>`,
    `<svg viewBox="0 0 24 24"><circle cx="8" cy="9" r="2.2"/><circle cx="16" cy="9" r="2.2"/><path d="M4.5 18c.6-2.4 2.2-3.5 3.5-3.5S11 15.6 11.6 18"/><path d="M12.4 18c.6-2.4 2.2-3.5 3.5-3.5s2.9 1.1 3.6 3.5"/></svg>`
  ];
  const total = [...name].reduce((sum, char) => sum + char.charCodeAt(0), 0);
  return icons[total % icons.length];
}

function render() {
  const rows = filtered();
  const size = pageSize.value === "all" ? Math.max(rows.length, 1) : Number(pageSize.value);
  const pages = Math.max(1, Math.ceil(rows.length / size) || 1);
  state.page = Math.min(Math.max(1, state.page), pages);
  const start = (state.page - 1) * size;
  const pageRows = rows.slice(start, start + size);
  body.innerHTML = pageRows.length
    ? pageRows.map((row, index) => {
        const name = cell(row, "name");
        const parent = cell(row, "parent");
        return `<tr>
          <td class="col-no">${start + index + 1}</td>
          <td><a class="master-name" href="/Masters/Records?master=${encodeURIComponent(name)}"><span class="master-icon ${tone(name)}">${masterIcon(name)}</span>${escapeHtml(name)}</a></td>
          <td><span class="pill prefix">${escapeHtml(cell(row, "prefix"))}</span></td>
          <td>${escapeHtml(cell(row, "table"))}</td>
          <td>${cell(row, "heading") ? `<span class="pill heading">${escapeHtml(cell(row, "heading"))}</span>` : "-"}</td>
          <td>${cell(row, "sub") ? `<span class="pill sub">${escapeHtml(cell(row, "sub"))}</span>` : "-"}</td>
          <td>${parent ? escapeHtml(parent) : "-"}</td>
          <td><a class="edit-link" href="/Masters/Create?master=${encodeURIComponent(name)}">Edit</a></td>
        </tr>`;
      }).join("")
    : `<tr><td colspan="8" class="muted empty-note">No masters match this search.</td></tr>`;
  const from = rows.length ? start + 1 : 0;
  const to = Math.min(start + size, rows.length);
  pageLabel.textContent = rows.length ? `Showing ${from} to ${to} of ${rows.length} masters` : "Showing 0 masters";
  document.querySelector("#page-current").textContent = String(state.page);
  document.querySelector("#page-prev").disabled = state.page <= 1;
  document.querySelector("#page-next").disabled = state.page >= pages || rows.length === 0;
  document.querySelectorAll("[data-sort]").forEach(button => {
    button.classList.toggle("sorted", button.dataset.sort === state.sortKey);
    button.dataset.dir = button.dataset.sort === state.sortKey && state.sortDir < 0 ? "desc" : "asc";
  });
}

function fillSelects() {
  ["heading", "sub", "parent"].forEach(key => {
    const select = document.querySelector(`select[data-filter="${key}"]`);
    const current = select.value;
    const values = [...new Set(state.rows.map(row => cell(row, key)).filter(Boolean))].sort((a, b) => a.localeCompare(b));
    select.innerHTML = `<option value="">Search...</option>${values.map(value => `<option value="${escapeHtml(value)}">${escapeHtml(value)}</option>`).join("")}`;
    if (values.includes(current)) {
      select.value = current;
    }
  });
}

function rowValue(row, key) {
  return String(cell(row, key) ?? "");
}

function exportPayload(rows) {
  return {
    title: "Masters",
    headers: ["Master", "Prefix", "Table", "Heading", "Sub heading", "Parent"],
    rows: rows.map(row => columns.map(column => rowValue(row, column.key) || "-"))
  };
}

function downloadExcel(rows) {
  return darExport.downloadExcel({ ...exportPayload(rows), fileName: "masters.xlsx" });
}

function downloadPdf(rows) {
  return darExport.downloadPdf({ ...exportPayload(rows), fileName: "masters.pdf" });
}

dar.api("").then(rows => {
  state.rows = (rows ?? []).filter(row => row.TableExists !== false);
  fillSelects();
  render();
}).catch(error => dar.fail(error.message));
