using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Drawing.Printing;

namespace TecnoStorERP;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LoginForm());
    }
}

public sealed class LoginForm : Form
{
    private const string DefaultApiUrl = "https://erp-api-7hd5.onrender.com";
    private readonly TextBox _apiUrl = Ui.Text(DefaultApiUrl, 340);
    private readonly TextBox _user = Ui.Text("TecnoStor", 260);
    private readonly TextBox _password = Ui.Text("258922", 260, true);
    private readonly FlowLayoutPanel _advanced = new()
    {
        AutoSize = true,
        Visible = false,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false
    };

    public LoginForm()
    {
        Text = "TecnoStor ERP - Ingreso";
        Width = 520;
        Height = 360;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Ui.Page;

        var card = Ui.Card();
        card.Width = 420;
        card.Height = 260;
        card.Anchor = AnchorStyles.None;
        card.Location = new Point((ClientSize.Width - card.Width) / 2, (ClientSize.Height - card.Height) / 2);

        var login = Ui.PrimaryButton("Ingresar", 340);
        login.Click += async (_, _) => await LoginAsync();
        var advanced = Ui.LinkButton("Configurar API");
        advanced.Click += (_, _) =>
        {
            _advanced.Visible = !_advanced.Visible;
            card.Height = _advanced.Visible ? 320 : 260;
            card.Location = new Point((ClientSize.Width - card.Width) / 2, (ClientSize.Height - card.Height) / 2);
        };

        _advanced.Controls.Add(Ui.Field("API", _apiUrl));

        card.Controls.Add(Ui.Stack(
            Ui.Title("TecnoStor ERP"),
            Ui.Subtitle("Programa de escritorio conectado a la nube"),
            Ui.Field("Usuario", _user),
            Ui.Field("Contraseña", _password),
            _advanced,
            advanced,
            login
        ));

        Controls.Add(card);
        Resize += (_, _) => card.Location = new Point((ClientSize.Width - card.Width) / 2, (ClientSize.Height - card.Height) / 2);
    }

    private async Task LoginAsync()
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(_apiUrl.Text) ? DefaultApiUrl : _apiUrl.Text.Trim();
            var api = new ApiClient(url);
            await api.LoginAsync(_user.Text.Trim(), _password.Text);
            Hide();
            new MainForm(api, url).ShowDialog();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se pudo ingresar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

public sealed class MainForm : Form
{
    private readonly ApiClient _api;
    private readonly string _apiUrl;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(22), BackColor = Ui.Page };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Ui.Muted };
    private readonly Dictionary<string, Button> _navButtons = [];
    private string _currentView = "inicio";

    private readonly DataGridView _clients = Ui.Grid();
    private readonly DataGridView _products = Ui.Grid();
    private readonly DataGridView _sales = Ui.Grid();
    private readonly DataGridView _payments = Ui.Grid();
    private readonly DataGridView _pending = Ui.Grid();
    private readonly DataGridView _balanceGrid = Ui.Grid();
    private readonly HashSet<DataGridView> _stateGrids = [];
    private readonly TextBox _companyName = Ui.Text("TU EMPRESA", 420);
    private readonly TextBox _companyRuc = Ui.Text("20611068701", 220);
    private readonly TextBox _companyAddress = Ui.Text("AV. INCA GARCILASO DE LA VEGA NRO. 1348 INT 2B 130-131 - LIMA", 520);
    private readonly TextBox _bankAccounts = Ui.MultiText("CUENTAS BANCARIAS", 520, 70);
    private readonly TextBox _receiptFooter = Ui.MultiText("UN AÑO DE GARANTIA DE CADA PRODUCTO Y 6 MESES PARA PERIFERICOS", 520, 90);

    private readonly ComboBox _saleClient = Ui.Combo(320);
    private readonly ComboBox _saleProduct = Ui.Combo(320);
    private readonly NumericUpDown _saleQty = Ui.Number(1, 999, 1);
    private readonly ComboBox _saleStatus = Ui.Combo(180, "pagado", "credito", "deuda");
    private readonly ComboBox _saleMethod = Ui.Combo(180, "efectivo", "yape", "transferencia");
    private readonly NumericUpDown _saleInitial = Ui.Money();
    private readonly ListBox _saleItems = new() { Width = 430, Height = 130, BorderStyle = BorderStyle.FixedSingle };

    private readonly ComboBox _paySale = Ui.Combo(500);
    private readonly CheckedListBox _payItems = new() { Width = 500, Height = 160, BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true };
    private readonly ComboBox _payProduct = Ui.Combo(320);
    private readonly NumericUpDown _payProductQty = Ui.Number(1, 999999, 1);
    private readonly NumericUpDown _payAmount = Ui.Money();
    private readonly ComboBox _payMethod = Ui.Combo(180, "efectivo", "yape", "transferencia");
    private readonly TextBox _payNote = Ui.Text("", 500);

    private List<Client> _clientData = [];
    private List<Product> _productData = [];
    private List<Sale> _saleData = [];
    private List<Payment> _paymentData = [];
    private readonly List<SaleLine> _newSaleItems = [];

    public MainForm(ApiClient api, string apiUrl)
    {
        _api = api;
        _apiUrl = apiUrl;
        Text = "TecnoStor ERP";
        Width = 1240;
        Height = 760;
        MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Ui.Page;

        Controls.Add(_content);
        Controls.Add(Header());
        Controls.Add(StatusBar());

        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) => await SafeRun(RefreshProductsAsync);
        timer.Start();

        Load += async (_, _) =>
        {
            Navigate("inicio");
            await SafeRun(LoadAllAsync);
        };
    }

    private Control Header()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 94, BackColor = Color.White, Padding = new Padding(24, 16, 24, 12) };
        var title = new Label
        {
            Text = "TecnoStor ERP",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 16)
        };

        var nav = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(24, 54)
        };

        nav.Controls.Add(NavButton("inicio", "Inicio"));
        nav.Controls.Add(NavButton("clientes", "Clientes"));
        nav.Controls.Add(NavButton("productos", "Productos"));
        nav.Controls.Add(NavButton("ventas", "Venta"));
        nav.Controls.Add(NavButton("caja", "Caja"));
        nav.Controls.Add(NavButton("balance", "Balance"));
        nav.Controls.Add(NavButton("configuracion", "Config."));

        var refresh = Ui.SecondaryButton("Refrescar", 120);
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Location = new Point(Width - 170, 30);
        refresh.Click += async (_, _) => await SafeRun(LoadAllAsync);
        header.Resize += (_, _) => refresh.Location = new Point(header.Width - 150, 30);

        header.Controls.Add(title);
        header.Controls.Add(nav);
        header.Controls.Add(refresh);
        return header;
    }

    private Control StatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Color.White, Padding = new Padding(18, 8, 18, 6) };
        _status.Text = $"Conectado a {_apiUrl}";
        bar.Controls.Add(_status);
        return bar;
    }

    private Button NavButton(string key, string text)
    {
        var button = new Button
        {
            Text = text,
            Width = 170,
            Height = 38,
            Margin = new Padding(0, 0, 18, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Ui.Ink,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Ui.Blue;
        button.FlatAppearance.BorderSize = 2;
        button.FlatAppearance.MouseOverBackColor = Ui.SoftBlue;
        button.FlatAppearance.MouseDownBackColor = Ui.Blue;
        button.Click += (_, _) => Navigate(key);
        _navButtons[key] = button;
        return button;
    }

    private void Navigate(string key)
    {
        _currentView = key;
        foreach (var item in _navButtons)
        {
            item.Value.BackColor = item.Key == key ? Ui.Blue : Color.White;
            item.Value.ForeColor = item.Key == key ? Color.White : Ui.Ink;
        }

        _content.Controls.Clear();
        _content.Controls.Add(key switch
        {
            "inicio" => HomeView(),
            "clientes" => ClientsView(),
            "productos" => ProductsView(),
            "ventas" => SalesView(),
            "caja" => CashView(),
            "balance" => BalanceView(),
            "configuracion" => ConfigView(),
            _ => HomeView()
        });
    }

    private Control HomeView()
    {
        var pendingSales = _saleData.Where(s => s.Balance > 0).ToList();
        _pending.DataSource = null;
        _pending.DataSource = pendingSales;
        HideColumns(_pending, "Id", "ClientId", "Client", "Items");
        Header(_pending, ("DocumentNumber", "Documento"), ("Cliente", "Cliente"), ("Total", "Total"), ("Paid", "Pagado"), ("Balance", "Pendiente pago"), ("Status", "Estado"), ("PaymentMethod", "Metodo"));
        MakeReadOnly(_pending);
        ColorGridBySale(_pending);
        MarkStateGrid(_pending);
        _pending.CellDoubleClick -= PendingDoubleClick;
        _pending.CellDoubleClick += PendingDoubleClick;

        return Ui.FullPanel("Inicio",
            Ui.StateLegend(),
            Ui.SummaryGrid(
                ("Pendiente", pendingSales.Sum(s => s.Balance)),
                ("Clientes", pendingSales.Select(s => s.Cliente).Distinct().Count()),
                ("Pagadas", _saleData.Count(s => s.Balance <= 0)),
                ("Caja", _paymentData.Sum(p => p.Monto))
            ),
            Ui.TablePanel("Pendientes de pago al ingresar", _pending)
        );
    }

    private Control ClientsView()
    {
        var name = Ui.Text("", 330);
        var docType = Ui.Combo(160, "dni", "ruc");
        var doc = Ui.Text("", 210);
        var phone = Ui.Text("", 210);
        var add = Ui.PrimaryButton("Agregar cliente", 330);
        var delete = Ui.DangerButton("Eliminar seleccionado", 330);
        _clients.CellDoubleClick -= ClientDoubleClick;
        _clients.CellDoubleClick += ClientDoubleClick;

        add.Click += async (_, _) =>
        {
            await SafeRun(async () =>
            {
                await _api.AddClientAsync(new ClientInput(name.Text, docType.Text, doc.Text, phone.Text));
                name.Clear(); doc.Clear(); phone.Clear();
                await LoadAllAsync();
            });
        };

        delete.Click += async (_, _) =>
        {
            if (_clients.CurrentRow?.DataBoundItem is not Client client) return;
            if (MessageBox.Show($"Eliminar cliente {client.Name}?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await SafeRun(async () =>
            {
                await _api.DeleteClientAsync(client.Id);
                await LoadAllAsync();
            });
        };

        return Workspace(
            Ui.FormPanel("Clientes", "Registro manual con DNI o RUC",
                Ui.Field("Nombre / razon social", name),
                Ui.Two(Ui.Field("Tipo", docType), Ui.Field("Numero", doc)),
                Ui.Field("Telefono", phone),
                add,
                delete
            ),
            Ui.TablePanel("Clientes registrados", _clients)
        );
    }

    private Control ProductsView()
    {
        var name = Ui.Text("", 330);
        var sku = Ui.Text("", 160);
        var cost = Ui.Money();
        var sale = Ui.Money();
        var stock = Ui.Number(0, 999999, 0);
        var serials = Ui.MultiText("", 500, 140);
        var add = Ui.PrimaryButton("Agregar producto", 330);

        add.Click += async (_, _) =>
        {
            await SafeRun(async () =>
            {
                await _api.AddProductAsync(new ProductInput(name.Text, sku.Text, sale.Value, (int)stock.Value, cost.Value, sale.Value, serials.Text));
                name.Clear(); sku.Clear(); cost.Value = 0; sale.Value = 0; stock.Value = 0; serials.Clear();
                await RefreshProductsAsync();
            });
        };

        return Workspace(
            Ui.FormPanel("Productos", "Inventario con costo, precio de venta y series unicas",
                Ui.Field("Producto", name),
                Ui.Two(Ui.Field("SKU / codigo", sku), Ui.Field("Stock", stock)),
                Ui.Two(Ui.Field("Precio costo", cost), Ui.Field("Precio venta", sale)),
                Ui.Field("Series unicas (una por linea)", serials),
                add
            ),
            Ui.TablePanel("Lista de productos", _products)
        );
    }

    private Control SalesView()
    {
        var addLine = Ui.SecondaryButton("Agregar producto a venta", 320);
        var removeLine = Ui.SecondaryButton("Quitar producto seleccionado", 320);
        var save = Ui.PrimaryButton("Guardar venta", 320);
        var print = Ui.SecondaryButton("Imprimir comprobante", 320);

        addLine.Click += (_, _) =>
        {
            if (_saleProduct.SelectedItem is not Product product) return;
            _newSaleItems.Add(new SaleLine(product.Id, product.Name, product.EffectiveSalePrice, (int)_saleQty.Value, product.CostPrice, product.NextSeries((int)_saleQty.Value)));
            RenderSaleLines();
        };

        removeLine.Click += (_, _) =>
        {
            if (_saleItems.SelectedIndex < 0) return;
            _newSaleItems.RemoveAt(_saleItems.SelectedIndex);
            RenderSaleLines();
        };

        save.Click += async (_, _) =>
        {
            if (_saleClient.SelectedItem is not Client client)
            {
                MessageBox.Show("Agrega o selecciona un cliente.", "Venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_newSaleItems.Count == 0)
            {
                MessageBox.Show("Agrega al menos un producto.", "Venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await SafeRun(async () =>
            {
                await _api.AddSaleAsync(new SaleInput(client.Id, _newSaleItems.ToList(), _saleStatus.Text, true, _saleMethod.Text, _saleInitial.Value, ""));
                _newSaleItems.Clear();
                _saleInitial.Value = 0;
                RenderSaleLines();
                await LoadAllAsync();
            });
        };

        _sales.CellEndEdit -= SalesCellEndEdit;
        _sales.CellEndEdit += SalesCellEndEdit;
        print.Click += (_, _) =>
        {
            if (_sales.CurrentRow?.DataBoundItem is Sale sale) PrintReceipt(sale);
        };

        return Workspace(
            Ui.FormPanel("Venta", "Estados: verde pagado, amarillo credito, rojo deuda",
                Ui.StateLegend(),
                Ui.Field("Cliente", _saleClient),
                Ui.Field("Producto", _saleProduct),
                Ui.Two(Ui.Field("Cantidad", _saleQty), Ui.Field("Pago inicial", _saleInitial)),
                addLine,
                removeLine,
                Ui.Field("Productos de la venta", _saleItems),
                Ui.Two(Ui.Field("Estado", _saleStatus), Ui.Field("Metodo", _saleMethod)),
                save,
                print
            ),
            Ui.TablePanel("Documentos de venta", _sales)
        );
    }

    private Control CashView()
    {
        _paySale.SelectedIndexChanged -= PaymentSaleChanged;
        _paySale.SelectedIndexChanged += PaymentSaleChanged;
        _payProduct.SelectedIndexChanged -= PaymentProductChanged;
        _payProduct.SelectedIndexChanged += PaymentProductChanged;

        var useProductQty = Ui.SecondaryButton("Calcular por cantidad", 330);
        useProductQty.Click += (_, _) =>
        {
            if (_payProduct.SelectedItem is not SaleItem item) return;
            var qty = Math.Min(_payProductQty.Value, item.Qty);
            _payAmount.Value = Math.Min(item.Price * qty, _payAmount.Maximum);
            _payNote.Text = $"Pago por {qty:0} unidad(es) de {item.Name}";
            CheckOnlyItem(item.Id);
        };

        var add = Ui.PrimaryButton("Subir pago a caja", 330);
        add.Click += async (_, _) =>
        {
            if (_paySale.SelectedItem is not Sale sale)
            {
                MessageBox.Show("No hay venta pendiente seleccionada.", "Caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var items = _payItems.CheckedItems.Cast<SaleItem>().Select(item => item.Id).ToList();
            await SafeRun(async () =>
            {
                await _api.AddPaymentAsync(new PaymentInput(sale.Id, _payAmount.Value, _payMethod.Text, items, _payNote.Text));
                _payAmount.Value = 0;
                _payNote.Clear();
                await LoadAllAsync();
            });
        };

        var summary = CashSummary();
        return Workspace(
            Ui.FormPanel("Caja", "Pagos parciales por monto o productos seleccionados",
                summary,
                Ui.Field("Venta credito/deuda", _paySale),
                Ui.Two(Ui.Field("Producto", _payProduct), Ui.Field("Cantidad", _payProductQty)),
                useProductQty,
                Ui.Field("Productos abonados", _payItems),
                Ui.Two(Ui.Field("Monto", _payAmount), Ui.Field("Metodo", _payMethod)),
                Ui.Field("Detalle", _payNote),
                add
            ),
            Ui.TablePanel("Movimientos de caja", _payments)
        );
    }

    private Control BalanceView()
    {
        var rows = _saleData.SelectMany(sale => sale.Items.Select(item => new MarginRow
        {
            Documento = sale.DocumentNumber,
            Cliente = sale.Cliente,
            Producto = item.Name,
            Cantidad = item.Qty,
            Costo = item.CostPrice,
            Venta = item.Price,
            TotalCosto = item.CostPrice * item.Qty,
            TotalVenta = item.Price * item.Qty,
            Margen = (item.Price - item.CostPrice) * item.Qty,
            Estado = Ui.StateText(sale.Status)
        })).ToList();

        _balanceGrid.DataSource = null;
        _balanceGrid.DataSource = rows;
        MakeReadOnly(_balanceGrid);

        return Ui.FullPanel("Balance",
            Ui.SummaryGrid(
                ("Ventas", rows.Sum(r => r.TotalVenta)),
                ("Costos", rows.Sum(r => r.TotalCosto)),
                ("Margen", rows.Sum(r => r.Margen)),
                ("Pendiente", _saleData.Sum(s => s.Balance))
            ),
            Ui.TablePanel("Margen por documento y producto", _balanceGrid)
        );
    }

    private Control ConfigView()
    {
        return Ui.FullPanel("Configuracion de comprobante",
            Ui.FormPanel("Plantilla", "Datos que se imprimen en nota, boleta o proforma",
                Ui.Field("Nombre comercial", _companyName),
                Ui.Field("RUC", _companyRuc),
                Ui.Field("Direccion", _companyAddress),
                Ui.Field("Cuentas bancarias", _bankAccounts),
                Ui.Field("Texto inferior / garantia", _receiptFooter)
            )
        );
    }

    private Control CashSummary()
    {
        var total = _paymentData.Sum(p => p.Monto);
        var efectivo = _paymentData.Where(p => p.Metodo == "efectivo").Sum(p => p.Monto);
        var yape = _paymentData.Where(p => p.Metodo == "yape").Sum(p => p.Monto);
        var transferencia = _paymentData.Where(p => p.Metodo == "transferencia").Sum(p => p.Monto);

        return Ui.SummaryGrid(
            ("Total", total),
            ("Efectivo", efectivo),
            ("Yape", yape),
            ("Transferencia", transferencia)
        );
    }

    private async void SalesCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _sales.Rows[e.RowIndex].DataBoundItem is not Sale sale) return;
        await SafeRun(async () =>
        {
            await _api.UpdateSaleAsync(sale.Id, sale.Status, sale.PaymentMethod);
            await LoadAllAsync();
        });
    }

    private void PaymentSaleChanged(object? sender, EventArgs e) => RenderPaymentItems();
    private void PaymentProductChanged(object? sender, EventArgs e)
    {
        if (_payProduct.SelectedItem is not SaleItem item) return;
        _payProductQty.Maximum = Math.Max(item.Qty, 1);
        if (_payProductQty.Value > _payProductQty.Maximum) _payProductQty.Value = _payProductQty.Maximum;
    }

    private void PendingDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _pending.Rows[e.RowIndex].DataBoundItem is not Sale sale) return;
        ShowQuickPayment(sale);
    }

    private void ShowQuickPayment(Sale sale)
    {
        var amount = Ui.Money();
        amount.Maximum = Math.Max(sale.Balance, 1);
        amount.Value = sale.Balance > 0 ? sale.Balance : 0;
        var method = Ui.Combo(220, "efectivo", "yape", "transferencia");
        var note = Ui.Text($"Pago de {sale.DocumentNumber}", 420);
        var save = Ui.PrimaryButton("Registrar pago", 420);

        var form = new Form
        {
            Text = $"Registrar pago - {sale.DocumentNumber}",
            Width = 560,
            Height = 470,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Ui.Page,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        save.Click += async (_, _) =>
        {
            await SafeRun(async () =>
            {
                await _api.AddPaymentAsync(new PaymentInput(sale.Id, amount.Value, method.Text, [], note.Text));
                await LoadAllAsync();
            });
            form.Close();
        };

        form.Controls.Add(Ui.FullPanel(
            $"Pago pendiente: {sale.Cliente}",
            Ui.SummaryGrid(
                ("Total", sale.Total),
                ("Pagado", sale.Paid),
                ("Pendiente pago", sale.Balance),
                ("Caja", _paymentData.Sum(p => p.Monto))
            ),
            Ui.FormPanel(sale.DocumentNumber, "Ingresa el pago que entrega el cliente ahora",
                Ui.Field("Monto recibido", amount),
                Ui.Field("Metodo de pago", method),
                Ui.Field("Detalle", note),
                save
            )
        ));

        form.ShowDialog(this);
    }

    private void PrintReceipt(Sale sale)
    {
        var document = new PrintDocument();
        document.DocumentName = sale.DocumentNumber;
        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null) return;
            DrawReceipt(e.Graphics, sale);
        };

        using var preview = new PrintPreviewDialog
        {
            Document = document,
            Width = 1000,
            Height = 760
        };
        preview.ShowDialog(this);
    }

    private void DrawReceipt(Graphics g, Sale sale)
    {
        using var titleFont = new Font("Arial", 14, FontStyle.Bold);
        using var bold = new Font("Arial", 9, FontStyle.Bold);
        using var font = new Font("Arial", 8);
        using var small = new Font("Arial", 7);
        var black = Brushes.Black;
        var pen = Pens.Black;
        var y = 45;

        g.DrawString(_companyName.Text, titleFont, black, 110, y);
        g.DrawString(_companyAddress.Text, small, black, 110, y + 42);
        g.DrawRectangle(pen, 560, y - 10, 250, 110);
        g.DrawString($"RUC {_companyRuc.Text}", font, black, 625, y + 10);
        g.DrawString("NOTA DE VENTA", titleFont, black, 610, y + 42);
        g.DrawString(sale.DocumentNumber, bold, black, 635, y + 80);

        y += 145;
        g.DrawString("DOCUMENTO", bold, black, 30, y);
        g.DrawString(sale.Client?.DocumentNumber ?? "", font, black, 150, y);
        g.DrawString("FECHA EMISION", bold, black, 560, y);
        g.DrawString(DateTime.Now.ToString("dd/MM/yyyy"), font, black, 710, y);
        y += 24;
        g.DrawString("CLIENTE", bold, black, 30, y);
        g.DrawString(sale.Cliente, font, black, 150, y);
        g.DrawString("CONDICION", bold, black, 560, y);
        g.DrawString(Ui.StateText(sale.Status), font, black, 710, y);
        y += 24;
        g.DrawString("MONEDA", bold, black, 560, y);
        g.DrawString("SOLES", font, black, 710, y);

        y += 45;
        g.FillRectangle(Brushes.Black, 30, y, 780, 24);
        g.DrawString("N", bold, Brushes.White, 38, y + 5);
        g.DrawString("CODIGO", bold, Brushes.White, 110, y + 5);
        g.DrawString("DESCRIPCION / SERIE", bold, Brushes.White, 210, y + 5);
        g.DrawString("CANT.", bold, Brushes.White, 610, y + 5);
        g.DrawString("P.UNIT.", bold, Brushes.White, 690, y + 5);
        g.DrawString("TOTAL", bold, Brushes.White, 760, y + 5);
        y += 24;

        var n = 1;
        foreach (var item in sale.Items)
        {
            var total = item.Price * item.Qty;
            g.DrawRectangle(pen, 30, y, 780, 46);
            g.DrawString(n.ToString(), font, black, 38, y + 8);
            g.DrawString(item.ProductId, font, black, 110, y + 8);
            g.DrawString(item.Name, font, black, 210, y + 8);
            g.DrawString(string.Join(", ", item.Serials), small, black, 210, y + 24);
            g.DrawString(item.Qty.ToString("0"), font, black, 620, y + 8);
            g.DrawString(item.Price.ToString("0.00"), font, black, 690, y + 8);
            g.DrawString(total.ToString("0.00"), font, black, 760, y + 8);
            y += 46;
            n++;
        }

        y += 20;
        g.DrawString("GRAVADO", bold, black, 610, y);
        g.DrawString((sale.Total / 1.18m).ToString("0.00"), font, black, 740, y);
        y += 22;
        g.DrawString("I.G.V. 18%", bold, black, 610, y);
        g.DrawString((sale.Total - sale.Total / 1.18m).ToString("0.00"), font, black, 740, y);
        y += 22;
        g.DrawString("TOTAL", bold, black, 610, y);
        g.DrawString(sale.Total.ToString("0.00"), bold, black, 740, y);
        y += 38;
        g.DrawString($"PAGADO: S/ {sale.Paid:0.00}   PENDIENTE PAGO: S/ {sale.Balance:0.00}", bold, black, 30, y);
        y += 40;
        g.DrawString(_bankAccounts.Text, small, black, 30, y);
        y += 80;
        g.DrawString(_receiptFooter.Text, small, black, 160, y);
    }

    private async Task LoadAllAsync()
    {
        _clientData = await _api.GetClientsAsync();
        _productData = await _api.GetProductsAsync();
        _saleData = await _api.GetSalesAsync();
        _paymentData = await _api.GetPaymentsAsync();

        BindClients();
        BindProducts();
        BindSales();
        BindPayments();
        RenderPaymentItems();
        _status.Text = $"Conectado a {_apiUrl} | Clientes {_clientData.Count} | Productos {_productData.Count} | Ventas {_saleData.Count}";
        if (_currentView == "inicio") Navigate("inicio");
    }

    private async Task RefreshProductsAsync()
    {
        _productData = await _api.GetProductsAsync();
        BindProducts();
        _status.Text = $"Productos actualizados {DateTime.Now:HH:mm:ss}";
    }

    private void BindClients()
    {
        _clients.DataSource = null;
        _clients.DataSource = _clientData;
        HideColumns(_clients, "Id");
        Header(_clients, ("Name", "Cliente"), ("DocumentType", "Tipo"), ("DocumentNumber", "Documento"), ("Phone", "Telefono"));
        MakeReadOnly(_clients);
        _saleClient.DataSource = _clientData.ToList();
    }

    private void BindProducts()
    {
        _products.DataSource = null;
        _products.DataSource = _productData;
        HideColumns(_products, "Id", "Price", "Serials", "EffectiveSalePrice");
        Header(_products, ("Name", "Producto"), ("Sku", "Codigo"), ("CostPrice", "Costo"), ("SalePrice", "Precio venta"), ("Stock", "Stock"), ("SerialsText", "Series disponibles"));
        MakeReadOnly(_products);
        _saleProduct.DataSource = _productData.ToList();
    }

    private void BindSales()
    {
        _sales.DataSource = null;
        _sales.DataSource = _saleData;
        HideColumns(_sales, "Id", "ClientId", "Client", "Items");
        Header(_sales, ("DocumentNumber", "Documento"), ("Cliente", "Cliente"), ("Total", "Total"), ("Paid", "Pagado"), ("Balance", "Pendiente pago"), ("Status", "Estado"), ("PaymentMethod", "Metodo"));
        MakeReadOnly(_sales, "Status", "PaymentMethod");
        ColorSales();
        MarkStateGrid(_sales);
        _paySale.DataSource = _saleData.Where(s => s.Balance > 0).ToList();
    }

    private void BindPayments()
    {
        _payments.DataSource = null;
        _payments.DataSource = _paymentData;
        Header(_payments, ("Documento", "Documento"), ("Cliente", "Cliente"), ("Monto", "Monto"), ("Metodo", "Metodo"), ("Detalle", "Detalle"), ("Fecha", "Fecha"));
        MakeReadOnly(_payments);
    }

    private void RenderSaleLines()
    {
        _saleItems.DataSource = null;
        _saleItems.DataSource = _newSaleItems.Select(item => $"{item.Name} x {item.Qty} - S/ {(item.Price * item.Qty):0.00} | Series: {string.Join(", ", item.Serials)}").ToList();
    }

    private void RenderPaymentItems()
    {
        _payItems.Items.Clear();
        _payProduct.DataSource = null;
        if (_paySale.SelectedItem is not Sale sale) return;
        foreach (var item in sale.Items) _payItems.Items.Add(item);
        _payProduct.DataSource = sale.Items.ToList();
        if (sale.Items.Count > 0)
        {
            _payProductQty.Maximum = sale.Items.Max(item => Math.Max(item.Qty, 1));
            _payProductQty.Value = 1;
        }
    }

    private void CheckOnlyItem(string itemId)
    {
        for (var index = 0; index < _payItems.Items.Count; index++)
        {
            var item = (SaleItem)_payItems.Items[index];
            _payItems.SetItemChecked(index, item.Id == itemId);
        }
    }

    private void ColorSales()
    {
        ColorGridBySale(_sales);
    }

    private static void ColorGridBySale(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is not Sale sale) continue;
            var colors = Ui.StateColors(sale.Status);
            row.DefaultCellStyle.BackColor = sale.Status switch
            {
                "pagado" => Color.FromArgb(240, 253, 244),
                "credito" => Color.FromArgb(255, 251, 235),
                "deuda" => Color.FromArgb(254, 242, 242),
                _ => Color.White
            };
            row.DefaultCellStyle.ForeColor = Color.FromArgb(24, 32, 42);
            if (grid.Columns.Contains("Status"))
            {
                row.Cells["Status"].Style.BackColor = colors.background;
                row.Cells["Status"].Style.ForeColor = colors.foreground;
                row.Cells["Status"].Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
            if (grid.Columns.Contains("Balance"))
            {
                row.Cells["Balance"].Style.BackColor = colors.background;
                row.Cells["Balance"].Style.ForeColor = colors.foreground;
                row.Cells["Balance"].Style.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            }
        }
    }

    private void ClientDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _clients.Rows[e.RowIndex].DataBoundItem is not Client client) return;
        ShowClientSales(client);
    }

    private void ShowClientSales(Client client)
    {
        var sales = _saleData.Where(s => s.ClientId == client.Id).ToList();
        var grid = Ui.Grid();
        grid.DataSource = sales;
        HideColumns(grid, "Id", "ClientId", "Client", "Items");
        Header(grid, ("DocumentNumber", "Documento"), ("Cliente", "Cliente"), ("Total", "Total"), ("Paid", "Pagado"), ("Balance", "Pendiente pago"), ("Status", "Estado"), ("PaymentMethod", "Metodo"));
        MakeReadOnly(grid);
        ColorGridBySale(grid);
        MarkStateGrid(grid);

        var form = new Form
        {
            Text = $"Notas de venta - {client.Name}",
            Width = 1000,
            Height = 640,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Ui.Page
        };

        form.Controls.Add(Ui.FullPanel(
            $"{client.Name} - {client.DocumentType.ToUpper()} {client.DocumentNumber}",
            Ui.StateLegend(),
            Ui.SummaryGrid(
                ("Vendido", sales.Sum(s => s.Total)),
                ("Pagado", sales.Sum(s => s.Paid)),
                ("Pendiente", sales.Sum(s => s.Balance)),
                ("Docs", sales.Count)
            ),
            Ui.TablePanel("Notas de venta y estado de pago", grid)
        ));
        form.ShowDialog(this);
    }

    private async Task SafeRun(Func<Task> action)
    {
        try
        {
            UseWaitCursor = true;
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(CleanError(ex.Message), "TecnoStor ERP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private static string CleanError(string message) => message.Replace("{\"error\":\"", "").Replace("\"}", "");

    private static Control Workspace(Control left, Control right)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Ui.Page,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.Margin = new Padding(0, 0, 18, 0);
        right.Margin = new Padding(0);
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        return layout;
    }

    private static void HideColumns(DataGridView grid, params string[] columns)
    {
        foreach (var name in columns)
        {
            if (grid.Columns.Contains(name)) grid.Columns[name].Visible = false;
        }
    }

    private static void Header(DataGridView grid, params (string column, string text)[] headers)
    {
        foreach (var item in headers)
        {
            if (grid.Columns.Contains(item.column)) grid.Columns[item.column].HeaderText = item.text;
        }
    }

    private static void MakeReadOnly(DataGridView grid, params string[] editable)
    {
        var editableSet = editable.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.ReadOnly = !editableSet.Contains(column.Name);
        }
    }

    private void MarkStateGrid(DataGridView grid)
    {
        if (_stateGrids.Add(grid))
        {
            grid.CellFormatting += StateGridCellFormatting;
        }
    }

    private static void StateGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0) return;
        var columnName = grid.Columns[e.ColumnIndex].Name;
        if (grid.Rows[e.RowIndex].DataBoundItem is not Sale sale) return;

        if (columnName is "Status" or "Balance")
        {
            var colors = Ui.StateColors(sale.Status);
            e.CellStyle.BackColor = colors.background;
            e.CellStyle.ForeColor = colors.foreground;
            e.CellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            if (columnName == "Status" && e.Value is string status) e.Value = Ui.StateText(status);
        }
    }
}

public static class Ui
{
    public static readonly Color Page = Color.FromArgb(242, 245, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color Line = Color.FromArgb(210, 218, 229);
    public static readonly Color Ink = Color.FromArgb(24, 32, 42);
    public static readonly Color Muted = Color.FromArgb(95, 104, 119);
    public static readonly Color Blue = Color.FromArgb(34, 87, 197);
    public static readonly Color SoftBlue = Color.FromArgb(228, 235, 252);
    public static readonly Color Red = Color.FromArgb(180, 35, 24);
    public static readonly Color Paid = Color.FromArgb(213, 247, 222);
    public static readonly Color PaidText = Color.FromArgb(22, 101, 52);
    public static readonly Color Credit = Color.FromArgb(255, 242, 184);
    public static readonly Color CreditText = Color.FromArgb(133, 77, 14);
    public static readonly Color Debt = Color.FromArgb(255, 218, 218);
    public static readonly Color DebtText = Color.FromArgb(153, 27, 27);

    public static TextBox Text(string value, int width, bool password = false) => new()
    {
        Text = value,
        Width = width,
        Height = 34,
        BorderStyle = BorderStyle.FixedSingle,
        PasswordChar = password ? '*' : '\0'
    };

    public static TextBox MultiText(string value, int width, int height) => new()
    {
        Text = value,
        Width = width,
        Height = height,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.FixedSingle
    };

    public static ComboBox Combo(int width, params string[] items)
    {
        var combo = new ComboBox { Width = width, Height = 34, DropDownStyle = ComboBoxStyle.DropDownList };
        if (items.Length > 0) combo.DataSource = items.ToList();
        return combo;
    }

    public static NumericUpDown Number(decimal min, decimal max, decimal value) => new()
    {
        Width = 150,
        Height = 34,
        Minimum = min,
        Maximum = max,
        Value = value
    };

    public static NumericUpDown Money() => new()
    {
        Width = 150,
        Height = 34,
        DecimalPlaces = 2,
        Maximum = 9999999,
        Increment = 1
    };

    public static Button PrimaryButton(string text, int width) => Button(text, width, Blue, Color.White);
    public static Button SecondaryButton(string text, int width) => Button(text, width, Color.FromArgb(232, 237, 248), Ink);
    public static Button DangerButton(string text, int width) => Button(text, width, Red, Color.White);
    public static Button LinkButton(string text)
    {
        var button = Button(text, 340, Color.White, Blue);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Line;
        return button;
    }

    public static Button Button(string text, int width, Color background, Color foreground)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 38,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Lighten(background);
        button.FlatAppearance.MouseDownBackColor = Darken(background);
        return button;
    }

    public static Color Lighten(Color color) => Color.FromArgb(
        Math.Min(255, color.R + 18),
        Math.Min(255, color.G + 18),
        Math.Min(255, color.B + 18)
    );

    public static Color Darken(Color color) => Color.FromArgb(
        Math.Max(0, color.R - 24),
        Math.Max(0, color.G - 24),
        Math.Max(0, color.B - 24)
    );

    public static (Color background, Color foreground) StateColors(string status) => status switch
    {
        "pagado" => (Paid, PaidText),
        "credito" => (Credit, CreditText),
        "deuda" => (Debt, DebtText),
        _ => (Color.White, Ink)
    };

    public static string StateText(string status) => status switch
    {
        "pagado" => "PAGADO",
        "credito" => "CREDITO",
        "deuda" => "DEUDA",
        _ => status.ToUpperInvariant()
    };

    public static Panel Card()
    {
        var panel = new Panel
        {
            BackColor = Surface,
            Padding = new Padding(24),
            BorderStyle = BorderStyle.FixedSingle
        };
        return panel;
    }

    public static Label Title(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 18, FontStyle.Bold),
        ForeColor = Ink,
        AutoSize = true
    };

    public static Label Subtitle(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 9),
        ForeColor = Muted,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 10)
    };

    public static Control Field(string label, Control input)
    {
        var box = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        box.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Ink, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        box.Controls.Add(input);
        return box;
    }

    public static FlowLayoutPanel Two(Control left, Control right)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2)
        };
        left.Margin = new Padding(0, 0, 16, 0);
        panel.Controls.Add(left);
        panel.Controls.Add(right);
        return panel;
    }

    public static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = true,
            WrapContents = false
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    public static Control FormPanel(string title, string subtitle, params Control[] controls)
    {
        var panel = Card();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(26);
        var allControls = new List<Control> { Title(title), Subtitle(subtitle) };
        allControls.AddRange(controls);
        var stack = Stack(allControls.ToArray());
        panel.Controls.Add(stack);
        return panel;
    }

    public static Control TablePanel(string title, DataGridView grid)
    {
        var panel = Card();
        panel.Dock = DockStyle.Fill;
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Ink
        };
        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid);
        panel.Controls.Add(label);
        return panel;
    }

    public static Control FullPanel(string title, params Control[] controls)
    {
        var panel = Card();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(26);
        var label = Title(title);
        label.Dock = DockStyle.Top;
        label.Height = 42;

        if (controls.Length == 0)
        {
            panel.Controls.Add(label);
            return panel;
        }

        var fill = controls[^1];
        fill.Dock = DockStyle.Fill;
        panel.Controls.Add(fill);

        for (var index = controls.Length - 2; index >= 0; index--)
        {
            var control = controls[index];
            control.Dock = DockStyle.Top;
            control.Height = Math.Max(control.Height, 100);
            control.Margin = new Padding(0, 0, 0, 12);
            panel.Controls.Add(control);
        }
        panel.Controls.Add(label);
        return panel;
    }

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EnableHeadersVisualStyles = false,
            GridColor = Color.FromArgb(225, 231, 240),
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(250, 252, 255) }
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 251);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 226, 249);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        grid.RowTemplate.Height = 34;
        return grid;
    }

    public static Control SummaryGrid(params (string label, decimal value)[] items)
    {
        var panel = new TableLayoutPanel
        {
            Width = 500,
            Height = 92,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        foreach (var item in items)
        {
            var label = new Label
            {
                Text = $"{item.label}\nS/ {item.value:0.00}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(245, 247, 251),
                ForeColor = Ink,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            panel.Controls.Add(label);
        }
        return panel;
    }

    public static Control StateLegend()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.Controls.Add(StateBadge("Pagado", Paid, PaidText));
        panel.Controls.Add(StateBadge("Credito", Credit, CreditText));
        panel.Controls.Add(StateBadge("Deuda", Debt, DebtText));
        return panel;
    }

    private static Label StateBadge(string text, Color back, Color fore) => new()
    {
        Text = text,
        AutoSize = false,
        Width = 100,
        Height = 28,
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = back,
        ForeColor = fore,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Margin = new Padding(0, 0, 10, 0),
        BorderStyle = BorderStyle.FixedSingle
    };
}

public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    public async Task LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/login", new { username, password });
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Usuario o contraseña incorrectos.");
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        _http.DefaultRequestHeaders.Remove("X-ERP-Token");
        _http.DefaultRequestHeaders.Add("X-ERP-Token", login?.Token ?? "");
    }

    public async Task<List<Client>> GetClientsAsync() => await GetAsync<List<Client>>("api/clients");
    public async Task<List<Product>> GetProductsAsync() => await GetAsync<List<Product>>("api/products");
    public async Task<List<Sale>> GetSalesAsync() => await GetAsync<List<Sale>>("api/sales");
    public async Task<List<Payment>> GetPaymentsAsync() => await GetAsync<List<Payment>>("api/payments");
    public async Task AddClientAsync(ClientInput input) => await PostAsync("api/clients", input);
    public async Task AddProductAsync(ProductInput input) => await PostAsync("api/products", input);
    public async Task AddSaleAsync(SaleInput input) => await PostAsync("api/sales", input);
    public async Task AddPaymentAsync(PaymentInput input) => await PostAsync("api/payments", input);

    public async Task DeleteClientAsync(string id)
    {
        var response = await _http.DeleteAsync($"api/clients/{id}");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    }

    public async Task UpdateSaleAsync(string id, string status, string paymentMethod)
    {
        var response = await _http.PatchAsJsonAsync($"api/sales/{id}", new { status, paymentMethod });
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    }

    private async Task<T> GetAsync<T>(string path) => await _http.GetFromJsonAsync<T>(path) ?? throw new InvalidOperationException("Respuesta vacia de la API.");

    private async Task PostAsync(string path, object input)
    {
        var response = await _http.PostAsJsonAsync(path, input);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    }
}

public sealed record LoginResponse([property: JsonPropertyName("token")] string Token);
public sealed record ClientInput(string Name, string DocumentType, string DocumentNumber, string Phone);
public sealed record ProductInput(string Name, string Sku, decimal Price, int Stock, decimal CostPrice, decimal SalePrice, string Serials);
public sealed record SaleInput(string ClientId, List<SaleLine> Items, string Status, bool ManualStatus, string PaymentMethod, decimal InitialPayment, string Notes);
public sealed record PaymentInput(string SaleId, decimal Amount, string Method, List<string> ItemIds, string Note);
public sealed record SaleLine(string ProductId, string Name, decimal Price, int Qty, decimal CostPrice, List<string> Serials);

public sealed class Client
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("documentType")] public string DocumentType { get; set; } = "";
    [JsonPropertyName("documentNumber")] public string DocumentNumber { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    public override string ToString() => $"{Name} - {DocumentNumber}";
}

public sealed class Product
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sku")] public string Sku { get; set; } = "";
    [JsonPropertyName("price")] public decimal Price { get; set; }
    [JsonPropertyName("costPrice")] public decimal CostPrice { get; set; }
    [JsonPropertyName("salePrice")] public decimal SalePrice { get; set; }
    [JsonPropertyName("serials")] public List<string> Serials { get; set; } = [];
    [JsonPropertyName("stock")] public int Stock { get; set; }
    public decimal EffectiveSalePrice => SalePrice > 0 ? SalePrice : Price;
    public string SerialsText => string.Join(", ", Serials);
    public List<string> NextSeries(int qty) => Serials.Take(Math.Max(qty, 0)).ToList();
    public override string ToString() => $"{Name} - S/ {EffectiveSalePrice:0.00}";
}

public sealed class Sale
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("clientId")] public string ClientId { get; set; } = "";
    [JsonPropertyName("documentNumber")] public string DocumentNumber { get; set; } = "";
    [JsonPropertyName("client")] public Client? Client { get; set; }
    [JsonPropertyName("items")] public List<SaleItem> Items { get; set; } = [];
    [JsonPropertyName("total")] public decimal Total { get; set; }
    [JsonPropertyName("paid")] public decimal Paid { get; set; }
    [JsonPropertyName("balance")] public decimal Balance { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("paymentMethod")] public string PaymentMethod { get; set; } = "";
    public string Cliente => Client?.Name ?? "";
    public override string ToString() => $"{DocumentNumber} - {Cliente} - saldo S/ {Balance:0.00}";
}

public sealed class SaleItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("productId")] public string ProductId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public decimal Price { get; set; }
    [JsonPropertyName("costPrice")] public decimal CostPrice { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("serials")] public List<string> Serials { get; set; } = [];
    public override string ToString() => $"{Name} x {Qty} - S/ {(Price * Qty):0.00}";
}

public sealed class MarginRow
{
    public string Documento { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Producto { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal Costo { get; set; }
    public decimal Venta { get; set; }
    public decimal TotalCosto { get; set; }
    public decimal TotalVenta { get; set; }
    public decimal Margen { get; set; }
    public string Estado { get; set; } = "";
}

public sealed class Payment
{
    [JsonPropertyName("saleDocument")] public string Documento { get; set; } = "";
    [JsonPropertyName("clientName")] public string Cliente { get; set; } = "";
    [JsonPropertyName("amount")] public decimal Monto { get; set; }
    [JsonPropertyName("method")] public string Metodo { get; set; } = "";
    [JsonPropertyName("note")] public string Detalle { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTime Fecha { get; set; }
}
