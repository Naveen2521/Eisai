const dataTypes = ["nvarchar", "varchar", "int", "bigint", "bit", "decimal", "datetime", "uniqueidentifier"];
const state = { masters: [], selected: null };

const createPanel = document.querySelector("#create-panel");
const detailPanel = document.querySelector("#detail-panel");
const columnRows = document.querySelector("#column-rows");
const addColumnButton = document.querySelector("#add-column");
const columnHint = document.querySelector("#column-hint");

document.querySelector("#new-master")?.addEventListener("click", showCreate);
addColumnButton.addEventListener("click", () => {
  if (!columnRows.querySelector(".column-row") || rowsReady(columnRows)) {
    addColumnRow(columnRows);
    refreshAddColumn();
    return;
  }
  dar.warn("Fill the current column before adding another.");
});
document.querySelector("#menu-heading-mode")?.addEventListener("change", syncMenuPlace);
document.querySelector("#menu-sub-mode")?.addEventListener("change", syncMenuPlace);
document.querySelector("#menu-heading")?.addEventListener("change", syncMenuPlace);
document.querySelector("#use-parent").addEventListener("change", toggleParent);
document.querySelector("#create-form").addEventListener("submit", createMaster);
document.querySelector("#master-name").addEventListener("input", suggestPrefix);
document.querySelector("#delete-master").addEventListener("click", deleteMaster);
document.querySelector("#add-column-form").addEventListener("submit", addColumn);
document.querySelector("#parent-required").addEventListener("change", syncParentDelete);
document.querySelector("#unique-prefix").addEventListener("input", event => {
  event.target.dataset.touched = "1";
});

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[char]));
}

function textType(type) {
  return type === "nvarchar" || type === "varchar";
}

function columnEditor(target, column) {
  const row = document.createElement("tr");
  row.className = "column-row";
  const required = column ? !column.Nullable : true;
  const typeOptions = dataTypes.map(type => `<option ${column?.DataType === type ? "selected" : ""}>${type}</option>`).join("");
  row.innerHTML = `
    <td class="row-no"></td>
    <td><input data-field="name" maxlength="40" placeholder="Column name" value="${escapeHtml(column?.Name ?? "")}"></td>
    <td><select data-field="type">${typeOptions}</select></td>
    <td class="size-cell">
      <input data-field="length" data-length type="number" min="1" value="${column?.Length ?? 150}">
      <span class="size-pair" data-decimal hidden>
        <input data-field="precision" type="number" min="1" max="38" value="${column?.Precision ?? 18}" title="Precision">
        <input data-field="scale" type="number" min="0" value="${column?.Scale ?? 2}" title="Scale">
      </span>
      <span data-plain class="muted" hidden>—</span>
    </td>
    <td class="constraints">
      <label class="tick"><input data-field="required" type="checkbox" ${required ? "checked" : ""}> Required</label>
      <label class="tick"><input data-field="unique" type="checkbox" ${column?.Unique ? "checked" : ""}> Unique</label>
      <label class="tick" data-length><input data-field="max" type="checkbox" ${column?.IsMax ? "checked" : ""}> Max Length</label>
    </td>
    <td class="row-actions">
      <button type="button" class="icon-action" data-edit aria-label="Edit column"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 20h4l10.5-10.5a2.1 2.1 0 0 0 0-3L16.5 4.5a2.1 2.1 0 0 0-3 0L3 15v5z"/></svg></button>
      <button type="button" class="icon-action danger" data-remove aria-label="Remove column"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 7h12l-1 14H7L6 7zm3-3h6l1 2H8l1-2z"/></svg></button>
    </td>`;
  row.querySelector("[data-edit]").addEventListener("click", () => row.querySelector("[data-field=name]").focus());
  row.querySelector("[data-remove]").addEventListener("click", () => {
    if (target === columnRows && target.querySelectorAll(".column-row").length === 1) {
      dar.warn("A master needs at least one column.");
      return;
    }
    row.remove();
    numberRows(target);
    refreshAddColumn();
  });
  row.addEventListener("input", () => {
    syncColumnRow(row);
    refreshAddColumn();
  });
  row.addEventListener("change", () => {
    syncColumnRow(row);
    refreshAddColumn();
  });
  target.append(row);
  syncColumnRow(row);
  numberRows(target);
}

function addColumnRow(target, column) {
  columnEditor(target, column);
}

function numberRows(target) {
  target.querySelectorAll(".column-row").forEach((row, index) => {
    const number = row.querySelector(".row-no");
    if (number) {
      number.textContent = String(index + 1);
    }
  });
}

function syncColumnRow(row) {
  const type = row.querySelector("[data-field=type]").value;
  const isMax = row.querySelector("[data-field=max]").checked;
  const unique = row.querySelector("[data-field=unique]");
  const isText = textType(type);
  const isDecimal = type === "decimal";
  row.querySelectorAll("[data-length]").forEach(node => { node.hidden = !isText; });
  row.querySelector("[data-field=length]").disabled = !isText || isMax;
  row.querySelector("[data-decimal]").hidden = !isDecimal;
  row.querySelector("[data-plain]").hidden = isText || isDecimal;
  if (isMax && unique) {
    unique.checked = false;
    unique.disabled = true;
  } else if (unique) {
    unique.disabled = false;
  }
}

function readColumn(row) {
  const type = row.querySelector("[data-field=type]").value;
  const isMax = row.querySelector("[data-field=max]").checked;
  const column = {
    name: row.querySelector("[data-field=name]").value.trim(),
    dataType: type,
    nullable: !row.querySelector("[data-field=required]").checked,
    unique: isMax ? false : row.querySelector("[data-field=unique]").checked
  };
  if (textType(type)) {
    column.length = isMax ? "max" : Number(row.querySelector("[data-field=length]").value);
  }
  if (type === "decimal") {
    column.precision = Number(row.querySelector("[data-field=precision]").value);
    column.scale = Number(row.querySelector("[data-field=scale]").value);
  }
  return column;
}

function rowReady(row) {
  const name = row.querySelector("[data-field=name]").value.trim();
  if (!/^[A-Za-z][A-Za-z0-9_]{0,39}$/.test(name)) {
    return false;
  }
  const type = row.querySelector("[data-field=type]").value;
  if (textType(type) && !row.querySelector("[data-field=max]").checked) {
    const length = Number(row.querySelector("[data-field=length]").value);
    const limit = type === "nvarchar" ? 4000 : 8000;
    if (!Number.isInteger(length) || length < 1 || length > limit) {
      return false;
    }
  }
  if (type === "decimal") {
    const precision = Number(row.querySelector("[data-field=precision]").value);
    const scale = Number(row.querySelector("[data-field=scale]").value);
    if (!Number.isInteger(precision) || !Number.isInteger(scale) || precision < 1 || precision > 38 || scale < 0 || scale > precision) {
      return false;
    }
  }
  return true;
}

function rowsReady(host) {
  const rows = [...host.querySelectorAll(".column-row")];
  return rows.length > 0 && rows.every(rowReady);
}

function refreshAddColumn() {
  const ready = rowsReady(columnRows);
  addColumnButton.disabled = !ready;
  columnHint.hidden = ready;
  numberRows(columnRows);
}

function selectedHeading() {
  if (document.querySelector("#menu-heading-mode").value === "new") {
    return document.querySelector("#menu-heading-new").value.trim();
  }
  return document.querySelector("#menu-heading").value;
}

function selectedSubHeading() {
  if (document.querySelector("#menu-sub-mode").value === "new") {
    return document.querySelector("#menu-sub-new").value.trim();
  }
  return document.querySelector("#menu-sub").value;
}

function subHeadingsFor(heading) {
  return [...new Set(state.masters
    .filter(master => master.MenuHeading === heading && master.MenuSubHeading)
    .map(master => master.MenuSubHeading))];
}

function syncMenuPlace() {
  const newHeading = document.querySelector("#menu-heading-mode").value === "new";
  document.querySelector("#menu-heading-existing-wrap").hidden = newHeading;
  document.querySelector("#menu-heading-new-wrap").hidden = !newHeading;

  const heading = newHeading ? "" : document.querySelector("#menu-heading").value;
  const subs = newHeading ? [] : subHeadingsFor(heading);
  const subSelect = document.querySelector("#menu-sub");
  const subMode = document.querySelector("#menu-sub-mode");
  subSelect.innerHTML = subs.map(name => `<option>${escapeHtml(name)}</option>`).join("");
  const existingOption = subMode.querySelector('option[value="existing"]');
  existingOption.disabled = subs.length === 0;
  if (subs.length === 0 && subMode.value === "existing") {
    subMode.value = "new";
  }
  const newSub = subMode.value === "new" || subs.length === 0;
  document.querySelector("#menu-sub-existing-wrap").hidden = newSub;
  document.querySelector("#menu-sub-new-wrap").hidden = !newSub;
  document.querySelector("#menu-sub-empty").hidden = newHeading || subs.length > 0;
}

function fillMenuPlaces() {
  const headings = [...new Set([
    "Super Admin Operations",
    ...state.masters.map(master => master.MenuHeading).filter(Boolean)
  ])];
  const select = document.querySelector("#menu-heading");
  const current = select.value;
  select.innerHTML = headings.map(name => `<option>${escapeHtml(name)}</option>`).join("");
  if (headings.includes(current)) {
    select.value = current;
  }
  syncMenuPlace();
}

function toggleParent() {
  const enabled = document.querySelector("#use-parent").checked;
  document.querySelector("#parent-fields").hidden = !enabled;
  if (enabled) {
    fillParents();
  }
}

function syncParentDelete() {
  const setNull = document.querySelector("#parent-delete");
  const required = document.querySelector("#parent-required").checked;
  setNull.querySelector('[value="setnull"]').disabled = required;
  if (required && setNull.value === "setnull") {
    setNull.value = "restrict";
  }
}

function fillParents() {
  const select = document.querySelector("#parent-name");
  const empty = document.querySelector("#parent-empty");
  select.innerHTML = state.masters.map(master => `<option>${escapeHtml(master.MasterName)}</option>`).join("");
  const hasParent = state.masters.length > 0;
  select.disabled = !hasParent;
  empty.hidden = hasParent;
}

function suggestPrefix() {
  const name = document.querySelector("#master-name").value.replace(/[^A-Za-z]/g, "");
  const prefix = document.querySelector("#unique-prefix");
  if (!prefix.dataset.touched) {
    prefix.value = name.slice(0, 3).toUpperCase();
  }
}

function showCreate() {
  state.selected = null;
  createPanel.hidden = false;
  detailPanel.hidden = true;
}

async function loadMasters() {
  state.masters = await dar.api("");
  fillParents();
  fillMenuPlaces();
  dar.renderMenu?.(state.masters);
}

async function createMaster(event) {
  event.preventDefault();
  if (!rowsReady(columnRows)) {
    dar.warn("Fill the current column before creating the master.");
    return;
  }
  if (document.querySelector("#use-parent").checked && state.masters.length === 0) {
    dar.warn("Create another master before choosing a parent.");
    return;
  }
  const heading = selectedHeading();
  const subHeading = selectedSubHeading();
  if (!heading || !subHeading) {
    dar.warn("Choose a heading and a sub heading for this master.");
    return;
  }
  const body = {
    masterName: document.querySelector("#master-name").value.trim(),
    uniqueIdPrefix: document.querySelector("#unique-prefix").value.trim(),
    menuHeading: heading,
    menuSubHeading: subHeading,
    columns: [...columnRows.querySelectorAll(".column-row")].map(readColumn)
  };
  if (document.querySelector("#use-parent").checked) {
    body.parent = {
      masterName: document.querySelector("#parent-name").value,
      required: document.querySelector("#parent-required").checked,
      onParentDelete: document.querySelector("#parent-delete").value
    };
  }
  try {
    const created = await dar.api("", { method: "POST", body: JSON.stringify(body) });
    dar.ok(`${created.TableName} and its procedures were created.`);
    document.querySelector("#create-form").reset();
    document.querySelector("#unique-prefix").dataset.touched = "";
    columnRows.innerHTML = "";
    addColumnRow(columnRows);
    toggleParent();
    syncMenuPlace();
    refreshAddColumn();
    await loadMasters();
    await openMaster(created.MasterName);
  } catch (error) {
    dar.fail(error.message);
  }
}

async function openMaster(name) {
  try {
    state.selected = name;
    const definition = await dar.api(`/${encodeURIComponent(name)}/definition`);
    createPanel.hidden = true;
    detailPanel.hidden = false;
    document.querySelector("#detail-title").textContent = definition.MasterName;
    const parentText = definition.Parent
      ? `Parent ${definition.Parent.MasterName} (${definition.Parent.ColumnName}, ${definition.Parent.Required ? "required" : "optional"}, ${definition.Parent.OnParentDelete})`
      : "No parent";
    document.querySelector("#detail-meta").textContent = `${definition.TableName} · prefix ${definition.UniqueIdPrefix} · ${parentText}`;
    renderColumns(definition);
    renderParent(definition);
    renderRecordForm(definition);
    await loadRecords(definition);
    await loadMasters();
  } catch (error) {
    dar.fail(error.message);
  }
}

function renderColumns(definition) {
  const host = document.querySelector("#columns");
  host.innerHTML = "";
  for (const column of definition.Columns) {
    const card = document.createElement("div");
    card.className = "column-card";
    const length = column.IsMax ? "max" : (column.Length ?? "");
    const meta = [column.DataType, length, column.Nullable ? "null" : "required", column.Unique ? "unique" : ""].filter(Boolean).join(" · ");
    card.innerHTML = `<div><strong>${escapeHtml(column.Name)}</strong><div class="muted">${escapeHtml(meta)}</div></div>`;
    const actions = document.createElement("div");
    if (textType(column.DataType)) {
      const lengthInput = document.createElement("input");
      lengthInput.value = column.IsMax ? "max" : column.Length;
      lengthInput.title = "Number or max";
      const save = document.createElement("button");
      save.type = "button";
      save.className = "btn secondary";
      save.textContent = "Change length";
      save.addEventListener("click", () => changeLength(definition, column, lengthInput.value.trim()));
      actions.append(lengthInput, save);
    }
    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "btn secondary";
    remove.textContent = "Drop";
    remove.addEventListener("click", () => dropColumn(definition.MasterName, column.Name));
    actions.append(remove);
    card.append(actions);
    host.append(card);
  }
}

async function changeLength(definition, column, lengthText) {
  const length = lengthText.toLowerCase() === "max" ? "max" : Number(lengthText);
  try {
    await dar.api(`/${definition.MasterName}/columns/${column.Name}`, {
      method: "PUT",
      body: JSON.stringify({
        name: column.Name,
        dataType: column.DataType,
        length,
        nullable: column.Nullable,
        unique: length === "max" ? false : column.Unique
      })
    });
    dar.ok(`${column.Name} length is now ${lengthText}.`);
    await openMaster(definition.MasterName);
  } catch (error) {
    dar.fail(error.message);
  }
}

async function dropColumn(masterName, columnName) {
  try {
    await dar.api(`/${masterName}/columns/${encodeURIComponent(columnName)}`, { method: "DELETE" });
    dar.ok(`${columnName} was dropped.`);
    await openMaster(masterName);
  } catch (error) {
    dar.fail(error.message);
  }
}

function renderParent(definition) {
  const host = document.querySelector("#parent-view");
  host.innerHTML = "";
  if (definition.Parent) {
    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "btn secondary";
    remove.textContent = "Remove parent";
    remove.addEventListener("click", async () => {
      try {
        await dar.api(`/${definition.MasterName}/parent`, { method: "DELETE" });
        dar.ok("Parent removed.");
        await openMaster(definition.MasterName);
      } catch (error) {
        dar.fail(error.message);
      }
    });
    host.append(remove);
    return;
  }
  const others = state.masters.filter(master => master.MasterName !== definition.MasterName);
  if (others.length === 0) {
    host.innerHTML = "<p class='muted'>Create another master before setting a parent.</p>";
    return;
  }
  const toggle = document.createElement("button");
  toggle.type = "button";
  toggle.className = "btn secondary";
  toggle.textContent = "Add a parent";
  const form = document.createElement("form");
  form.className = "stack";
  form.hidden = true;
  form.innerHTML = `
    <div class="grid-3">
      <label>Parent <select name="master">${others.map(master => `<option>${escapeHtml(master.MasterName)}</option>`).join("")}</select></label>
      <label class="check"><input name="required" type="checkbox" checked> Required</label>
      <label>When the parent row is deleted
        <select name="onDelete"><option value="restrict">Keep the parent if children exist</option><option value="setnull">Clear the parent link</option></select>
      </label>
    </div>
    <button type="submit" class="btn secondary">Save parent</button>`;
  toggle.addEventListener("click", () => {
    form.hidden = !form.hidden;
    toggle.textContent = form.hidden ? "Add a parent" : "Hide parent options";
  });
  form.querySelector('[name="required"]').addEventListener("change", event => {
    const setNull = form.querySelector('[name="onDelete"] option[value="setnull"]');
    setNull.disabled = event.target.checked;
    if (event.target.checked && form.querySelector('[name="onDelete"]').value === "setnull") {
      form.querySelector('[name="onDelete"]').value = "restrict";
    }
  });
  form.addEventListener("submit", async event => {
    event.preventDefault();
    const data = new FormData(form);
    try {
      await dar.api(`/${definition.MasterName}/parent`, {
        method: "PUT",
        body: JSON.stringify({
          masterName: data.get("master"),
          required: data.get("required") === "on",
          onParentDelete: data.get("onDelete")
        })
      });
      dar.ok("Parent saved.");
      await openMaster(definition.MasterName);
    } catch (error) {
      dar.fail(error.message);
    }
  });
  host.append(toggle, form);
}

function renderRecordForm(definition) {
  const form = document.querySelector("#record-form");
  const fields = definition.Columns.map(column =>
    `<label><span>${escapeHtml(column.Name)}${column.Nullable ? "" : ' <span class="req">*</span>'}</span><input name="${escapeHtml(column.Name)}" ${column.Nullable ? "" : "required"}></label>`).join("");
  const parent = definition.Parent
    ? `<label><span>${escapeHtml(definition.Parent.ColumnName)}${definition.Parent.Required ? ' <span class="req">*</span>' : ""}</span><input name="${escapeHtml(definition.Parent.ColumnName)}" type="number" ${definition.Parent.Required ? "required" : ""}></label>`
    : "";
  const idKey = `${definition.MasterName[0].toLowerCase()}${definition.MasterName.slice(1)}Id`;
  form.innerHTML = `<div class="grid-2">${parent}${fields}<label>Created by <input name="createdBy" type="number" required value="1"></label></div><button class="btn" type="submit">Save record</button>`;
  form.onsubmit = async event => {
    event.preventDefault();
    const data = new FormData(form);
    const body = { [idKey]: 0 };
    for (const [key, value] of data.entries()) {
      body[key] = value === "" ? null : (/^-?\d+$/.test(value) ? Number(value) : value);
    }
    try {
      await dar.api(`/${definition.MasterName}`, { method: "POST", body: JSON.stringify(body) });
      dar.ok("Record saved.");
      form.reset();
      await loadRecords(definition);
    } catch (error) {
      dar.fail(error.message);
    }
  };
}

async function loadRecords(definition) {
  const host = document.querySelector("#records");
  const rows = await dar.api(`/${definition.MasterName}`);
  if (!rows.length) {
    host.innerHTML = "<p class='muted'>No active records.</p>";
    return;
  }
  const idName = `${definition.MasterName}Id`;
  const headers = Object.keys(rows[0]);
  host.innerHTML = `<table><thead><tr>${headers.map(header => `<th>${escapeHtml(header)}</th>`).join("")}<th></th></tr></thead><tbody>
    ${rows.map(row => `<tr>${headers.map(header => `<td>${escapeHtml(row[header] ?? "")}</td>`).join("")}<td><button type="button" class="btn secondary" data-id="${escapeHtml(row[idName])}">Delete</button></td></tr>`).join("")}
  </tbody></table>`;
  host.querySelectorAll("[data-id]").forEach(button => {
    button.addEventListener("click", async () => {
      try {
        await dar.api(`/${definition.MasterName}/${button.dataset.id}`, { method: "DELETE" });
        dar.ok("Record removed.");
        await loadRecords(definition);
      } catch (error) {
        dar.fail(error.message);
      }
    });
  });
}

async function addColumn(event) {
  event.preventDefault();
  const row = document.querySelector("#extra-column .column-row");
  if (!rowReady(row)) {
    dar.warn("Fill the new column before saving it.");
    return;
  }
  try {
    await dar.api(`/${state.selected}/columns`, { method: "POST", body: JSON.stringify(readColumn(row)) });
    dar.ok("Column added.");
    document.querySelector("#extra-column").innerHTML = "";
    addColumnRow(document.querySelector("#extra-column"));
    await openMaster(state.selected);
  } catch (error) {
    dar.fail(error.message);
  }
}

function deleteMaster() {
  if (!state.selected) {
    return;
  }
  const name = state.selected;
  Notiflix.Confirm.show(
    "Delete master",
    `${name} and its table will be removed.`,
    "Delete",
    "Cancel",
    async () => {
      try {
        await dar.api(`/${name}/definition`, { method: "DELETE" });
        dar.ok(`${name} was removed.`);
        showCreate();
        await loadMasters();
      } catch (error) {
        dar.fail(error.message);
      }
    }
  );
}

addColumnRow(columnRows);
addColumnRow(document.querySelector("#extra-column"));
syncParentDelete();
refreshAddColumn();
syncMenuPlace();
const requestedMaster = new URLSearchParams(location.search).get("master");
loadMasters()
  .then(() => requestedMaster ? openMaster(requestedMaster) : null)
  .catch(error => dar.fail(error.message));
