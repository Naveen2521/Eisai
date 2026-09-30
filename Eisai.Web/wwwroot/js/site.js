Notiflix.Notify.init({
  position: "right-top",
  timeout: 4000,
  cssAnimationStyle: "from-right"
});

const loader = document.querySelector("#app-loader");
let busy = 0;

window.dar = {
  apiBase: (document.querySelector('meta[name="api-base"]')?.content || "").replace(/\/$/, ""),
  showLoader() {
    busy += 1;
    loader.hidden = false;
  },
  hideLoader() {
    busy = Math.max(0, busy - 1);
    loader.hidden = busy === 0;
  },
  ok(message) { Notiflix.Notify.success(message); },
  fail(message) { Notiflix.Notify.failure(message); },
  warn(message) { Notiflix.Notify.warning(message); },
  info(message) { Notiflix.Notify.info(message); },
  async api(path, options = {}) {
    this.showLoader();
    try {
      let response;
      try {
        response = await fetch(`/api/masters${path}`, {
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          ...options
        });
      } catch {
        throw new Error("Could not reach the master API. Start again and refresh this page.");
      }
      let body;
      try {
        body = await response.json();
      } catch {
        throw new Error("The master API did not respond. Start Eisai.Api and try again.");
      }
      if (!body.Success) {
        const details = body.Errors
          ? Object.entries(body.Errors).flatMap(([, messages]) => messages).join(" ")
          : "";
        throw new Error([body.Message, details].filter(Boolean).join(" "));
      }
      return body.Data;
    } finally {
      this.hideLoader();
    }
  }
};

document.querySelectorAll("[data-soon]").forEach(node => {
  node.addEventListener("click", event => {
    event.preventDefault();
    dar.info("This module is not available yet.");
  });
});

function setGroupOpen(button, open) {
  const submenu = button.closest(".menu-node")?.querySelector(":scope > .submenu");
  if (!submenu) {
    return;
  }
  submenu.classList.toggle("open", open);
  button.classList.toggle("open", open);
  button.setAttribute("aria-expanded", String(open));
}

document.querySelector("#menu-root")?.addEventListener("click", event => {
  const button = event.target.closest("[data-group]");
  if (!button?.closest("#menu-root")) {
    return;
  }
  if (document.querySelector(".shell")?.classList.contains("collapsed") && !button.closest(".submenu")) {
    return;
  }
  const submenu = button.closest(".menu-node")?.querySelector(":scope > .submenu");
  if (!submenu) {
    return;
  }
  const open = !submenu.classList.contains("open");
  if (open) {
    button.closest(".menu-node")?.parentElement?.querySelectorAll(":scope > .menu-node > [data-group]").forEach(other => {
      if (other !== button) {
        setGroupOpen(other, false);
      }
    });
  }
  setGroupOpen(button, open);
});

if (!document.querySelector('link[href*="remixicon"]')) {
  const iconStyles = document.createElement("link");
  iconStyles.rel = "stylesheet";
  iconStyles.href = "/lib/remixicon/remixicon.css";
  document.head.append(iconStyles);
}

function useTopIcon(selector, iconClass) {
  const button = document.querySelector(selector);
  if (!button || button.querySelector("i")) {
    return;
  }
  button.replaceChildren();
  const icon = document.createElement("i");
  icon.className = iconClass;
  button.append(icon);
}

useTopIcon("#theme-toggle", "ri-sun-line");
useTopIcon("#notice-btn", "ri-notification-3-line");

document.querySelector("#theme-toggle")?.addEventListener("click", () => {
  const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = next;
  localStorage.setItem("dar-theme", next);
});

function clearMenuFlyout() {
  document.querySelectorAll(".menu-node.flyout").forEach(node => {
    node.classList.remove("flyout");
    const submenu = node.querySelector(":scope > .submenu");
    if (submenu) {
      submenu.style.top = "";
      submenu.style.left = "";
    }
  });
}

function placeMenuFlyout(node) {
  const submenu = node.querySelector(":scope > .submenu");
  if (!submenu) {
    return;
  }
  document.querySelectorAll("#menu-root > .menu-node.flyout").forEach(other => {
    if (other !== node) {
      other.classList.remove("flyout");
    }
  });
  node.classList.add("flyout");
  const sidebar = document.querySelector(".sidebar")?.getBoundingClientRect();
  const anchor = node.getBoundingClientRect();
  submenu.style.left = `${(sidebar?.right ?? anchor.right) + 8}px`;
  const height = submenu.offsetHeight;
  const top = Math.max(8, Math.min(anchor.top, window.innerHeight - height - 8));
  submenu.style.top = `${top}px`;
}

let menuFlyoutTimer;
const menuRoot = document.querySelector("#menu-root");
menuRoot?.addEventListener("mouseover", event => {
  if (!document.querySelector(".shell")?.classList.contains("collapsed")) {
    return;
  }
  const node = event.target.closest("#menu-root > .menu-node");
  if (!node) {
    return;
  }
  clearTimeout(menuFlyoutTimer);
  placeMenuFlyout(node);
});
menuRoot?.addEventListener("mouseleave", () => {
  menuFlyoutTimer = setTimeout(clearMenuFlyout, 160);
});

document.querySelector("#collapse-nav")?.addEventListener("click", () => {
  const shell = document.querySelector(".shell");
  shell.classList.toggle("collapsed");
  const collapsed = shell.classList.contains("collapsed");
  document.querySelector("#collapse-nav")?.setAttribute("aria-label", collapsed ? "Expand sidebar" : "Collapse sidebar");
  clearMenuFlyout();
});

document.querySelector("#notice-btn")?.addEventListener("click", () => {
  dar.info("No new notifications.");
});

const builtinHeading = "Super Admin Operations";

function escapeMenu(value) {
  return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[char]));
}

const menuIcons = [
  '<path d="M4 7h16M4 12h16M4 17h10"/>',
  '<rect x="4" y="4" width="7" height="7" rx="1"/><rect x="13" y="4" width="7" height="7" rx="1"/><rect x="4" y="13" width="7" height="7" rx="1"/><rect x="13" y="13" width="7" height="7" rx="1"/>',
  '<circle cx="12" cy="12" r="3"/><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M18.4 5.6 17 7M7 17l-1.4 1.4"/>',
  '<path d="M4 7h6l2 2h8v9a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1z"/>',
  '<path d="M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01"/>',
  '<path d="M12 3 4 7v6c0 5 3.4 7.6 8 8 4.6-.4 8-3 8-8V7z"/>',
  '<circle cx="12" cy="8" r="3"/><path d="M5 20c1.2-3 3.4-4.5 7-4.5S17.8 17 19 20"/>',
  '<path d="M5 19V5m0 14h14M9 15V9m4 6V7m4 8v-4"/>'
];

function menuIcon(name, kind) {
  const text = String(name ?? "");
  const glyph = kind === "home"
    ? '<path d="M4 10.5 12 4l8 6.5V20a1 1 0 0 1-1 1h-5v-6H10v6H5a1 1 0 0 1-1-1z"/>'
    : menuIcons[menuIconIndex(text)];
  return `<span class="menu-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">${glyph}</svg></span><span class="menu-label">${escapeMenu(text)}</span>`;
}

function menuIconIndex(text) {
  let hash = 0;
  for (let index = 0; index < text.length; index += 1) {
    hash = (hash + text.charCodeAt(index) * (index + 1)) % menuIcons.length;
  }
  return hash;
}

function menuKey(kind, ...parts) {
  return `${kind}:${parts.map(part => encodeURIComponent(part)).join(":")}`;
}

function applyMenuOrder(nodes, saved) {
  if (!Array.isArray(saved) || saved.length === 0) {
    return nodes;
  }

  const templates = new Map();
  const walk = list => {
    for (const node of list) {
      templates.set(node.id, node);
      walk(node.children ?? []);
    }
  };
  walk(nodes);

  const hidden = new Set();
  for (const item of saved) {
    if (item?.id !== "__hidden__") {
      continue;
    }
    for (const child of item.children ?? []) {
      if (child?.id) {
        hidden.add(child.id);
      }
    }
  }

  const used = new Set();
  const build = item => {
    if (!item?.id || item.id === "__hidden__" || hidden.has(item.id) || used.has(item.id)) {
      return null;
    }
    const template = templates.get(item.id);
    const custom = String(item.id).startsWith("group:");
    if (!template && !custom) {
      return null;
    }
    const label = String(item.label ?? "").trim();
    const node = template
      ? {
          id: template.id,
          label: label || template.label,
          kind: template.kind,
          routeLabel: template.routeLabel || template.label,
          children: []
        }
      : {
          id: item.id,
          label: label || "Group",
          kind: "group",
          routeLabel: label || "Group",
          children: []
        };
    used.add(node.id);
    node.children = (item.children ?? []).map(build).filter(Boolean);
    return node;
  };

  const tree = saved.filter(item => item?.id !== "__hidden__").map(build).filter(Boolean);
  const findNode = (list, id) => {
    for (const node of list) {
      if (node.id === id) {
        return node;
      }
      const found = findNode(node.children ?? [], id);
      if (found) {
        return found;
      }
    }
    return null;
  };

  const placeMissing = (originalList, placedList) => {
    originalList.forEach((node, index) => {
      if (hidden.has(node.id)) {
        placeMissing(node.children ?? [], placedList);
        return;
      }
      let placed = findNode(tree, node.id);
      if (!placed && !used.has(node.id)) {
        placed = {
          id: node.id,
          label: node.label,
          kind: node.kind,
          routeLabel: node.routeLabel || node.label,
          children: []
        };
        used.add(node.id);
        const previousId = originalList.slice(0, index).map(item => item.id).reverse().find(id => placedList.some(item => item.id === id));
        if (previousId) {
          placedList.splice(placedList.findIndex(item => item.id === previousId) + 1, 0, placed);
        } else {
          const nextId = originalList.slice(index + 1).map(item => item.id).find(id => placedList.some(item => item.id === id));
          if (nextId) {
            placedList.splice(placedList.findIndex(item => item.id === nextId), 0, placed);
          } else {
            placedList.push(placed);
          }
        }
      }
      if (placed) {
        placeMissing(node.children ?? [], placed.children);
      }
    });
  };

  placeMissing(nodes, tree);
  return tree;
}

dar.menuNodes = function menuNodes(masters, savedOrder) {
  const groups = new Map([[builtinHeading, new Map()]]);
  for (const master of masters ?? []) {
    if (!master.MenuHeading || !master.MenuSubHeading || master.TableExists === false) {
      continue;
    }
    if (!groups.has(master.MenuHeading)) {
      groups.set(master.MenuHeading, new Map());
    }
    const subs = groups.get(master.MenuHeading);
    if (!subs.has(master.MenuSubHeading)) {
      subs.set(master.MenuSubHeading, []);
    }
    subs.get(master.MenuSubHeading).push(master);
  }

  const nodes = [{ id: menuKey("link", "Home"), label: "Home", kind: "home", routeLabel: "Home" }];
  for (const [heading, subs] of groups) {
    const children = [];
    if (heading === builtinHeading) {
      children.push({ id: menuKey("link", "Masters"), label: "Masters", kind: "link", routeLabel: "Masters" });
      children.push({ id: menuKey("link", "Theme"), label: "Theme", kind: "link", routeLabel: "Theme" });
    }
    for (const [sub, items] of subs) {
      if (items.length === 0) {
        continue;
      }
      children.push({
        id: menuKey("sub", heading, sub),
        label: sub,
        kind: "sub",
        routeLabel: sub,
        children: items.map(item => ({ id: menuKey("master", item.MasterName), label: item.MasterName, kind: "master", routeLabel: item.MasterName }))
      });
    }
    if (children.length === 0) {
      continue;
    }
    nodes.push({ id: menuKey("heading", heading), label: heading, kind: "heading", routeLabel: heading, children });
  }
  return applyMenuOrder(nodes, savedOrder ?? window.darMenuOrder);
};

dar.renderMenu = function renderMenu(masters) {
  dar.latestMasters = masters ?? [];
  const root = document.querySelector("#menu-root");
  if (!root) {
    document.dispatchEvent(new CustomEvent("dar-menu"));
    return;
  }
  const selected = new URLSearchParams(location.search).get("master");
  const path = location.pathname.toLowerCase();
  const onList = path.endsWith("/masters") || path.endsWith("/masters/index");
  const onTheme = path === "/theme" || path.startsWith("/theme/");
  const onHome = path === "/" || path === "/home" || path.startsWith("/home/");
  const nodes = dar.menuNodes(masters);
  dar.currentMenu = nodes;

  const leaf = node => {
    const name = node.routeLabel || node.label;
    if (node.kind === "home") {
      return `<a class="menu-link${onHome ? " active" : ""}" href="/">${menuIcon(node.label, "home")}</a>`;
    }
    if (node.kind === "link") {
      const active = name === "Masters" ? onList : name === "Theme" && onTheme;
      const href = name === "Theme" ? "/Theme" : "/Masters";
      return `<a class="menu-link${active ? " active" : ""}" href="${href}">${menuIcon(node.label)}</a>`;
    }
    if (node.kind === "master") {
      const active = name === selected ? " active" : "";
      return `<a class="menu-link master-link${active}" href="/Masters/Records?master=${encodeURIComponent(name)}">${menuIcon(node.label)}</a>`;
    }
    return "";
  };

  const html = items => items.map(node => {
    const children = node.children ?? [];
    const canGroup = node.kind === "heading" || node.kind === "sub" || node.kind === "group" || children.length > 0;
    if (!canGroup) {
      return leaf(node);
    }
    const inner = html(children);
    const open = inner.includes(' active"');
    const group = node.kind === "sub" ? "sub-heading" : "menu-group";
    const nested = node.kind === "sub" ? " nested" : "";
    return `<div class="menu-node"><button class="menu-link ${group}${open ? " open" : ""}" type="button" data-group aria-expanded="${open}">${menuIcon(node.label)}</button><div class="submenu${nested}${open ? " open" : ""}"><span class="flyout-title">${escapeMenu(node.label)}</span>${inner}</div></div>`;
  }).join("");

  root.innerHTML = html(nodes);
  document.dispatchEvent(new CustomEvent("dar-menu"));
};

dar.renderMenu([]);
dar.api("").then(masters => dar.renderMenu(masters)).catch(() => dar.renderMenu([]));
