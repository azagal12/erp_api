# ERP Simple

MVP sencillo para manejar clientes, productos, ventas y caja.

## Funciones

- Clientes con DNI o RUC, agregado manual y eliminación si no tienen ventas.
- Productos con auto-refresh cada 5 segundos.
- Ventas con estados editables:
  - verde: pagado
  - amarillo: crédito
  - rojo: deuda
- Métodos de pago: efectivo, Yape y transferencia.
- Caja con pagos iniciales y pagos parciales para créditos/deudas.
- Pagos asociados a una venta completa o a productos seleccionados.

## Ejecutar local

```bash
npm start
```

Luego abrir:

```text
http://localhost:3000
```

Los datos locales se guardan en `data/db.json`.

## Programa de escritorio en Visual Studio

El programa Windows está en `Desktop/`.

1. Abrir `TecnoStorERP.sln` con Visual Studio.
2. Ejecutar la API con `npm start`.
3. Iniciar el programa.
4. En login usar:
   - Usuario: `TecnoStor`
   - Contraseña: `258922`
   - API: `http://localhost:3000`

Cuando la API esté publicada en Render, en el campo API se coloca la URL de Render. Así se puede usar desde cualquier PC y cualquier red, porque los documentos se guardan en PostgreSQL en la nube.

## GitHub y Render

Este MVP corre localmente con archivo JSON y en Render con PostgreSQL.

Incluye `render.yaml` para crear el servicio web y la base PostgreSQL. También incluye `database.sql` con las tablas iniciales para clientes, productos, ventas, productos vendidos y pagos.

Si existe `DATABASE_URL`, el servidor usa PostgreSQL. Si no existe, guarda en `data/db.json`; si abres `public/index.html` directamente, la pantalla funciona con `localStorage`.

Variables importantes en Render:

```text
APP_USER=TecnoStor
APP_PASSWORD=258922
APP_TOKEN=cambiar-por-un-token-largo
DATABASE_URL=lo-coloca-render
```
