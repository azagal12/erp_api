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
    private readonly TextBox _apiUrl = new() { Text = "http://localhost:3000", Width = 260 };
    private readonly TextBox _user = new() { Text = "TecnoStor", Width = 260 };
    private readonly TextBox _password = new() { Text = "258922", PasswordChar = '*', Width = 260 };

    public LoginForm()
    {
        Text = "TecnoStor ERP - Login";
        Width = 390;
        Height = 280;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var login = new Button { Text = "Ingresar", Width = 260, Height = 36 };
        login.Click += async (_, _) => await LoginAsync();

        Controls.Add(Stack(
            Title("TecnoStor ERP"),
            Field("API nube / local", _apiUrl),
            Field("Usuario", _user),
            Field("Contraseña", _password),
            login
        ));
    }

    private async Task LoginAsync()
    {
        try
        {
            var api = new ApiClient(_apiUrl.Text.Trim());
            await api.LoginAsync(_user.Text.Trim(), _password.Text);
            Hide();
            new MainForm(api).ShowDialog();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se pudo ingresar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Label Title(string text) => new() { Text = text, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true };
    private static Control Field(string label, Control input) => Stack(new Label { Text = label, AutoSize = true }, input);
    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(24), AutoScroll = true, WrapContents = false };
        panel.Controls.AddRange(controls);
        return panel;
    }
}

public sealed class MainForm : Form
{
    private readonly ApiClient _api;
    private readonly DataGridView _clients = Grid();
    private readonly DataGridView _products = Grid();
    private readonly DataGridView _sales = Grid();
    private readonly DataGridView _payments = Grid();
    private readonly ComboBox _saleClient = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _saleProduct = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _saleQty = new() { Minimum = 1, Maximum = 999, Value = 1 };
    private readonly ComboBox _saleStatus = Pick("pagado", "credito", "deuda");
    private readonly ComboBox _saleMethod = Pick("efectivo", "yape", "transferencia");
    private readonly NumericUpDown _saleInitial = MoneyInput();
    private readonly ListBox _saleItems = new() { Width = 420, Height = 110 };
    private readonly ComboBox _paySale = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckedListBox _payItems = new() { Width = 360, Height = 110 };
    private readonly NumericUpDown _payAmount = MoneyInput();
    private readonly ComboBox _payMethod = Pick("efectivo", "yape", "transferencia");
    private readonly TextBox _payNote = new() { Width = 360 };
    private List<Client> _clientData = [];
    private List<Product> _productData = [];
    private List<Sale> _saleData = [];
    private readonly List<SaleLine> _newSaleItems = [];

    public MainForm(ApiClient api)
    {
        _api = api;
        Text = "TecnoStor ERP";
        Width = 1180;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Clientes", ClientsTab()));
        tabs.TabPages.Add(Page("Productos", ProductsTab()));
        tabs.TabPages.Add(Page("Venta", SalesTab()));
        tabs.TabPages.Add(Page("Caja", CashTab()));
        Controls.Add(tabs);

        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) => await RefreshProductsAsync();
        timer.Start();

        Load += async (_, _) => await LoadAllAsync();
    }

    private Control ClientsTab()
    {
        var name = new TextBox { Width = 240 };
        var docType = Pick("dni", "ruc");
        var doc = new TextBox { Width = 180 };
        var phone = new TextBox { Width = 180 };
        var add = new Button { Text = "Agregar cliente", Height = 34 };
        add.Click += async (_, _) =>
        {
            await _api.AddClientAsync(new ClientInput(name.Text, docType.Text, doc.Text, phone.Text));
            name.Clear(); doc.Clear(); phone.Clear();
            await LoadAllAsync();
        };
        var delete = new Button { Text = "Eliminar seleccionado", Height = 34 };
        delete.Click += async (_, _) =>
        {
            if (_clients.CurrentRow?.DataBoundItem is Client client)
            {
                await _api.DeleteClientAsync(client.Id);
                await LoadAllAsync();
            }
        };
        return Split(Stack(Field("Nombre", name), Field("Tipo", docType), Field("DNI/RUC", doc), Field("Teléfono", phone), add, delete), _clients);
    }

    private Control ProductsTab()
    {
        var name = new TextBox { Width = 240 };
        var sku = new TextBox { Width = 160 };
        var price = MoneyInput();
        var stock = new NumericUpDown { Width = 160, Minimum = 0, Maximum = 999999 };
        var add = new Button { Text = "Agregar producto", Height = 34 };
        add.Click += async (_, _) =>
        {
            await _api.AddProductAsync(new ProductInput(name.Text, sku.Text, price.Value, (int)stock.Value));
            name.Clear(); sku.Clear(); price.Value = 0; stock.Value = 0;
            await RefreshProductsAsync();
        };
        return Split(Stack(Field("Producto", name), Field("SKU", sku), Field("Precio", price), Field("Stock", stock), add, new Label { Text = "Auto-refresh cada 5 segundos.", AutoSize = true }), _products);
    }

    private Control SalesTab()
    {
        var addLine = new Button { Text = "Agregar producto", Height = 34 };
        addLine.Click += (_, _) =>
        {
            if (_saleProduct.SelectedItem is not Product product) return;
            _newSaleItems.Add(new SaleLine(product.Id, product.Name, product.Price, (int)_saleQty.Value));
            RenderSaleLines();
        };
        var save = new Button { Text = "Guardar venta", Height = 36 };
        save.Click += async (_, _) =>
        {
            if (_saleClient.SelectedItem is not Client client || _newSaleItems.Count == 0) return;
            await _api.AddSaleAsync(new SaleInput(client.Id, _newSaleItems, _saleStatus.Text, true, _saleMethod.Text, _saleInitial.Value, ""));
            _newSaleItems.Clear();
            _saleInitial.Value = 0;
            RenderSaleLines();
            await LoadAllAsync();
        };
        _sales.CellValueChanged += async (_, e) =>
        {
            if (e.RowIndex < 0 || _sales.Rows[e.RowIndex].DataBoundItem is not Sale sale) return;
            await _api.UpdateSaleAsync(sale.Id, sale.Status, sale.PaymentMethod);
            await LoadAllAsync();
        };
        return Split(Stack(Field("Cliente", _saleClient), Field("Producto", _saleProduct), Field("Cantidad", _saleQty), addLine, _saleItems, Field("Estado", _saleStatus), Field("Método", _saleMethod), Field("Pago inicial", _saleInitial), save), _sales);
    }

    private Control CashTab()
    {
        _paySale.SelectedIndexChanged += (_, _) => RenderPaymentItems();
        var add = new Button { Text = "Subir pago a caja", Height = 36 };
        add.Click += async (_, _) =>
        {
            if (_paySale.SelectedItem is not Sale sale) return;
            var items = _payItems.CheckedItems.Cast<SaleItem>().Select(item => item.Id).ToList();
            await _api.AddPaymentAsync(new PaymentInput(sale.Id, _payAmount.Value, _payMethod.Text, items, _payNote.Text));
            _payAmount.Value = 0;
            _payNote.Clear();
            await LoadAllAsync();
        };
        return Split(Stack(Field("Venta pendiente", _paySale), Field("Productos abonados", _payItems), Field("Monto", _payAmount), Field("Método", _payMethod), Field("Detalle", _payNote), add), _payments);
    }

    private async Task LoadAllAsync()
    {
        _clientData = await _api.GetClientsAsync();
        _productData = await _api.GetProductsAsync();
        _saleData = await _api.GetSalesAsync();
        _clients.DataSource = _clientData;
        _products.DataSource = _productData;
        _sales.DataSource = _saleData;
        _payments.DataSource = await _api.GetPaymentsAsync();
        _saleClient.DataSource = _clientData.ToList();
        _saleProduct.DataSource = _productData.ToList();
        _paySale.DataSource = _saleData.Where(s => s.Balance > 0).ToList();
        ColorSales();
        RenderPaymentItems();
    }

    private async Task RefreshProductsAsync()
    {
        _productData = await _api.GetProductsAsync();
        _products.DataSource = _productData;
        _saleProduct.DataSource = _productData.ToList();
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
                "pagado" => Color.FromArgb(217, 247, 223),
                "credito" => Color.FromArgb(255, 243, 191),
                "deuda" => Color.FromArgb(255, 216, 216),
                _ => Color.White
            };
        }
    }

    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, AutoGenerateColumns = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private static TabPage Page(string title, Control control) => new(title) { Controls = { control } };
    private static ComboBox Pick(params string[] values) => new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = values.ToList() };
    private static NumericUpDown MoneyInput() => new() { Width = 160, DecimalPlaces = 2, Maximum = 9999999, Increment = 1 };
    private static Control Field(string label, Control input) => Stack(new Label { Text = label, AutoSize = true }, input);
    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Width = 430, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), AutoScroll = true, WrapContents = false };
        panel.Controls.AddRange(controls);
        return panel;
    }
    private static SplitContainer Split(Control left, Control right)
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 440 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        return split;
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

    private async Task<T> GetAsync<T>(string path) => await _http.GetFromJsonAsync<T>(path) ?? throw new InvalidOperationException("Respuesta vacía de la API.");
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
