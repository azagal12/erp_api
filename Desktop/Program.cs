using System.Net.Http.Json;
using System.Text.Json.Serialization;

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
    private readonly TextBox _apiUrl = Ui.Text("https://erp-api-7hd5.onrender.com", 340);
    private readonly TextBox _user = Ui.Text("TecnoStor", 260);
    private readonly TextBox _password = Ui.Text("258922", 260, true);

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

        card.Controls.Add(Ui.Stack(
            Ui.Title("TecnoStor ERP"),
            Ui.Subtitle("Programa de escritorio conectado a la nube"),
            Ui.Field("API", _apiUrl),
            Ui.Field("Usuario", _user),
            Ui.Field("Contraseña", _password),
            login
        ));

        Controls.Add(card);
        Resize += (_, _) => card.Location = new Point((ClientSize.Width - card.Width) / 2, (ClientSize.Height - card.Height) / 2);
    }

    private async Task LoginAsync()
    {
        try
        {
            var api = new ApiClient(_apiUrl.Text.Trim());
            await api.LoginAsync(_user.Text.Trim(), _password.Text);
            Hide();
            new MainForm(api, _apiUrl.Text.Trim()).ShowDialog();
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

    private readonly DataGridView _clients = Ui.Grid();
    private readonly DataGridView _products = Ui.Grid();
    private readonly DataGridView _sales = Ui.Grid();
    private readonly DataGridView _payments = Ui.Grid();

    private readonly ComboBox _saleClient = Ui.Combo(320);
    private readonly ComboBox _saleProduct = Ui.Combo(320);
    private readonly NumericUpDown _saleQty = Ui.Number(1, 999, 1);
    private readonly ComboBox _saleStatus = Ui.Combo(180, "pagado", "credito", "deuda");
    private readonly ComboBox _saleMethod = Ui.Combo(180, "efectivo", "yape", "transferencia");
    private readonly NumericUpDown _saleInitial = Ui.Money();
    private readonly ListBox _saleItems = new() { Width = 430, Height = 130, BorderStyle = BorderStyle.FixedSingle };

    private readonly ComboBox _paySale = Ui.Combo(430);
    private readonly CheckedListBox _payItems = new() { Width = 430, Height = 130, BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true };
    private readonly NumericUpDown _payAmount = Ui.Money();
    private readonly ComboBox _payMethod = Ui.Combo(180, "efectivo", "yape", "transferencia");
    private readonly TextBox _payNote = Ui.Text("", 430);

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
        BackColor = Ui.Page;

        Controls.Add(_content);
        Controls.Add(Header());
        Controls.Add(StatusBar());

        Navigate("clientes");

        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) => await SafeRun(RefreshProductsAsync);
        timer.Start();

        Load += async (_, _) => await SafeRun(LoadAllAsync);
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

        nav.Controls.Add(NavButton("clientes", "Clientes"));
        nav.Controls.Add(NavButton("productos", "Productos"));
        nav.Controls.Add(NavButton("ventas", "Venta"));
        nav.Controls.Add(NavButton("caja", "Caja"));

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
            Margin = new Padding(0, 0, 48, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.Black;
        button.FlatAppearance.BorderSize = 3;
        button.Click += (_, _) => Navigate(key);
        _navButtons[key] = button;
        return button;
    }

    private void Navigate(string key)
    {
        foreach (var item in _navButtons)
        {
            item.Value.BackColor = item.Key == key ? Color.Black : Color.White;
            item.Value.ForeColor = item.Key == key ? Color.White : Color.Black;
        }

        _content.Controls.Clear();
        _content.Controls.Add(key switch
        {
            "clientes" => ClientsView(),
            "productos" => ProductsView(),
            "ventas" => SalesView(),
            "caja" => CashView(),
            _ => ClientsView()
        });
    }

    private Control ClientsView()
    {
        var name = Ui.Text("", 330);
        var docType = Ui.Combo(160, "dni", "ruc");
        var doc = Ui.Text("", 210);
        var phone = Ui.Text("", 210);
        var add = Ui.PrimaryButton("Agregar cliente", 330);
        var delete = Ui.DangerButton("Eliminar seleccionado", 330);

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
        var price = Ui.Money();
        var stock = Ui.Number(0, 999999, 0);
        var add = Ui.PrimaryButton("Agregar producto", 330);

        add.Click += async (_, _) =>
        {
            await SafeRun(async () =>
            {
                await _api.AddProductAsync(new ProductInput(name.Text, sku.Text, price.Value, (int)stock.Value));
                name.Clear(); sku.Clear(); price.Value = 0; stock.Value = 0;
                await RefreshProductsAsync();
            });
        };

        return Workspace(
            Ui.FormPanel("Productos", "Auto-refresh activo cada 5 segundos",
                Ui.Field("Producto", name),
                Ui.Two(Ui.Field("SKU", sku), Ui.Field("Precio", price)),
                Ui.Field("Stock", stock),
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

        addLine.Click += (_, _) =>
        {
            if (_saleProduct.SelectedItem is not Product product) return;
            _newSaleItems.Add(new SaleLine(product.Id, product.Name, product.Price, (int)_saleQty.Value));
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

        return Workspace(
            Ui.FormPanel("Venta", "Estados: verde pagado, amarillo credito, rojo deuda",
                Ui.Field("Cliente", _saleClient),
                Ui.Field("Producto", _saleProduct),
                Ui.Two(Ui.Field("Cantidad", _saleQty), Ui.Field("Pago inicial", _saleInitial)),
                addLine,
                removeLine,
                Ui.Field("Productos de la venta", _saleItems),
                Ui.Two(Ui.Field("Estado", _saleStatus), Ui.Field("Metodo", _saleMethod)),
                save
            ),
            Ui.TablePanel("Documentos de venta", _sales)
        );
    }

    private Control CashView()
    {
        _paySale.SelectedIndexChanged -= PaymentSaleChanged;
        _paySale.SelectedIndexChanged += PaymentSaleChanged;

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
                Ui.Field("Productos abonados", _payItems),
                Ui.Two(Ui.Field("Monto", _payAmount), Ui.Field("Metodo", _payMethod)),
                Ui.Field("Detalle", _payNote),
                add
            ),
            Ui.TablePanel("Movimientos de caja", _payments)
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
        HideColumns(_products, "Id");
        Header(_products, ("Name", "Producto"), ("Sku", "SKU"), ("Price", "Precio"), ("Stock", "Stock"));
        MakeReadOnly(_products);
        _saleProduct.DataSource = _productData.ToList();
    }

    private void BindSales()
    {
        _sales.DataSource = null;
        _sales.DataSource = _saleData;
        HideColumns(_sales, "Id", "Client", "Items");
        Header(_sales, ("DocumentNumber", "Documento"), ("Cliente", "Cliente"), ("Total", "Total"), ("Paid", "Pagado"), ("Balance", "Saldo"), ("Status", "Estado"), ("PaymentMethod", "Metodo"));
        MakeReadOnly(_sales, "Status", "PaymentMethod");
        ColorSales();
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
        _saleItems.DataSource = _newSaleItems.Select(item => $"{item.Name} x {item.Qty} - S/ {(item.Price * item.Qty):0.00}").ToList();
    }

    private void RenderPaymentItems()
    {
        _payItems.Items.Clear();
        if (_paySale.SelectedItem is not Sale sale) return;
        foreach (var item in sale.Items) _payItems.Items.Add(item);
    }

    private void ColorSales()
    {
        foreach (DataGridViewRow row in _sales.Rows)
        {
            if (row.DataBoundItem is not Sale sale) continue;
            row.DefaultCellStyle.BackColor = sale.Status switch
            {
                "pagado" => Color.FromArgb(219, 247, 224),
                "credito" => Color.FromArgb(255, 242, 184),
                "deuda" => Color.FromArgb(255, 218, 218),
                _ => Color.White
            };
            row.DefaultCellStyle.ForeColor = Color.FromArgb(24, 32, 42);
        }
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
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 410,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Ui.Page
        };
        split.Panel1.Padding = new Padding(0, 0, 18, 0);
        split.Panel2.Padding = new Padding(0);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        return split;
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
}

public static class Ui
{
    public static readonly Color Page = Color.FromArgb(242, 245, 249);
    public static readonly Color Surface = Color.White;
    public static readonly Color Line = Color.FromArgb(210, 218, 229);
    public static readonly Color Ink = Color.FromArgb(24, 32, 42);
    public static readonly Color Muted = Color.FromArgb(95, 104, 119);
    public static readonly Color Blue = Color.FromArgb(34, 87, 197);
    public static readonly Color Red = Color.FromArgb(180, 35, 24);

    public static TextBox Text(string value, int width, bool password = false) => new()
    {
        Text = value,
        Width = width,
        Height = 34,
        BorderStyle = BorderStyle.FixedSingle,
        PasswordChar = password ? '*' : '\0'
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
        return button;
    }

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
            EnableHeadersVisualStyles = false
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 251);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 226, 249);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.RowTemplate.Height = 34;
        return grid;
    }

    public static Control SummaryGrid(params (string label, decimal value)[] items)
    {
        var panel = new TableLayoutPanel
        {
            Width = 360,
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
public sealed record ProductInput(string Name, string Sku, decimal Price, int Stock);
public sealed record SaleInput(string ClientId, List<SaleLine> Items, string Status, bool ManualStatus, string PaymentMethod, decimal InitialPayment, string Notes);
public sealed record PaymentInput(string SaleId, decimal Amount, string Method, List<string> ItemIds, string Note);
public sealed record SaleLine(string ProductId, string Name, decimal Price, int Qty);

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
    [JsonPropertyName("stock")] public int Stock { get; set; }
    public override string ToString() => $"{Name} - S/ {Price:0.00}";
}

public sealed class Sale
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
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
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("price")] public decimal Price { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    public override string ToString() => $"{Name} x {Qty} - S/ {(Price * Qty):0.00}";
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
