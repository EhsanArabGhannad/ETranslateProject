// Generated from shared table-row-plan.js; source-sha256:5a3bcc2530e3dbcbfff7e6c2f50500e3c8525eb20a78d62e6db2e6bb67970f01
var NativeTableRows = (() => {
  var __defProp = Object.defineProperty;
  var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __hasOwnProp = Object.prototype.hasOwnProperty;
  var __export = (target, all) => {
    for (var name in all)
      __defProp(target, name, { get: all[name], enumerable: true });
  };
  var __copyProps = (to, from, except, desc) => {
    if (from && typeof from === "object" || typeof from === "function") {
      for (let key of __getOwnPropNames(from))
        if (!__hasOwnProp.call(to, key) && key !== except)
          __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
    }
    return to;
  };
  var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

  // ../../Shared/DocumentLayout/table-row-plan.js
  var table_row_plan_exports = {};
  __export(table_row_plan_exports, {
    applyRowPlan: () => applyRowPlan,
    buildRowPlan: () => buildRowPlan,
    measureRowPlan: () => measureRowPlan,
    prepareRowPlan: () => prepareRowPlan,
    rowPlanVersion: () => rowPlanVersion
  });
  var rowPlanVersion = "native-table-row-plan-v1";
  function buildRowPlan(tables, pages, fingerprint) {
    if (!Number.isInteger(pages) || pages < 1 || pages > 50 || !Array.isArray(tables) || typeof fingerprint !== "string")
      throw new Error("Unsupported row-plan input");
    return { version: rowPlanVersion, pages, fingerprint, tables: tables.map((table, index) => {
      if (!Array.isArray(table.rows) || !table.rows.length || typeof table.hasHeader !== "boolean") throw new Error("Invalid table plan");
      const start = table.hasHeader ? 1 : 0, breaks = [];
      if (table.hasHeader && (table.rows[0].index !== 0 || !Number.isInteger(table.rows[0].page) || table.rows[0].page < 1 || table.rows[0].page > pages || !Number.isInteger(table.rows[0].fragments) || table.rows[0].fragments < 1)) throw new Error("Invalid heading fragment");
      for (let i = start; i < table.rows.length; i++) {
        const row = table.rows[i];
        if (row.index !== i || row.fragments !== 1 || !Number.isInteger(row.page) || row.page < 1 || row.page > pages)
          throw new Error("A table body row is not one bounded native fragment");
        if (i > start) {
          const previous = table.rows[i - 1].page;
          if (row.page < previous || row.page > previous + 1) throw new Error("Non-contiguous table fragments");
          if (row.page > previous) breaks.push(i);
        }
      }
      return { index, hasHeader: table.hasHeader, rowCount: table.rows.length, rows: table.rows, breakBeforeRows: breaks };
    }) };
  }
  function contentFingerprint(root) {
    let hash = 2166136261;
    const value = root.textContent + [...root.querySelectorAll("table")].map((table) => [...table.rows].map((row) => [...row.cells].map((cell) => {
      const style = [cell.style.backgroundColor, cell.style.borderColor, cell.style.borderWidth, cell.style.borderStyle];
      return cell.tagName + (cell.colSpan > 1 ? `[${cell.colSpan}]` : "") + (style.some(Boolean) ? ":" + style.join("/") : "");
    }).join(",")).join(";") + (table.dataset.etColumnWidths ? `:${table.dataset.etColumnWidths}` : "")).join("|") + (root.querySelector("[data-et-page-start]") ? ":page-starts:" + [...root.querySelectorAll("p,h1,h2,h3,h4,h5,h6")].flatMap((node, index) => node.hasAttribute("data-et-page-start") ? [index] : []).join(",") : "");
    for (let i = 0; i < value.length; i++) hash = Math.imul(hash ^ value.charCodeAt(i), 16777619);
    return `${value.length}:${hash >>> 0}`;
  }
  function measureRowPlan(root, metrics) {
    const { contentWidth: width, contentHeight: height, columnGap: gap } = metrics ?? {};
    if (![width, height, gap].every(Number.isFinite) || width <= 0 || height <= 0 || gap < 0) throw new Error("Unsupported row-plan geometry");
    if (root.querySelector("table table")) throw new Error("Nested tables are not supported");
    if ([...root.querySelectorAll("th,td")].some((cell) => cell.colSpan < 1 || cell.colSpan > 12 || cell.rowSpan !== 1)) throw new Error("Only bounded horizontal spans are supported");
    for (const table of root.querySelectorAll("table")) {
      const widths = [...table.rows].map((row) => [...row.cells].reduce((sum, cell) => sum + cell.colSpan, 0));
      if (!widths.length || widths[0] < 1 || widths[0] > 12 || !widths.every((value) => value === widths[0])) throw new Error("Invalid logical column grid");
    }
    const saved = /* @__PURE__ */ new Map();
    const remember = (node) => {
      if (!saved.has(node)) saved.set(node, {
        present: node.hasAttribute("style"),
        properties: [...node.style].map((name) => [name, node.style.getPropertyValue(name), node.style.getPropertyPriority(name)])
      });
    };
    try {
      remember(root);
      const firstBlock = root.tagName === "MAIN" && root.firstElementChild?.tagName === "DIV" ? root.firstElementChild.firstElementChild : root.firstElementChild;
      for (const block of root.querySelectorAll("[data-et-page-start]")) {
        remember(block);
        block.style.breakBefore = block === firstBlock ? "auto" : "column";
      }
      Object.assign(root.style, { width: `${width}px`, height: `${height}px`, columnCount: "1", columnWidth: `${width}px`, columnGap: `${gap}px`, columnFill: "auto" });
      const tables = [...root.querySelectorAll("table")];
      for (const table of tables) {
        const first = table.rows[0];
        if (first?.cells.length && [...first.cells].every((cell) => cell.tagName === "TH")) {
          if (first.parentElement.tagName === "THEAD") {
            remember(first.parentElement);
            first.parentElement.style.breakInside = "avoid";
          } else {
            remember(first.parentElement);
            first.parentElement.style.display = "contents";
            remember(first);
            Object.assign(first.style, { display: "table-header-group", breakInside: "avoid" });
          }
        }
      }
      const origin = root.getBoundingClientRect().left, pitch = width + gap;
      const pages = Math.max(1, Math.ceil((root.scrollWidth + gap - 1) / pitch));
      const values = tables.map((table) => ({
        hasHeader: !!table.rows[0]?.cells.length && [...table.rows[0].cells].every((cell) => cell.tagName === "TH"),
        rows: [...table.rows].map((row, index) => {
          const rects = [...row.getClientRects()];
          return { index, page: rects.length ? Math.max(1, Math.floor((rects[0].left - origin + 1) / pitch) + 1) : null, fragments: rects.length };
        })
      }));
      return buildRowPlan(values, pages, contentFingerprint(root));
    } finally {
      for (const [node, style] of saved) {
        while (node.style.length) node.style.removeProperty(node.style.item(0));
        for (const [name, value, priority] of style.properties) node.style.setProperty(name, value, priority);
        if (!style.present) node.removeAttribute("style");
      }
    }
  }
  function applyRowPlan(root, plan) {
    const tables = [...root.querySelectorAll("table")];
    if (plan?.version !== rowPlanVersion || plan.fingerprint !== contentFingerprint(root) || tables.length !== plan.tables.length)
      throw new Error("Row plan is stale or unsupported");
    for (const table of plan.tables) {
      const rows = tables[table.index]?.rows;
      if (!rows || rows.length !== table.rowCount || table.breakBeforeRows.some((index) => !Number.isInteger(index) || index < 1 || index >= rows.length))
        throw new Error("Row-plan shape changed");
    }
    for (const table of plan.tables) for (const index of table.breakBeforeRows) tables[table.index].rows[index].style.breakBefore = "page";
    return plan;
  }
  function prepareRowPlan(root, metrics) {
    return applyRowPlan(root, measureRowPlan(root, metrics));
  }
  return __toCommonJS(table_row_plan_exports);
})();
