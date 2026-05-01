const http = require("http");
const fs = require("fs/promises");
const path = require("path");
const crypto = require("crypto");

const PORT = Number(process.env.PORT || 3000);
const APP_USER = process.env.APP_USER || "TecnoStor";
const APP_PASSWORD = process.env.APP_PASSWORD || "258922";
const APP_TOKEN = process.env.APP_TOKEN || "tecnostor-local-token";
const ROOT = __dirname;
const PUBLIC_DIR = path.join(ROOT, "public");
const DATA_DIR = path.join(ROOT, "data");
const DATA_FILE = path.join(DATA_DIR, "db.json");
let pgPool;

const initialData = {
  clients: [],
  products: [
    { id: "prod-1", name: "Producto ejemplo 1", sku: "P001", category: "GENERAL", brand: "", model: "", price: 25, costPrice: 18, salePrice: 25, stock: 20, serials: [] },
    { id: "prod-2", name: "Producto ejemplo 2", sku: "P002", category: "GENERAL", brand: "", model: "", price: 40, costPrice: 30, salePrice: 40, stock: 12, serials: [] }
  ],
  sales: [],
  payments: []
};

const mimeTypes = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml"
};

function id(prefix) {
  return `${prefix}-${crypto.randomBytes(8).toString("hex")}`;
}

function money(value) {
  return Math.round((Number(value) || 0) * 100) / 100;
}

async function ensureData() {
  if (process.env.DATABASE_URL) {
    const db = await getPgPool();
    await db.query(`
      create table if not exists clients (
        id text primary key,
        name text not null,
        document_type text not null check (document_type in ('dni', 'ruc')),
        document_number text not null,
        phone text,
        created_at timestamptz not null default now()
      );
      create table if not exists products (
        id text primary key,
        name text not null,
        sku text,
        category text not null default 'GENERAL',
        brand text not null default '',
        model text not null default '',
        price numeric(12, 2) not null default 0,
        cost_price numeric(12, 2) not null default 0,
        sale_price numeric(12, 2) not null default 0,
        serials jsonb not null default '[]'::jsonb,
        stock integer not null default 0
      );
      alter table products add column if not exists cost_price numeric(12, 2) not null default 0;
      alter table products add column if not exists sale_price numeric(12, 2) not null default 0;
      alter table products add column if not exists serials jsonb not null default '[]'::jsonb;
      alter table products add column if not exists category text not null default 'GENERAL';
      alter table products add column if not exists brand text not null default '';
      alter table products add column if not exists model text not null default '';
      create table if not exists sales (
        id text primary key,
        client_id text not null references clients(id),
        document_number text not null unique,
        status text not null check (status in ('pagado', 'credito', 'deuda')),
        manual_status boolean not null default true,
        payment_method text not null check (payment_method in ('efectivo', 'yape', 'transferencia')),
        notes text,
        created_at timestamptz not null default now()
      );
      create table if not exists sale_items (
        id text primary key,
        sale_id text not null references sales(id) on delete cascade,
        product_id text not null references products(id),
        name text not null,
        price numeric(12, 2) not null,
        cost_price numeric(12, 2) not null default 0,
        serials jsonb not null default '[]'::jsonb,
        qty integer not null check (qty > 0)
      );
      alter table sale_items add column if not exists cost_price numeric(12, 2) not null default 0;
      alter table sale_items add column if not exists serials jsonb not null default '[]'::jsonb;
      create table if not exists payments (
        id text primary key,
        sale_id text not null references sales(id),
        amount numeric(12, 2) not null check (amount > 0),
        method text not null check (method in ('efectivo', 'yape', 'transferencia')),
        item_ids jsonb not null default '[]'::jsonb,
        note text,
        created_at timestamptz not null default now()
      );
    `);
    const products = await db.query("select count(*)::int as count from products");
    if (products.rows[0].count === 0) {
      for (const product of initialData.products) {
        await db.query(
          "insert into products (id, name, sku, category, brand, model, price, cost_price, sale_price, stock, serials) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
          [product.id, product.name, product.sku, product.category || "GENERAL", product.brand || "", product.model || "", product.price, product.costPrice || 0, product.salePrice || product.price, product.stock, JSON.stringify(product.serials || [])]
        );
      }
    }
    return;
  }
  await fs.mkdir(DATA_DIR, { recursive: true });
  try {
    await fs.access(DATA_FILE);
  } catch {
    await fs.writeFile(DATA_FILE, JSON.stringify(initialData, null, 2));
  }
}

async function readDb() {
  await ensureData();
  if (process.env.DATABASE_URL) return readPgDb();
  const raw = await fs.readFile(DATA_FILE, "utf8");
  return JSON.parse(raw);
}

async function writeDb(db) {
  if (process.env.DATABASE_URL) {
    await writePgDb(db);
    return;
  }
  await fs.writeFile(DATA_FILE, JSON.stringify(db, null, 2));
}

async function getPgPool() {
  if (pgPool) return pgPool;
  const { Pool } = require("pg");
  pgPool = new Pool({
    connectionString: process.env.DATABASE_URL,
    ssl: process.env.NODE_ENV === "production" ? { rejectUnauthorized: false } : false
  });
  return pgPool;
}

async function readPgDb() {
  const db = await getPgPool();
  const [clients, products, sales, items, payments] = await Promise.all([
    db.query("select id, name, document_type, document_number, phone, created_at from clients order by created_at desc"),
    db.query("select id, name, sku, category, brand, model, price, cost_price, sale_price, stock, serials from products order by name asc"),
    db.query("select id, client_id, document_number, status, manual_status, payment_method, notes, created_at from sales order by created_at desc"),
    db.query("select id, sale_id, product_id, name, price, cost_price, qty, serials from sale_items order by id asc"),
    db.query("select id, sale_id, amount, method, item_ids, note, created_at from payments order by created_at desc")
  ]);

  return {
    clients: clients.rows.map((row) => ({
      id: row.id,
      name: row.name,
      documentType: row.document_type,
      documentNumber: row.document_number,
      phone: row.phone || "",
      createdAt: row.created_at
    })),
    products: products.rows.map((row) => ({
      id: row.id,
      name: row.name,
      sku: row.sku || "",
      category: row.category || "GENERAL",
      brand: row.brand || "",
      model: row.model || "",
      price: Number(row.price),
      costPrice: Number(row.cost_price || 0),
      salePrice: Number(row.sale_price || row.price),
      serials: Array.isArray(row.serials) ? row.serials : [],
      stock: Number(row.stock)
    })),
    sales: sales.rows.map((row) => ({
      id: row.id,
      clientId: row.client_id,
      documentNumber: row.document_number,
      items: items.rows.filter((item) => item.sale_id === row.id).map((item) => ({
        id: item.id,
        productId: item.product_id,
        name: item.name,
        price: Number(item.price),
        costPrice: Number(item.cost_price || 0),
        serials: Array.isArray(item.serials) ? item.serials : [],
        qty: Number(item.qty)
      })),
      status: row.status,
      manualStatus: row.manual_status,
      paymentMethod: row.payment_method,
      notes: row.notes || "",
      createdAt: row.created_at
    })),
    payments: payments.rows.map((row) => ({
      id: row.id,
      saleId: row.sale_id,
      amount: Number(row.amount),
      method: row.method,
      itemIds: Array.isArray(row.item_ids) ? row.item_ids : [],
      note: row.note || "",
      createdAt: row.created_at
    }))
  };
}

async function writePgDb(dbSnapshot) {
  const db = await getPgPool();
  const client = await db.connect();
  try {
    await client.query("begin");
    await client.query("delete from payments");
    await client.query("delete from sale_items");
    await client.query("delete from sales");
    await client.query("delete from products");
    await client.query("delete from clients");

    for (const item of dbSnapshot.clients) {
      await client.query(
        "insert into clients (id, name, document_type, document_number, phone, created_at) values ($1, $2, $3, $4, $5, $6)",
        [item.id, item.name, item.documentType, item.documentNumber, item.phone, item.createdAt]
      );
    }
    for (const item of dbSnapshot.products) {
      await client.query(
        "insert into products (id, name, sku, category, brand, model, price, cost_price, sale_price, stock, serials) values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)",
        [item.id, item.name, item.sku, item.category || "GENERAL", item.brand || "", item.model || "", item.price, item.costPrice || 0, item.salePrice || item.price, item.stock, JSON.stringify(item.serials || [])]
      );
    }
    for (const sale of dbSnapshot.sales) {
      await client.query(
        "insert into sales (id, client_id, document_number, status, manual_status, payment_method, notes, created_at) values ($1, $2, $3, $4, $5, $6, $7, $8)",
        [sale.id, sale.clientId, sale.documentNumber, sale.status, sale.manualStatus, sale.paymentMethod, sale.notes, sale.createdAt]
      );
      for (const item of sale.items) {
        await client.query(
          "insert into sale_items (id, sale_id, product_id, name, price, cost_price, qty, serials) values ($1, $2, $3, $4, $5, $6, $7, $8)",
          [item.id, sale.id, item.productId, item.name, item.price, item.costPrice || 0, item.qty, JSON.stringify(item.serials || [])]
        );
      }
    }
    for (const payment of dbSnapshot.payments) {
      await client.query(
        "insert into payments (id, sale_id, amount, method, item_ids, note, created_at) values ($1, $2, $3, $4, $5, $6, $7)",
        [payment.id, payment.saleId, payment.amount, payment.method, JSON.stringify(payment.itemIds || []), payment.note, payment.createdAt]
      );
    }
    await client.query("commit");
  } catch (error) {
    await client.query("rollback");
    throw error;
  } finally {
    client.release();
  }
}

function sendJson(res, status, payload) {
  res.writeHead(status, { "Content-Type": "application/json; charset=utf-8" });
  res.end(JSON.stringify(payload));
}

function parseBody(req) {
  return new Promise((resolve, reject) => {
    let body = "";
    req.on("data", (chunk) => {
      body += chunk;
      if (body.length > 1_000_000) {
        reject(new Error("Payload demasiado grande"));
        req.destroy();
      }
    });
    req.on("end", () => {
      if (!body) {
        resolve({});
        return;
      }
      try {
        resolve(JSON.parse(body));
      } catch {
        reject(new Error("JSON inválido"));
      }
    });
  });
}

function saleTotals(sale, payments) {
  const total = money(sale.items.reduce((sum, item) => sum + item.price * item.qty, 0));
  const paid = money(payments.filter((payment) => payment.saleId === sale.id).reduce((sum, payment) => sum + payment.amount, 0));
  const balance = money(Math.max(total - paid, 0));
  return { total, paid, balance };
}

function normalizeSaleStatus(sale, payments) {
  const { total, paid, balance } = saleTotals(sale, payments);
  if (sale.manualStatus) return sale.status;
  if (balance <= 0 && total > 0) return "pagado";
  if (paid > 0 && balance > 0) return "credito";
  return sale.status || "deuda";
}

function withSaleComputedFields(sale, db) {
  const totals = saleTotals(sale, db.payments);
  return {
    ...sale,
    ...totals,
    status: normalizeSaleStatus(sale, db.payments),
    client: db.clients.find((client) => client.id === sale.clientId) || null
  };
}

async function handleApi(req, res, pathname) {
  const db = await readDb();

  if (req.method === "GET" && pathname === "/api/health") {
    sendJson(res, 200, { ok: true });
    return;
  }

  if (req.method === "POST" && pathname === "/api/login") {
    const body = await parseBody(req);
    if (body.username === APP_USER && body.password === APP_PASSWORD) {
      sendJson(res, 200, { ok: true, token: APP_TOKEN, username: APP_USER });
      return;
    }
    sendJson(res, 401, { error: "Usuario o contraseña incorrectos." });
    return;
  }

  if (req.method === "GET" && pathname === "/api/clients") {
    sendJson(res, 200, db.clients);
    return;
  }

  if (req.method === "POST" && pathname === "/api/clients") {
    const body = await parseBody(req);
    if (!body.name || !body.documentNumber || !["dni", "ruc"].includes(body.documentType)) {
      sendJson(res, 400, { error: "Nombre, tipo y número de documento son obligatorios." });
      return;
    }
    const client = {
      id: id("client"),
      name: String(body.name).trim(),
      documentType: body.documentType,
      documentNumber: String(body.documentNumber).trim(),
      phone: String(body.phone || "").trim(),
      createdAt: new Date().toISOString()
    };
    db.clients.unshift(client);
    await writeDb(db);
    sendJson(res, 201, client);
    return;
  }

  if (req.method === "DELETE" && pathname.startsWith("/api/clients/")) {
    const clientId = pathname.split("/").pop();
    const hasSales = db.sales.some((sale) => sale.clientId === clientId);
    if (hasSales) {
      sendJson(res, 409, { error: "No se puede eliminar un cliente con ventas registradas." });
      return;
    }
    db.clients = db.clients.filter((client) => client.id !== clientId);
    await writeDb(db);
    sendJson(res, 200, { ok: true });
    return;
  }

  if (req.method === "GET" && pathname === "/api/products") {
    sendJson(res, 200, db.products);
    return;
  }

  if (req.method === "POST" && pathname === "/api/products") {
    const body = await parseBody(req);
    if (!body.name || Number(body.price) < 0) {
      sendJson(res, 400, { error: "Nombre y precio válido son obligatorios." });
      return;
    }
    const product = {
      id: id("prod"),
      name: String(body.name).trim(),
      sku: String(body.sku || "").trim(),
      category: String(body.category || "GENERAL").trim().toUpperCase(),
      brand: String(body.brand || "").trim(),
      model: String(body.model || "").trim(),
      price: money(body.salePrice ?? body.price),
      costPrice: money(body.costPrice),
      salePrice: money(body.salePrice ?? body.price),
      stock: Number(body.stock || 0),
      serials: String(body.serials || "")
        .split(/\r?\n|,/)
        .map((serial) => serial.trim())
        .filter(Boolean)
    };
    db.products.unshift(product);
    await writeDb(db);
    sendJson(res, 201, product);
    return;
  }

  if (req.method === "POST" && pathname.match(/^\/api\/products\/[^/]+\/adjust$/)) {
    const productId = pathname.split("/")[3];
    const body = await parseBody(req);
    const product = db.products.find((item) => item.id === productId);
    if (!product) {
      sendJson(res, 404, { error: "Producto no encontrado." });
      return;
    }
    product.stock = Math.max(Number(body.stock || 0), 0);
    await writeDb(db);
    sendJson(res, 200, product);
    return;
  }

  if (req.method === "POST" && pathname.match(/^\/api\/products\/[^/]+\/entries$/)) {
    const productId = pathname.split("/")[3];
    const body = await parseBody(req);
    const product = db.products.find((item) => item.id === productId);
    if (!product) {
      sendJson(res, 404, { error: "Producto no encontrado." });
      return;
    }
    const serials = String(body.serials || "")
      .split(/\r?\n|,/)
      .map((serial) => serial.trim())
      .filter(Boolean);
    const existing = new Set(product.serials || []);
    const uniqueNew = serials.filter((serial) => !existing.has(serial));
    product.costPrice = money(body.costPrice || product.costPrice || 0);
    product.salePrice = money(body.salePrice || product.salePrice || product.price || 0);
    product.price = product.salePrice;
    product.stock = Number(product.stock || 0) + uniqueNew.length;
    product.serials = [...(product.serials || []), ...uniqueNew];
    product.lastEntryAt = body.entryDate || new Date().toISOString();
    product.lastProvider = String(body.provider || "").trim();
    await writeDb(db);
    sendJson(res, 201, product);
    return;
  }

  if (req.method === "DELETE" && pathname.startsWith("/api/products/")) {
    const productId = pathname.split("/").pop();
    const hasSales = db.sales.some((sale) => sale.items.some((item) => item.productId === productId));
    if (hasSales) {
      sendJson(res, 409, { error: "No se puede eliminar un producto usado en ventas. Para pruebas, elimina ventas primero o crea un producto nuevo." });
      return;
    }
    db.products = db.products.filter((product) => product.id !== productId);
    await writeDb(db);
    sendJson(res, 200, { ok: true });
    return;
  }

  if (req.method === "GET" && pathname === "/api/sales") {
    sendJson(res, 200, db.sales.map((sale) => withSaleComputedFields(sale, db)));
    return;
  }

  if (req.method === "POST" && pathname === "/api/sales") {
    const body = await parseBody(req);
    const client = db.clients.find((item) => item.id === body.clientId);
    if (!client || !Array.isArray(body.items) || body.items.length === 0) {
      sendJson(res, 400, { error: "Selecciona cliente y al menos un producto." });
      return;
    }

    const items = body.items.map((item) => {
      const product = db.products.find((candidate) => candidate.id === item.productId);
      if (!product) throw new Error("Producto inválido.");
      const qty = Math.max(Number(item.qty || 1), 1);
      const serials = Array.isArray(item.serials) ? item.serials : [];
      product.stock = Math.max(Number(product.stock || 0) - qty, 0);
      product.serials = Array.isArray(product.serials)
        ? product.serials.filter((serial) => !serials.includes(serial))
        : [];
      return {
        id: id("item"),
        productId: product.id,
        name: product.name,
        price: money(item.price ?? product.salePrice ?? product.price),
        costPrice: money(item.costPrice ?? product.costPrice ?? 0),
        qty,
        serials
      };
    });

    const sale = {
      id: id("sale"),
      clientId: client.id,
      documentNumber: `V-${String(db.sales.length + 1).padStart(5, "0")}`,
      items,
      status: ["pagado", "credito", "deuda"].includes(body.status) ? body.status : "deuda",
      manualStatus: Boolean(body.manualStatus),
      paymentMethod: ["efectivo", "yape", "transferencia"].includes(body.paymentMethod) ? body.paymentMethod : "efectivo",
      notes: String(body.notes || "").trim(),
      createdAt: new Date().toISOString()
    };

    db.sales.unshift(sale);
    const total = saleTotals(sale, db.payments).total;
    const firstPayment = money(body.initialPayment || (sale.status === "pagado" ? total : 0));
    if (firstPayment > 0) {
      db.payments.unshift({
        id: id("pay"),
        saleId: sale.id,
        amount: Math.min(firstPayment, total),
        method: sale.paymentMethod,
        itemIds: [],
        note: "Pago inicial",
        createdAt: new Date().toISOString()
      });
    }
    await writeDb(db);
    sendJson(res, 201, withSaleComputedFields(sale, db));
    return;
  }

  if (req.method === "PATCH" && pathname.startsWith("/api/sales/")) {
    const saleId = pathname.split("/").pop();
    const body = await parseBody(req);
    const sale = db.sales.find((item) => item.id === saleId);
    if (!sale) {
      sendJson(res, 404, { error: "Venta no encontrada." });
      return;
    }
    if (["pagado", "credito", "deuda"].includes(body.status)) {
      sale.status = body.status;
      sale.manualStatus = true;
    }
    if (["efectivo", "yape", "transferencia"].includes(body.paymentMethod)) {
      sale.paymentMethod = body.paymentMethod;
    }
    await writeDb(db);
    sendJson(res, 200, withSaleComputedFields(sale, db));
    return;
  }

  if (req.method === "GET" && pathname === "/api/payments") {
    const payments = db.payments.map((payment) => {
      const sale = db.sales.find((item) => item.id === payment.saleId);
      const client = sale ? db.clients.find((item) => item.id === sale.clientId) : null;
      return { ...payment, saleDocument: sale?.documentNumber || "", clientName: client?.name || "" };
    });
    sendJson(res, 200, payments);
    return;
  }

  if (req.method === "POST" && pathname === "/api/payments") {
    const body = await parseBody(req);
    const sale = db.sales.find((item) => item.id === body.saleId);
    const amount = money(body.amount);
    if (!sale || amount <= 0) {
      sendJson(res, 400, { error: "Venta y monto válido son obligatorios." });
      return;
    }
    const totals = saleTotals(sale, db.payments);
    const payment = {
      id: id("pay"),
      saleId: sale.id,
      amount: Math.min(amount, totals.balance),
      method: ["efectivo", "yape", "transferencia"].includes(body.method) ? body.method : "efectivo",
      itemIds: Array.isArray(body.itemIds) ? body.itemIds : [],
      note: String(body.note || "").trim(),
      createdAt: new Date().toISOString()
    };
    if (payment.amount <= 0) {
      sendJson(res, 409, { error: "La venta ya está pagada." });
      return;
    }
    db.payments.unshift(payment);
    await writeDb(db);
    sendJson(res, 201, payment);
    return;
  }

  sendJson(res, 404, { error: "Ruta no encontrada." });
}

async function serveStatic(req, res, pathname) {
  if (pathname === "/") {
    sendJson(res, 200, {
      ok: true,
      name: "TecnoStor ERP API",
      message: "Servidor activo. Use el programa de escritorio para operar el ERP."
    });
    return;
  }
  const safePath = pathname === "/" ? "/index.html" : pathname;
  const filePath = path.normalize(path.join(PUBLIC_DIR, safePath));
  if (!filePath.startsWith(PUBLIC_DIR)) {
    res.writeHead(403);
    res.end("Forbidden");
    return;
  }
  try {
    const content = await fs.readFile(filePath);
    res.writeHead(200, { "Content-Type": mimeTypes[path.extname(filePath)] || "application/octet-stream" });
    res.end(content);
  } catch {
    res.writeHead(404);
    res.end("Not found");
  }
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${req.headers.host}`);
    if (url.pathname.startsWith("/api/")) {
      await handleApi(req, res, url.pathname);
      return;
    }
    await serveStatic(req, res, url.pathname);
  } catch (error) {
    sendJson(res, 500, { error: error.message || "Error interno" });
  }
});

server.listen(PORT, () => {
  console.log(`ERP simple listo en http://localhost:${PORT}`);
});
