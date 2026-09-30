const masterName = new URLSearchParams(location.search).get("master") || "";
const actorId = 1;

const state = {
  definition: null,
  parentLabels: new Map(),
  rows: [],
  editingId: 0,
  pending: [],
  pendingEdit: -1,
  page: 1,
  sortKey: "UniqueId",
  sortDir: -1,
  hiddenColumns: new Set(),
  filters: {}
};

const title = document.querySelector("#record-title");
const hint = document.querySelector("#record-hint");
const crumb = document.querySelector("#crumb-name");
const addButton = document.querySelector("#add-record");
const head = document.querySelector("#record-head");
let renderedHeadKey = "";
const body = document.querySelector("#record-grid");
const search = document.querySelector("#record-search");
const pageSize = document.querySelector("#page-size");
const pageLabel = document.querySelector("#page-label");
const formDialog = document.querySelector("#record-dialog");
const form = document.querySelector("#record-form");
const formTitle = document.querySelector("#form-title");
const formFields = document.querySelector("#form-fields");
const formError = document.querySelector("#form-error");
const pendingBox = document.querySelector("#pending-box");
const pendingLabel = document.querySelector("#pending-label");
const pendingList = document.querySelector("#pending-list");
const formMore = document.querySelector("#form-more");
const formUpdate = document.querySelector("#form-update");
const formSave = document.querySelector("#form-save");

head.addEventListener("click", event => {
  const button = event.target.closest("[data-sort]");
  if (!button) {
    return;
  }
  const key = button.dataset.sort;
  if (key === "UniqueId") {
    state.sortKey = "UniqueId";
    state.sortDir = -1;
  } else {
    state.sortDir = state.sortKey === key ? -state.sortDir : 1;
    state.sortKey = key;
  }
  render();
});
head.addEventListener("input", event => {
  const input = event.target.closest("[data-filter]");
  if (!input || input.tagName === "SELECT") {
    return;
  }
  state.filters[input.dataset.filter] = input.value;
  state.page = 1;
  render();
});
head.addEventListener("change", event => {
  const input = event.target.closest("[data-filter]");
  if (!input) {
    return;
  }
  state.filters[input.dataset.filter] = input.value;
  state.page = 1;
  render();
});
search.addEventListener("input", () => { state.page = 1; render(); });
pageSize.addEventListener("change", () => { state.page = 1; render(); });
document.querySelector("#page-prev").addEventListener("click", () => { state.page -= 1; render(); });
document.querySelector("#page-next").addEventListener("click", () => { state.page += 1; render(); });
const exportMenu = document.querySelector("#export-menu");
const exportBox = document.querySelector(".export-box");
const columnMenu = document.querySelector("#column-menu");
const columnBox = document.querySelector(".column-box");
document.querySelector("#export-toggle").addEventListener("click", () => {
  columnMenu.hidden = true;
  exportMenu.hidden = !exportMenu.hidden;
});
document.querySelector("#column-toggle").addEventListener("click", () => {
  if (!state.definition) {
    return;
  }
  exportMenu.hidden = true;
  renderColumnMenu();
  columnMenu.hidden = !columnMenu.hidden;
});
columnMenu.addEventListener("change", event => {
  const input = event.target.closest("[data-column]");
  if (!input) {
    return;
  }
  if (input.checked) {
    state.hiddenColumns.delete(input.dataset.column);
  } else {
    state.hiddenColumns.add(input.dataset.column);
  }
  if (state.hiddenColumns.has(state.sortKey)) {
    state.sortKey = state.hiddenColumns.has("UniqueId") ? "" : "UniqueId";
    state.sortDir = state.sortKey === "UniqueId" ? -1 : 1;
  }
  renderColumnMenu();
  render();
});
document.addEventListener("click", event => {
  if (!exportBox.contains(event.target)) {
    exportMenu.hidden = true;
  }
  if (!columnBox.contains(event.target)) {
    columnMenu.hidden = true;
  }
});
document.querySelectorAll("[data-export]").forEach(button => {
  button.addEventListener("click", () => {
    exportMenu.hidden = true;
    if (!state.definition) {
      return;
    }
    const rows = filtered();
    const task = button.dataset.export === "excel" ? downloadExcel(rows) : downloadPdf(rows);
    Promise.resolve(task).catch(error => dar.fail(error.message || "Could not export."));
  });
});
addButton.addEventListener("click", () => openForm(null));
form.addEventListener("submit", event => {
  event.preventDefault();
  saveRecord();
});
formMore.addEventListener("click", addMore);
formUpdate.addEventListener("click", updatePending);
pendingList.addEventListener("click", event => {
  const edit = event.target.closest("[data-pending-edit]");
  const remove = event.target.closest("[data-pending-remove]");
  if (edit) {
    beginPendingEdit(Number(edit.dataset.pendingEdit));
  }
  if (remove) {
    removePending(Number(remove.dataset.pendingRemove));
  }
});
formDialog.querySelectorAll("[data-close-form]").forEach(button => {
  button.addEventListener("click", requestClose);
});
formDialog.addEventListener("cancel", event => {
  if (!discardPending()) {
    event.preventDefault();
  }
});
body.addEventListener("click", event => {
  const edit = event.target.closest("[data-edit]");
  const remove = event.target.closest("[data-delete]");
  if (edit) {
    const row = state.rows.find(item => String(field(item, keyName())) === edit.dataset.edit);
    if (row) {
      openForm(row);
    }
  }
  if (remove) {
    deleteRecord(remove.dataset.delete);
  }
});

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[char]));
}

function field(row, name) {
  if (!row) {
    return undefined;
  }
  const match = Object.keys(row).find(key => key.toLowerCase() === String(name).toLowerCase());
  return match ? row[match] : undefined;
}

function keyName() {
  return `${state.definition.MasterName}Id`;
}

function displayColumns() {
  const columns = [{ key: "UniqueId", label: "Unique id" }];
  const parent = state.definition.Parent;
  if (parent) {
    columns.push({ key: parent.ColumnName, label: parent.MasterName, parent: true });
  }
  for (const column of state.definition.Columns) {
    columns.push({ key: column.Name, label: column.Name, type: column.DataType });
  }
  return columns;
}

function visibleColumns() {
  const columns = displayColumns().filter(column => !state.hiddenColumns.has(column.key));
  return columns.length ? columns : displayColumns().slice(0, 1);
}

function renderColumnMenu() {
  const visible = new Set(visibleColumns().map(column => column.key));
  columnMenu.innerHTML = displayColumns().map(column => {
    const checked = visible.has(column.key);
    const locked = checked && visible.size === 1;
    return `<label class="tick"><input type="checkbox" data-column="${escapeHtml(column.key)}" ${checked ? "checked" : ""} ${locked ? "disabled" : ""} /> ${escapeHtml(column.label)}</label>`;
  }).join("");
}

function displayValue(row, column) {
  const raw = field(row, column.key);
  if (raw === null || raw === undefined || raw === "") {
    return "";
  }
  if (column.parent) {
    return state.parentLabels.get(String(raw)) || String(raw);
  }
  if (column.type === "bit") {
    return raw === true || raw === 1 || raw === "true" ? "Yes" : "No";
  }
  if (column.type === "datetime") {
    const date = new Date(raw);
    return Number.isNaN(date.getTime()) ? String(raw) : date.toLocaleString();
  }
  return String(raw);
}

function filterUsesExact(column) {
  return column.parent || String(column.type || "").toLowerCase() === "bit";
}

function filterOptions(column) {
  const values = new Set();
  for (const row of state.rows) {
    const value = displayValue(row, column);
    if (value) {
      values.add(value);
    }
  }
  return [...values].sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
}

function filterCell(column) {
  const current = state.filters[column.key] || "";
  if (filterUsesExact(column)) {
    const options = filterOptions(column).map(value => {
      const selected = value === current ? " selected" : "";
      return `<option value="${escapeHtml(value)}"${selected}>${escapeHtml(value)}</option>`;
    }).join("");
    return `<select data-filter="${escapeHtml(column.key)}"><option value="">Search...</option>${options}</select>`;
  }
  return `<input data-filter="${escapeHtml(column.key)}" placeholder="Search..." value="${escapeHtml(current)}" />`;
}

function refreshFilterSelects() {
  head.querySelectorAll("select[data-filter]").forEach(select => {
    const column = visibleColumns().find(item => item.key === select.dataset.filter);
    if (!column) {
      return;
    }
    const current = state.filters[column.key] || "";
    const values = filterOptions(column);
    select.innerHTML = `<option value="">Search...</option>${values.map(value => `<option value="${escapeHtml(value)}">${escapeHtml(value)}</option>`).join("")}`;
    if (values.includes(current)) {
      select.value = current;
    } else {
      select.value = "";
      state.filters[column.key] = "";
    }
  });
}

function syncSortButtons() {
  head.querySelectorAll("[data-sort]").forEach(button => {
    button.classList.toggle("sorted", button.dataset.sort === state.sortKey);
    button.dataset.dir = button.dataset.sort === state.sortKey && state.sortDir < 0 ? "desc" : "asc";
  });
}

function renderHead() {
  const columns = visibleColumns();
  head.innerHTML = `<tr>
    <th class="col-no">#</th>
    ${columns.map(column => `<th><button type="button" data-sort="${escapeHtml(column.key)}">${escapeHtml(column.label)}</button></th>`).join("")}
    <th>Actions</th>
  </tr>
  <tr class="filter-row">
    <th></th>
    ${columns.map(column => `<th>${filterCell(column)}</th>`).join("")}
    <th></th>
  </tr>`;
  renderedHeadKey = columns.map(column => column.key).join("|");
  syncSortButtons();
}

function filtered() {
  const term = search.value.trim().toLowerCase();
  const columns = displayColumns();
  const visible = visibleColumns();
  const rows = state.rows.filter(row => {
    if (term && !columns.some(column => displayValue(row, column).toLowerCase().includes(term))) {
      return false;
    }
    return visible.every(column => {
      const filter = String(state.filters[column.key] || "").trim().toLowerCase();
      if (!filter) {
        return true;
      }
      const value = displayValue(row, column).toLowerCase();
      return filterUsesExact(column) ? value === filter : value.includes(filter);
    });
  });
  const sortKey = state.sortKey;
  if (!sortKey) {
    return rows;
  }
  const column = columns.find(item => item.key === sortKey);
  return rows.sort((left, right) => {
    const a = displayValue(left, column).toLowerCase();
    const b = displayValue(right, column).toLowerCase();
    return a.localeCompare(b, undefined, { numeric: true }) * state.sortDir;
  });
}

function render() {
  const columns = visibleColumns();
  const rows = filtered();
  const size = pageSize.value === "all" ? Math.max(rows.length, 1) : Number(pageSize.value);
  const pages = Math.max(1, Math.ceil(rows.length / size) || 1);
  state.page = Math.min(Math.max(1, state.page), pages);
  const start = (state.page - 1) * size;
  const pageRows = rows.slice(start, start + size);
  const noun = rows.length === 1 ? state.definition.MasterName.toLowerCase() : `${state.definition.MasterName.toLowerCase()} records`;
  const headKey = columns.map(column => column.key).join("|");
  if (headKey !== renderedHeadKey) {
    renderHead();
  } else {
    syncSortButtons();
  }
  const name = state.definition.MasterName.toLowerCase();
  const searching = Boolean(search.value.trim()) || columns.some(column => String(state.filters[column.key] || "").trim());
  const emptyText = state.rows.length && searching
    ? `No ${escapeHtml(name)} records match this search.`
    : `No ${escapeHtml(name)} records yet.`;
  body.innerHTML = pageRows.length
    ? pageRows.map((row, index) => {
        const id = field(row, keyName());
        return `<tr>
          <td class="col-no">${start + index + 1}</td>
          ${columns.map(column => `<td>${escapeHtml(displayValue(row, column) || "-")}</td>`).join("")}
          <td class="row-actions">
            <button class="edit-link" type="button" data-edit="${escapeHtml(id)}">Edit</button>
            <button class="edit-link danger-link" type="button" data-delete="${escapeHtml(id)}">Delete</button>
          </td>
        </tr>`;
      }).join("")
    : `<tr><td colspan="${columns.length + 2}" class="muted empty-note">${emptyText}</td></tr>`;
  const from = rows.length ? start + 1 : 0;
  const to = Math.min(start + size, rows.length);
  pageLabel.textContent = rows.length ? `Showing ${from} to ${to} of ${rows.length} ${noun}` : `Showing 0 ${state.definition.MasterName.toLowerCase()} records`;
  document.querySelector("#page-current").textContent = String(state.page);
  document.querySelector("#page-prev").disabled = state.page <= 1;
  document.querySelector("#page-next").disabled = state.page >= pages || rows.length === 0;
}

function inputFor(column, value) {
  const type = (column.DataType || "").toLowerCase();
  const name = escapeHtml(column.Name);
  const required = column.Nullable ? "" : "required";
  if (type === "bit") {
    const checked = value === true || value === 1 || value === "true" || value === "Yes" ? "checked" : "";
    return `<label class="tick"><input type="checkbox" name="${name}" ${checked} /> ${name}${required ? ' <span class="req">*</span>' : ""}</label>`;
  }
  if (type === "int" || type === "bigint") {
    return `<input type="number" step="1" name="${name}" ${required} value="${escapeHtml(value ?? "")}" />`;
  }
  if (type === "decimal") {
    return `<input type="number" step="any" name="${name}" ${required} value="${escapeHtml(value ?? "")}" />`;
  }
  if (type === "datetime") {
    const date = value ? new Date(value) : null;
    const local = date && !Number.isNaN(date.getTime()) ? toLocalInput(date) : "";
    return `<input type="datetime-local" name="${name}" ${required} value="${escapeHtml(local)}" />`;
  }
  const max = column.IsMax || !column.Length ? "" : `maxlength="${column.Length}"`;
  if (type === "nvarchar" || type === "varchar") {
    if (column.IsMax || (column.Length || 0) > 200) {
      return `<textarea name="${name}" rows="3" ${required} ${max}>${escapeHtml(value ?? "")}</textarea>`;
    }
  }
  return `<input type="text" name="${name}" ${required} ${max} value="${escapeHtml(value ?? "")}" />`;
}

function toLocalInput(date) {
  const pad = number => String(number).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function openForm(row) {
  state.pending = [];
  state.pendingEdit = -1;
  state.editingId = row ? Number(field(row, keyName())) || 0 : 0;
  fillForm(row, row ? "edit" : "add");
  formDialog.showModal();
}

function fillForm(row, mode) {
  const creating = mode === "add";
  const editingPending = mode === "pending";
  formTitle.textContent = creating ? `Add ${state.definition.MasterName}` : `Edit ${state.definition.MasterName}`;
  formSave.textContent = saveButtonText();
  formMore.hidden = !creating;
  formUpdate.hidden = !editingPending;
  formError.hidden = true;
  formError.textContent = "";
  const parent = state.definition.Parent;
  const parts = [];
  if (parent) {
    const current = row ? field(row, parent.ColumnName) : "";
    const options = [...state.parentLabels.entries()].map(([id, label]) => {
      const selected = String(current ?? "") === id ? "selected" : "";
      return `<option value="${escapeHtml(id)}" ${selected}>${escapeHtml(label)}</option>`;
    }).join("");
    const required = parent.Required ? "required" : "";
    parts.push(`<label><span>${escapeHtml(parent.MasterName)}${parent.Required ? ' <span class="req">*</span>' : ""}</span>
      <select name="${escapeHtml(parent.ColumnName)}" ${required}>
        <option value="">Select ${escapeHtml(parent.MasterName)}</option>
        ${options}
      </select>
    </label>`);
  }
  for (const column of state.definition.Columns) {
    const type = (column.DataType || "").toLowerCase();
    const current = row ? field(row, column.Name) : "";
    if (type === "bit") {
      parts.push(inputFor(column, current));
      continue;
    }
    parts.push(`<label><span>${escapeHtml(column.Name)}${column.Nullable ? "" : ' <span class="req">*</span>'}</span>${inputFor(column, current)}</label>`);
  }
  formFields.innerHTML = parts.join("");
  renderPending();
}

function formHasValues() {
  const parent = state.definition.Parent;
  if (parent && form.elements[parent.ColumnName]?.value) {
    return true;
  }
  for (const column of state.definition.Columns) {
    const input = form.elements[column.Name];
    if (!input) {
      continue;
    }
    if ((column.DataType || "").toLowerCase() === "bit") {
      if (input.checked) {
        return true;
      }
      continue;
    }
    if ((input.value || "").trim()) {
      return true;
    }
  }
  return false;
}

function pendingLabelText(payload) {
  const parts = [];
  const parent = state.definition.Parent;
  if (parent && payload[parent.ColumnName]) {
    parts.push(state.parentLabels.get(String(payload[parent.ColumnName])) || String(payload[parent.ColumnName]));
  }
  for (const column of state.definition.Columns) {
    const value = payload[column.Name];
    if (value === null || value === undefined || value === "") {
      continue;
    }
    if ((column.DataType || "").toLowerCase() === "bit") {
      parts.push(value ? `${column.Name}: Yes` : `${column.Name}: No`);
    } else {
      parts.push(String(value));
    }
    if (parts.length >= 3) {
      break;
    }
  }
  return parts.join(" · ") || state.definition.MasterName;
}

function renderPending() {
  const adding = state.editingId === 0;
  pendingBox.hidden = !adding || state.pending.length === 0;
  if (!adding || state.pending.length === 0) {
    pendingList.innerHTML = "";
    return;
  }
  const count = state.pending.length;
  pendingLabel.textContent = count === 1
    ? "1 record added. Save stores it, or add another."
    : `${count} records added. Save stores all of them.`;
  pendingList.innerHTML = state.pending.map((payload, index) => `<li class="${index === state.pendingEdit ? "current" : ""}">
      <span class="pending-text">${escapeHtml(pendingLabelText(payload))}</span>
      <button class="edit-link" type="button" data-pending-edit="${index}">Edit</button>
      <button class="edit-link danger-link" type="button" data-pending-remove="${index}">Remove</button>
    </li>`).join("");
}

function keepCurrentDraft() {
  if (state.editingId !== 0) {
    return true;
  }
  if (state.pendingEdit < 0 && !formHasValues()) {
    return true;
  }
  if (!form.reportValidity()) {
    formError.textContent = "Finish this record before editing another.";
    formError.hidden = false;
    return false;
  }
  const payload = readForm();
  const duplicate = duplicateMessage(payload, draftSiblings());
  if (duplicate) {
    showFormError(duplicate);
    return false;
  }
  if (state.pendingEdit >= 0) {
    state.pending[state.pendingEdit] = payload;
  } else {
    state.pending.push(payload);
  }
  formError.hidden = true;
  return true;
}

function addMore() {
  if (state.editingId !== 0 || !form.reportValidity()) {
    return;
  }
  const payload = readForm();
  const duplicate = duplicateMessage(payload, draftSiblings());
  if (duplicate) {
    showFormError(duplicate);
    return;
  }
  if (state.pendingEdit >= 0) {
    state.pending[state.pendingEdit] = payload;
  } else {
    state.pending.push(payload);
  }
  state.pendingEdit = -1;
  fillForm(null, "add");
}

function updatePending() {
  if (state.pendingEdit < 0 || !form.reportValidity()) {
    return;
  }
  const payload = readForm();
  const duplicate = duplicateMessage(payload, draftSiblings());
  if (duplicate) {
    showFormError(duplicate);
    return;
  }
  state.pending[state.pendingEdit] = payload;
  state.pendingEdit = -1;
  fillForm(null, "add");
}

function beginPendingEdit(index) {
  if (!Number.isInteger(index) || !state.pending[index] || state.pendingEdit === index) {
    return;
  }
  if (!keepCurrentDraft()) {
    return;
  }
  state.pendingEdit = index;
  fillForm(state.pending[index], "pending");
}

function removePending(index) {
  if (!Number.isInteger(index) || !state.pending[index]) {
    return;
  }
  state.pending.splice(index, 1);
  if (state.pendingEdit === index) {
    state.pendingEdit = -1;
    fillForm(null, "add");
    return;
  }
  if (state.pendingEdit > index) {
    state.pendingEdit -= 1;
  }
  renderPending();
  if (state.editingId === 0 && state.pendingEdit < 0) {
    formSave.textContent = saveButtonText();
  }
}

function saveButtonText() {
  if (state.editingId === 0 && state.pending.length > 0) {
    return "Save all";
  }
  return "Save";
}

function discardPending() {
  if (state.editingId !== 0 || state.pending.length === 0) {
    return true;
  }
  return window.confirm("Close without saving the added records?");
}

function requestClose() {
  if (discardPending()) {
    formDialog.close();
  }
}

function recordsToSave() {
  const batch = state.pending.map(item => ({ ...item, [keyName()]: 0, createdBy: actorId, updatedBy: null, isActive: true }));
  if (state.pendingEdit >= 0) {
    if (!form.reportValidity()) {
      return null;
    }
    batch[state.pendingEdit] = readForm();
    return batch;
  }
  if (formHasValues() || batch.length === 0) {
    if (!form.reportValidity()) {
      return null;
    }
    batch.push(readForm());
  }
  return batch;
}

function showFormError(message) {
  formError.textContent = message;
  formError.hidden = false;
}

function mustBeUnique(column) {
  const type = (column.DataType || "").toLowerCase();
  const text = type === "nvarchar" || type === "varchar";
  return column.Unique || (text && !column.Nullable && !column.IsMax);
}

function sameValue(left, right) {
  return String(left ?? "").trim().toLowerCase() === String(right ?? "").trim().toLowerCase();
}

function sameParent(row, payload) {
  const parent = state.definition.Parent;
  if (!parent) {
    return true;
  }
  const current = field(row, parent.ColumnName);
  const next = payload[parent.ColumnName];
  return String(current ?? "") === String(next ?? "");
}

function draftSiblings() {
  if (state.pendingEdit < 0) {
    return state.pending;
  }
  return state.pending.filter((_, index) => index !== state.pendingEdit);
}

function duplicateMessage(payload, others, ignoreId = state.editingId) {
  for (const column of state.definition.Columns.filter(mustBeUnique)) {
    const value = payload[column.Name];
    if (value === null || value === undefined || String(value).trim() === "") {
      continue;
    }
    const saved = state.rows.some(row => {
      const id = Number(field(row, keyName())) || 0;
      if (ignoreId && id === ignoreId) {
        return false;
      }
      return sameParent(row, payload) && sameValue(field(row, column.Name), value);
    });
    const staged = (others || []).some(item => sameParent(item, payload) && sameValue(item[column.Name], value));
    if (saved || staged) {
      return `${column.Name} already exists.`;
    }
  }
  return "";
}

function readForm() {
  const payload = {
    [keyName()]: state.editingId,
    createdBy: actorId,
    updatedBy: state.editingId ? actorId : null,
    isActive: true
  };
  const parent = state.definition.Parent;
  if (parent) {
    const selected = form.elements[parent.ColumnName]?.value;
    payload[parent.ColumnName] = selected ? Number(selected) : null;
  }
  for (const column of state.definition.Columns) {
    const type = (column.DataType || "").toLowerCase();
    const input = form.elements[column.Name];
    if (type === "bit") {
      payload[column.Name] = Boolean(input?.checked);
      continue;
    }
    const text = input?.value?.trim?.() ?? "";
    if (!text) {
      payload[column.Name] = null;
      continue;
    }
    if (type === "int" || type === "bigint" || type === "decimal") {
      payload[column.Name] = Number(text);
      continue;
    }
    payload[column.Name] = text;
  }
  return payload;
}

async function postRecord(payload) {
  await dar.api(`/${encodeURIComponent(masterName)}`, {
    method: "POST",
    body: JSON.stringify(payload)
  });
}

async function saveRecord() {
  formError.hidden = true;
  const name = state.definition.MasterName;
  if (state.editingId !== 0) {
    if (!form.reportValidity()) {
      return;
    }
    const current = readForm();
    const duplicate = duplicateMessage(current, []);
    if (duplicate) {
      showFormError(duplicate);
      return;
    }
    try {
      await postRecord(current);
      formDialog.close();
      await loadRows();
      dar.ok(`${name} updated.`);
    } catch (error) {
      formError.textContent = error.message || "Could not save this record.";
      formError.hidden = false;
    }
    return;
  }

  const batch = recordsToSave();
  if (!batch || batch.length === 0) {
    return;
  }
  for (let index = 0; index < batch.length; index += 1) {
    const duplicate = duplicateMessage(batch[index], batch.filter((_, item) => item !== index), 0);
    if (duplicate) {
      showFormError(duplicate);
      return;
    }
  }
  let saved = 0;
  try {
    for (const item of batch) {
      await postRecord(item);
      saved += 1;
    }
  } catch (error) {
    state.pending = batch.slice(saved);
    state.pendingEdit = -1;
    fillForm(null, "add");
    formError.textContent = error.message || "Could not save this record.";
    formError.hidden = false;
    if (saved > 0) {
      await loadRows().catch(() => {});
    }
    return;
  }
  state.pending = [];
  state.pendingEdit = -1;
  formDialog.close();
  await loadRows();
  dar.ok(saved === 1 ? `${name} saved.` : `${saved} ${name} records saved.`);
}

async function deleteRecord(id) {
  const name = state.definition.MasterName;
  if (!window.confirm(`Delete this ${name}?`)) {
    return;
  }
  try {
    await dar.api(`/${encodeURIComponent(masterName)}/${id}`, { method: "DELETE" });
    dar.ok(`${name} deleted.`);
    await loadRows();
  } catch (error) {
    dar.fail(error.message || `Could not delete this ${name}.`);
  }
}

function labelFrom(definition, row) {
  const text = definition.Columns.find(column => column.DataType === "nvarchar" || column.DataType === "varchar");
  if (text) {
    const value = field(row, text.Name);
    if (value) {
      return String(value);
    }
  }
  return String(field(row, "UniqueId") || field(row, `${definition.MasterName}Id`) || "");
}

async function loadParents() {
  const parent = state.definition.Parent;
  state.parentLabels = new Map();
  if (!parent) {
    return;
  }
  const [definition, rows] = await Promise.all([
    dar.api(`/${encodeURIComponent(parent.MasterName)}/definition`),
    dar.api(`/${encodeURIComponent(parent.MasterName)}`)
  ]);
  for (const row of rows ?? []) {
    const id = field(row, `${definition.MasterName}Id`);
    if (id !== undefined && id !== null) {
      state.parentLabels.set(String(id), labelFrom(definition, row));
    }
  }
}

async function loadRows() {
  state.rows = await dar.api(`/${encodeURIComponent(masterName)}`) || [];
  if (renderedHeadKey) {
    refreshFilterSelects();
  }
  render();
}

function fileBase() {
  const name = String(state.definition?.MasterName || "records").trim().toLowerCase().replace(/[^\w\-]+/g, "-");
  return name || "records";
}

function exportPayload(rows) {
  const columns = visibleColumns();
  return {
    title: state.definition.MasterName,
    headers: columns.map(column => column.label),
    rows: rows.map(row => columns.map(column => displayValue(row, column) || "-"))
  };
}

function downloadExcel(rows) {
  return darExport.downloadExcel({ ...exportPayload(rows), fileName: `${fileBase()}.xlsx` });
}

function downloadPdf(rows) {
  return darExport.downloadPdf({ ...exportPayload(rows), fileName: `${fileBase()}.pdf` });
}

async function start() {
  if (!masterName) {
    title.textContent = "Master";
    hint.textContent = "Open a master from the menu.";
    return;
  }
  title.textContent = masterName;
  crumb.textContent = masterName;
  document.title = masterName;
  try {
    state.definition = await dar.api(`/${encodeURIComponent(masterName)}/definition`);
    title.textContent = state.definition.MasterName;
    crumb.textContent = state.definition.MasterName;
    document.title = state.definition.MasterName;
    hint.textContent = state.definition.Parent
      ? `Each ${state.definition.MasterName.toLowerCase()} belongs to a ${state.definition.Parent.MasterName.toLowerCase()}.`
      : `Add and manage ${state.definition.MasterName.toLowerCase()} records.`;
    addButton.textContent = `Add ${state.definition.MasterName}`;
    renderColumnMenu();
    await loadParents();
    await loadRows();
    addButton.hidden = false;
  } catch (error) {
    hint.textContent = error.message || "Could not open this master.";
    dar.fail(hint.textContent);
  }
}

start();
