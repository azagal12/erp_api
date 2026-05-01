const state = {
  clients: [],
  products: [],
  sales: [],
  payments: [],
  currentItems: []
};

const fallbackData = {
  clients: [],
  products: [
    { id: "prod-1", name: "Producto ejemplo 1", sku: "P001", price: 25, stock: 20 },
    { id: "prod-2", name: "Producto ejemplo 2", sku: "P002", price: 40, stock: 12 }
  ],
  sales: [],
  payments: []
};

const money = (value) => `S/ ${Number(value || 0).toFixed(2)}`;
const niceDate = (value) => new Date(value).toLocaleString("es-PE", { dateStyle: "short", timeStyle: "short" });
const title = (value) => value.charAt(0).toUpperCase() + value.slice(1);
const makeId = (prefix) => `${prefix}-${Date.now()}-${Math.random().toString(16).slice(2)}`;
const roundMoney = (value) => Math.round((Number(value) || 0) * 100) / 100;

function toast(message) {
  const el = document.querySelector("#toast");
  el.textContent = message;
  el.classList.add("show");
  setTimeout(() => el.classList.remove("show"), 2200);
}

async function api(path, options = {}) {
  if (window.location.protocol === "file:") return localApi(path, options);
  try {
    const response = await fetch(path, {
      headers: { "Content-Type": "application/json" },
      ...options
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || "No se pudo completar la acción.");
    return payload;
  } catch (error) {
    if (error instanceof TypeError) return localApi(path, options);
    throw error;
  }
}

function readLocalDb() {
  const raw = localStorage.getItem("erp-simple-db");
  if (!raw) {
    localStorage.setItem("erp-simple-db", JSON.stringify(fallbackData));
    return structuredClone(fallbackData);
  }
  return JSON.parse(raw);
}

function writeLocalDb(db) {
  localStorage.setItem("erp-simple-db", JSON.stringify(db));
}

function getBody(options) {
  return options.body ? JSON.parse(options.body) : {};
}

function localSaleTotals(sale, payments) {
  const total = roundMoney(sale.items.reduce((sum, item) => sum + item.price * item.qty, 0));
  const paid = roundMoney(payments.filter((payment) => payment.saleId === sale.id).reduce((sum, payment) => sum + payment.amount, 0));
  return { total, paid, balance: roundMoney(Math.max(total - paid, 0)) };
}

function localSaleView(sale, db) {
  const totals = localSaleTotals(sale, db.payments);
  const autoStatus = totals.balance <= 0 && totals.total > 0 ? "pagado" : totals.paid > 0 ? "credito" : sale.status;
  return {
    ...sale,
    ...totals,
    status: sale.manualStatus ? sale.status : autoStatus,
    client: db.clients.find((client) => client.id === sale.clientId) || null
  };
}

async function localApi(path, options = {}) {
  const method = options.method || "GET";
  const db = readLocalDb();

  if (method === "GET" && path === "/api/clients") return db.clients;
  if (method === "POST" && path === "/api/clients") {
    const body = getBody(options);
    const client = {
      id: makeId("client"),
      name: body.name.trim(),
      documentType: body.documentType,
      documentNumber: body.documentNumber.trim(),
      phone: (body.phone || "").trim(),
      createdAt: new Date().toISOString()
    };
    db.clients.unshift(client);
    writeLocalDb(db);
    return client;
  }
  if (method === "DELETE" && path.startsWith("/api/clients/")) {
    const clientId = path.split("/").pop();
    if (db.sales.some((sale) => sale.clientId === clientId)) throw new Error("No se puede eliminar un cliente con ventas registradas.");
    db.clients = db.clients.filter((client) => client.id !== clientId);
    writeLocalDb(db);
    return { ok: true };
  }

  if (method === "GET" && path === "/api/products") return db.products;
  if (method === "POST" && path === "/api/products") {
    const body = getBody(options);
    const product = {
      id: makeId("prod"),
      name: body.name.trim(),
      sku: (body.sku || "").trim(),
      price: roundMoney(body.price),
      stock: Number(body.stock || 0)
    };
    db.products.unshift(product);
    writeLocalDb(db);
    return product;
  }

  if (method === "GET" && path === "/api/sales") return db.sales.map((sale) => localSaleView(sale, db));
  if (method === "POST" && path === "/api/sales") {
    const body = getBody(options);
    const sale = {
      id: makeId("sale"),
      clientId: body.clientId,
      documentNumber: `V-${String(db.sales.length + 1).padStart(5, "0")}`,
      items: body.items.map((item) => ({ ...item, id: makeId("item") })),
      status: body.status || "deuda",
      manualStatus: Boolean(body.manualStatus),
      paymentMethod: body.paymentMethod || "efectivo",
      notes: body.notes || "",
      createdAt: new Date().toISOString()
    };
    db.sales.unshift(sale);
    const total = localSaleTotals(sale, db.payments).total;
    const firstPayment = roundMoney(body.initialPayment || (sale.status === "pagado" ? total : 0));
    if (firstPayment > 0) {
      db.payments.unshift({
        id: makeId("pay"),
        saleId: sale.id,
        amount: Math.min(firstPayment, total),
        method: sale.paymentMethod,
        itemIds: [],
        note: "Pago inicial",
        createdAt: new Date().toISOString()
      });
    }
    writeLocalDb(db);
    return localSaleView(sale, db);
  }
  if (method === "PATCH" && path.startsWith("/api/sales/")) {
    const saleId = path.split("/").pop();
    const body = getBody(options);
    const sale = db.sales.find((item) => item.id === saleId);
    if (!sale) throw new Error("Venta no encontrada.");
    if (body.status) {
      sale.status = body.status;
      sale.manualStatus = true;
    }
    if (body.paymentMethod) sale.paymentMethod = body.paymentMethod;
    writeLocalDb(db);
    return localSaleView(sale, db);
  }

  if (method === "GET" && path === "/api/payments") {
    return db.payments.map((payment) => {
      const sale = db.sales.find((item) => item.id === payment.saleId);
      const client = sale ? db.clients.find((item) => item.id === sale.clientId) : null;
      return { ...payment, saleDocument: sale?.documentNumber || "", clientName: client?.name || "" };
    });
  }
  if (method === "POST" && path === "/api/payments") {
    const body = getBody(options);
    const sale = db.sales.find((item) => item.id === body.saleId);
    if (!sale) throw new Error("Venta no encontrada.");
    const totals = localSaleTotals(sale, db.payments);
    const payment = {
      id: makeId("pay"),
      saleId: sale.id,
      amount: Math.min(roundMoney(body.amount), totals.balance),
      method: body.method || "efectivo",
      itemIds: body.itemIds || [],
      note: body.note || "",
      createdAt: new Date().toISOString()
    };
    if (payment.amount <= 0) throw new Error("La venta ya está pagada.");
    db.payments.unshift(payment);
    writeLocalDb(db);
    return payment;
  }

  throw new Error("Ruta local no encontrada.");
}

async function loadAll() {
  const [clients, products, sales, payments] = await Promise.all([
    api("/api/clients"),
    api("/api/products"),
    api("/api/sales"),
    api("/api/payments")
  ]);
  state.clients = clients;
  state.products = products;
  state.sales = sales;
  state.payments = payments;
  renderAll();
}

async function refreshProductsOnly() {
  state.products = await api("/api/products");
  renderProducts();
  renderProductSelects();
}

function renderAll() {
  renderClients();
  renderProducts();
  renderProductSelects();
  renderClientSelects();
  renderCurrentItems();
  renderSales();
  renderPaymentSales();
  renderPayments();
}

function renderClients() {
  const body = document.querySelector("#clients-body");
  body.innerHTML = state.clients.map((client) => `
    <tr>
      <td>${client.name}</td>
      <td>${client.documentType.toUpperCase()} ${client.documentNumber}</td>
      <td>${client.phone || "-"}</td>
      <td><button class="danger" data-delete-client="${client.id}" type="button">Eliminar</button></td>
    </tr>
  `).join("") || `<tr><td colspan="4">Sin clientes todavía.</td></tr>`;
}

function renderProducts() {
  const body = document.querySelector("#products-body");
  body.innerHTML = state.products.map((product) => `
    <tr>
      <td>${product.name}</td>
      <td>${product.sku || "-"}</td>
      <td>${money(product.price)}</td>
      <td>${product.stock}</td>
    </tr>
  `).join("") || `<tr><td colspan="4">Sin productos todavía.</td></tr>`;
}

function renderClientSelects() {
  const options = state.clients.map((client) => `<option value="${client.id}">${client.name} - ${client.documentNumber}</option>`).join("");
  document.querySelector("#sale-client").innerHTML = options || `<option value="">Agrega un cliente</option>`;
}

function renderProductSelects() {
  const options = state.products.map((product) => `<option value="${product.id}">${product.name} - ${money(product.price)}</option>`).join("");
  document.querySelector("#sale-product").innerHTML = options || `<option value="">Agrega un producto</option>`;
}

function renderCurrentItems() {
  const box = document.querySelector("#sale-items");
  const total = state.currentItems.reduce((sum, item) => sum + item.price * item.qty, 0);
  box.innerHTML = state.currentItems.map((item, index) => `
    <div class="sale-item">
      <span>${item.name} x ${item.qty}</span>
      <strong>${money(item.price * item.qty)}</strong>
      <button class="secondary" type="button" data-remove-item="${index}">Quitar</button>
    </div>
  `).join("") + (state.currentItems.length ? `<strong>Total: ${money(total)}</strong>` : `<p class="muted">Agrega productos a la venta.</p>`);
}

function renderSales() {
  const body = document.querySelector("#sales-body");
  body.innerHTML = state.sales.map((sale) => `
    <tr class="status-row-${sale.status}">
      <td>${sale.documentNumber}<br><small>${niceDate(sale.createdAt)}</small></td>
      <td>${sale.client?.name || "-"}</td>
      <td>${money(sale.total)}</td>
      <td>${money(sale.paid)}</td>
      <td>${money(sale.balance)}</td>
      <td>
        <div class="inline-controls">
          <span class="status status-${sale.status}">${title(sale.status)}</span>
          <select data-sale-status="${sale.id}">
            <option value="pagado" ${sale.status === "pagado" ? "selected" : ""}>Pagado</option>
            <option value="credito" ${sale.status === "credito" ? "selected" : ""}>Crédito</option>
            <option value="deuda" ${sale.status === "deuda" ? "selected" : ""}>Deuda</option>
          </select>
        </div>
      </td>
      <td>
        <select data-sale-method="${sale.id}">
          <option value="efectivo" ${sale.paymentMethod === "efectivo" ? "selected" : ""}>Efectivo</option>
          <option value="yape" ${sale.paymentMethod === "yape" ? "selected" : ""}>Yape</option>
          <option value="transferencia" ${sale.paymentMethod === "transferencia" ? "selected" : ""}>Transferencia</option>
        </select>
      </td>
    </tr>
  `).join("") || `<tr><td colspan="7">Sin ventas todavía.</td></tr>`;
}

function renderPaymentSales() {
  const select = document.querySelector("#payment-sale");
  const pending = state.sales.filter((sale) => sale.balance > 0);
  select.innerHTML = pending.map((sale) => `
    <option value="${sale.id}">${sale.documentNumber} - ${sale.client?.name || ""} - saldo ${money(sale.balance)}</option>
  `).join("") || `<option value="">No hay créditos/deudas pendientes</option>`;
  renderPaymentItems();
}

function renderPaymentItems() {
  const sale = state.sales.find((item) => item.id === document.querySelector("#payment-sale").value);
  const select = document.querySelector("#payment-items");
  select.innerHTML = sale?.items.map((item) => `
    <option value="${item.id}">${item.name} x ${item.qty} - ${money(item.price * item.qty)}</option>
  `).join("") || "";
}

function renderPayments() {
  const totals = state.payments.reduce((acc, payment) => {
    acc.total += payment.amount;
    acc[payment.method] += payment.amount;
    return acc;
  }, { total: 0, efectivo: 0, yape: 0, transferencia: 0 });

  document.querySelector("#cash-total").textContent = money(totals.total);
  document.querySelector("#cash-efectivo").textContent = money(totals.efectivo);
  document.querySelector("#cash-yape").textContent = money(totals.yape);
  document.querySelector("#cash-transferencia").textContent = money(totals.transferencia);

  const body = document.querySelector("#payments-body");
  body.innerHTML = state.payments.map((payment) => `
    <tr>
      <td>${niceDate(payment.createdAt)}</td>
      <td>${payment.saleDocument}</td>
      <td>${payment.clientName}</td>
      <td>${money(payment.amount)}</td>
      <td>${title(payment.method)}</td>
      <td>${payment.note || (payment.itemIds.length ? "Pago por productos seleccionados" : "-")}</td>
    </tr>
  `).join("") || `<tr><td colspan="6">Sin movimientos de caja.</td></tr>`;
}

document.querySelectorAll(".tab").forEach((button) => {
  button.addEventListener("click", () => {
    document.querySelectorAll(".tab, .panel").forEach((el) => el.classList.remove("active"));
    button.classList.add("active");
    document.querySelector(`#${button.dataset.tab}`).classList.add("active");
  });
});

document.querySelector("#client-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  await api("/api/clients", { method: "POST", body: JSON.stringify(Object.fromEntries(form)) });
  event.currentTarget.reset();
  toast("Cliente agregado.");
  await loadAll();
});

document.querySelector("#product-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  await api("/api/products", { method: "POST", body: JSON.stringify(Object.fromEntries(form)) });
  event.currentTarget.reset();
  toast("Producto agregado.");
  await refreshProductsOnly();
});

document.querySelector("#refresh-products").addEventListener("click", async () => {
  await refreshProductsOnly();
  toast("Productos actualizados.");
});

document.addEventListener("click", async (event) => {
  const clientId = event.target.dataset.deleteClient;
  if (clientId) {
    await api(`/api/clients/${clientId}`, { method: "DELETE" });
    toast("Cliente eliminado.");
    await loadAll();
  }

  const removeIndex = event.target.dataset.removeItem;
  if (removeIndex !== undefined) {
    state.currentItems.splice(Number(removeIndex), 1);
    renderCurrentItems();
  }
});

document.querySelector("#add-sale-item").addEventListener("click", () => {
  const product = state.products.find((item) => item.id === document.querySelector("#sale-product").value);
  const qty = Number(document.querySelector("#sale-qty").value || 1);
  if (!product) return;
  state.currentItems.push({ productId: product.id, name: product.name, price: product.price, qty });
  renderCurrentItems();
});

document.querySelector("#sale-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  if (!state.currentItems.length) {
    toast("Agrega al menos un producto.");
    return;
  }
  const form = new FormData(event.currentTarget);
  const payload = Object.fromEntries(form);
  payload.items = state.currentItems;
  payload.manualStatus = true;
  await api("/api/sales", { method: "POST", body: JSON.stringify(payload) });
  state.currentItems = [];
  event.currentTarget.reset();
  toast("Venta guardada.");
  await loadAll();
});

document.addEventListener("change", async (event) => {
  const statusSaleId = event.target.dataset.saleStatus;
  if (statusSaleId) {
    await api(`/api/sales/${statusSaleId}`, { method: "PATCH", body: JSON.stringify({ status: event.target.value }) });
    toast("Estado actualizado.");
    await loadAll();
  }

  const methodSaleId = event.target.dataset.saleMethod;
  if (methodSaleId) {
    await api(`/api/sales/${methodSaleId}`, { method: "PATCH", body: JSON.stringify({ paymentMethod: event.target.value }) });
    toast("Método actualizado.");
    await loadAll();
  }

  if (event.target.id === "payment-sale") {
    renderPaymentItems();
  }
});

document.querySelector("#payment-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  const itemIds = Array.from(document.querySelector("#payment-items").selectedOptions).map((option) => option.value);
  const payload = Object.fromEntries(form);
  payload.itemIds = itemIds;
  await api("/api/payments", { method: "POST", body: JSON.stringify(payload) });
  event.currentTarget.reset();
  toast("Pago subido a caja.");
  await loadAll();
});

loadAll().catch((error) => toast(error.message));
setInterval(() => refreshProductsOnly().catch(() => {}), 5000);
