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
  price numeric(12, 2) not null default 0,
  stock integer not null default 0
);

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
  qty integer not null check (qty > 0)
);

create table if not exists payments (
  id text primary key,
  sale_id text not null references sales(id),
  amount numeric(12, 2) not null check (amount > 0),
  method text not null check (method in ('efectivo', 'yape', 'transferencia')),
  item_ids jsonb not null default '[]'::jsonb,
  note text,
  created_at timestamptz not null default now()
);
