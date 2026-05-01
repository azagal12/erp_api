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
- Productos con precio costo, precio venta y series unicas para inventario.
- Balance de margen por documento y producto.
- Configuracion basica de plantilla para imprimir comprobante.

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
   - La API de Render queda configurada por defecto. Si necesitas cambiarla, usa `Configurar API` en el login.

Cuando la API esté publicada en Render, en el campo API se coloca la URL de Render. Así se puede usar desde cualquier PC y cualquier red, porque los documentos se guardan en PostgreSQL en la nube.

La interfaz principal del negocio es el programa Windows. Render solo queda como API/servidor en la nube para guardar clientes, productos, documentos de venta, estados de deuda y movimientos de caja.

En caja se pueden registrar pagos parciales de dos formas:

- Por monto libre, por ejemplo `3000`.
- Por cantidad de productos, por ejemplo 10 unidades de un producto de S/ 300 para calcular S/ 3000.

En Inicio se muestran los documentos con pendiente de pago. Con doble clic sobre un pendiente se puede registrar el pago al momento.

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
