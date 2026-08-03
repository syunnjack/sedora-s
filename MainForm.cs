using System.Net;
using System.Text.Json;

namespace BarcodeWorkInfoComplete;

public sealed class MainForm : Form
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly TextBox codeBox = new() { Font = new Font("Consolas", 18), PlaceholderText = "JAN / ISBNを読み取り、または入力" };
    private readonly Button searchButton = new() { Text = "検索", BackColor = Color.FromArgb(23, 105, 170), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Label status = new() { AutoEllipsis = true, ForeColor = Color.FromArgb(65, 81, 96) };
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Microsoft.Web.WebView2.WinForms.WebView2 view = new();
    private readonly TextBox appIdBox = new() { PlaceholderText = "楽天アプリID" };
    private readonly TextBox accessKeyBox = new() { PlaceholderText = "楽天アクセスキー", UseSystemPasswordChar = true };
    private readonly TextBox keepaKeyBox = new() { PlaceholderText = "Keepa APIキー（Amazon新品・中古相場、自動判定用）", UseSystemPasswordChar = true };
    private readonly Button saveSettingsButton = new() { Text = "API設定を保存" };
    private readonly NumericUpDown costBox = MoneyBox();
    private readonly NumericUpDown saleBox = MoneyBox();
    private readonly NumericUpDown feeBox = new() { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Value = 10, Width = 75 };
    private readonly NumericUpDown shippingBox = MoneyBox();
    private readonly Label profitLabel = new() { AutoSize = true, Font = new Font("Yu Gothic UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(25, 95, 65) };
    private bool ready;

    public MainForm()
    {
        Text = "JAN・ISBN・DVD/Blu-ray 作品情報検索";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 620);
        Size = new Size(1040, 760);
        BackColor = Color.FromArgb(244, 247, 251);
        BuildLayout();
        LoadSettings();
        Shown += async (_, _) => await InitializeAsync();
        searchButton.Click += async (_, _) => await SearchAsync();
        codeBox.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SearchAsync(); } };
        saveSettingsButton.Click += (_, _) => SaveSettings();
        costBox.ValueChanged += (_, _) => CalculateProfit(); saleBox.ValueChanged += (_, _) => CalculateProfit();
        feeBox.ValueChanged += (_, _) => CalculateProfit(); shippingBox.ValueChanged += (_, _) => CalculateProfit();
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = Color.FromArgb(28, 55, 82) };
        header.Controls.Add(new Label { Text = "JAN・ISBN・DVD/Blu-ray 作品情報検索", ForeColor = Color.White, Font = new Font("Yu Gothic UI", 20, FontStyle.Bold), AutoSize = true, Location = new Point(24, 12) });
        header.Controls.Add(new Label { Text = "USBバーコードリーダーで読み取ると自動検索します", ForeColor = Color.FromArgb(218, 230, 240), AutoSize = true, Location = new Point(27, 57) });

        var input = new Panel { Dock = DockStyle.Top, Height = 112, Padding = new Padding(24, 18, 24, 4) };
        codeBox.SetBounds(24, 18, 760, 38); codeBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        searchButton.SetBounds(800, 18, 185, 39); searchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        progress.SetBounds(24, 63, 961, 4); progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        status.SetBounds(24, 76, 961, 24); status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        input.Controls.AddRange([codeBox, searchButton, progress, status]);

        var settings = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(24, 8, 24, 8), BackColor = Color.FromArgb(232, 238, 244) };
        appIdBox.SetBounds(24, 12, 250, 27); accessKeyBox.SetBounds(286, 12, 430, 27); saveSettingsButton.SetBounds(728, 11, 150, 29);
        keepaKeyBox.SetBounds(24, 45, 692, 27);
        settings.Controls.AddRange([appIdBox, accessKeyBox, keepaKeyBox, saveSettingsButton]);

        var calculator = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(20, 10, 20, 6), BackColor = Color.FromArgb(245, 249, 243), WrapContents = false };
        calculator.Controls.AddRange([new Label { Text = "利益計算　仕入", AutoSize = true, Margin = new Padding(4, 5, 3, 0) }, costBox,
            new Label { Text = "円　販売", AutoSize = true, Margin = new Padding(5, 5, 3, 0) }, saleBox,
            new Label { Text = "円　手数料", AutoSize = true, Margin = new Padding(5, 5, 3, 0) }, feeBox,
            new Label { Text = "%　送料", AutoSize = true, Margin = new Padding(5, 5, 3, 0) }, shippingBox,
            new Label { Text = "円　", AutoSize = true, Margin = new Padding(3, 5, 3, 0) }, profitLabel]);

        view.Dock = DockStyle.Fill;
        Controls.Add(view); Controls.Add(input); Controls.Add(calculator); Controls.Add(settings); Controls.Add(header);
    }

    private async Task InitializeAsync()
    {
        try
        {
            await view.EnsureCoreWebView2Async(); ready = true;
            ShowPage("バーコードを読み取ってください", "ISBNは書籍情報、JANはDVD/Blu-ray作品を優先して検索します。", null);
            status.Text = "準備完了"; codeBox.Focus();
        }
        catch (Exception ex) { status.Text = "WebView2を起動できません: " + ex.Message; }
    }

    private async Task SearchAsync()
    {
        if (!ready || !searchButton.Enabled) return;
        string code = Normalize(codeBox.Text); codeBox.Text = code;
        if (!Barcode.IsValid(code, out string type, out string error)) { status.Text = error; ShowPage("番号を確認してください", error, null); SelectInput(); return; }

        searchButton.Enabled = false; progress.Visible = true; status.Text = $"{type} {code} を検索中...";
        try
        {
            Work? work;
            if (type.StartsWith("ISBN"))
            {
                work = await SearchBookAsync(code);
                if (work is null && code.Length == 13) work = await SearchRakutenBooksAsync(code);
            }
            else
            {
                work = await SearchRakutenBooksAsync(code); // DVD/Blu-rayを含む楽天Booksを最優先
                work ??= await SearchRakutenMarketAsync(code); // 一般商品JANの予備検索
            }
            if (work is null) { status.Text = "作品情報が見つかりませんでした。"; ShowPage("情報が見つかりませんでした", $"コード: {code}", null); }
            else
            {
                MarketSummary market = await AnalyzeMarketsAsync(code);
                if (market.SuggestedPrice > 0) saleBox.Value = Math.Min(saleBox.Maximum, market.SuggestedPrice);
                status.Text = "取得・相場判定完了: " + work.Title; ShowWork(work, code, type, market);
            }
        }
        catch (Exception ex) { status.Text = "検索できませんでした。"; ShowPage("検索エラー", WebUtility.HtmlEncode(ex.Message), null); }
        finally { searchButton.Enabled = true; progress.Visible = false; SelectInput(); }
    }

    private static async Task<Work?> SearchBookAsync(string isbn)
    {
        using JsonDocument doc = await GetJsonAsync("https://www.googleapis.com/books/v1/volumes?q=isbn:" + Uri.EscapeDataString(isbn) + "&maxResults=1");
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0) return null;
        var v = items[0].GetProperty("volumeInfo");
        return new Work(S(v, "title") ?? "（タイトル不明）", Join(v, "authors"), S(v, "publisher"), S(v, "publishedDate"), S(v, "description"), Nested(v, "imageLinks", "thumbnail")?.Replace("http://", "https://"), S(v, "infoLink"), "書籍", "Google Books");
    }

    private async Task<Work?> SearchRakutenBooksAsync(string jan)
    {
        EnsureRakutenSettings();
        string url = "https://openapi.rakuten.co.jp/services/api/BooksTotal/Search/20170404" +
            $"?applicationId={Uri.EscapeDataString(appIdBox.Text.Trim())}&accessKey={Uri.EscapeDataString(accessKeyBox.Text.Trim())}&isbnjan={jan}&hits=5&formatVersion=2";
        using JsonDocument doc = await GetJsonAsync(url);
        if (!TryItems(doc.RootElement, out var items) || items.GetArrayLength() == 0) return null;
        var x = items[0];
        string creator = S(x, "artistName") ?? S(x, "author") ?? "";
        string category = string.IsNullOrWhiteSpace(S(x, "artistName")) ? "書籍・楽天Books商品" : "DVD/Blu-ray・映像作品";
        return new Work(S(x, "title") ?? "（作品名不明）", creator, S(x, "publisherName") ?? S(x, "label"), S(x, "salesDate"), null, S(x, "largeImageUrl") ?? S(x, "mediumImageUrl"), S(x, "itemUrl"), category, "楽天Books");
    }

    private async Task<Work?> SearchRakutenMarketAsync(string jan)
    {
        EnsureRakutenSettings();
        string url = "https://openapi.rakuten.co.jp/services/api/IchibaItem/Search/20220601" +
            $"?applicationId={Uri.EscapeDataString(appIdBox.Text.Trim())}&accessKey={Uri.EscapeDataString(accessKeyBox.Text.Trim())}&keyword={jan}&hits=5&formatVersion=2";
        using JsonDocument doc = await GetJsonAsync(url);
        if (!TryItems(doc.RootElement, out var items) || items.GetArrayLength() == 0) return null;
        var x = items[0]; string? image = FirstImage(x, "mediumImageUrls");
        string description = (x.TryGetProperty("itemPrice", out var p) ? $"価格: {p.GetDecimal():N0}円\n" : "") + (S(x, "itemCaption") ?? "");
        return new Work(S(x, "itemName") ?? "（商品名不明）", S(x, "shopName"), null, null, description, image, S(x, "itemUrl"), "一般JAN商品", "楽天市場");
    }

    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url); string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"APIエラー {(int)response.StatusCode}: {ApiMessage(body)}");
        return JsonDocument.Parse(body);
    }

    private async Task<MarketSummary> AnalyzeMarketsAsync(string code)
    {
        var prices = new List<MarketPrice>();
        if (!string.IsNullOrWhiteSpace(appIdBox.Text) && !string.IsNullOrWhiteSpace(accessKeyBox.Text))
        {
            string url = "https://openapi.rakuten.co.jp/services/api/IchibaItem/Search/20220601" +
                $"?applicationId={Uri.EscapeDataString(appIdBox.Text.Trim())}&accessKey={Uri.EscapeDataString(accessKeyBox.Text.Trim())}&keyword={code}&hits=30&formatVersion=2";
            try
            {
                using var d = await GetJsonAsync(url);
                if (TryItems(d.RootElement, out var a)) foreach (var x in a.EnumerateArray())
                    if (x.TryGetProperty("itemPrice", out var p) && p.TryGetDecimal(out decimal value) && value > 0) prices.Add(new("楽天市場", "新品・出品価格", value));
            }
            catch { }
        }
        if (!string.IsNullOrWhiteSpace(keepaKeyBox.Text))
        {
            string url = $"https://api.keepa.com/product?key={Uri.EscapeDataString(keepaKeyBox.Text.Trim())}&domain=5&asin={Uri.EscapeDataString(code)}&stats=90&history=0";
            try
            {
                using var d = await GetJsonAsync(url);
                if (d.RootElement.TryGetProperty("products", out var products) && products.GetArrayLength() > 0 && products[0].TryGetProperty("stats", out var stats) && stats.TryGetProperty("current", out var current))
                {
                    AddKeepa(prices, current, 0, "Amazon本体"); AddKeepa(prices, current, 1, "Amazon新品"); AddKeepa(prices, current, 2, "Amazon中古");
                }
            }
            catch { }
        }
        var valid = prices.Select(x => x.Price).OrderBy(x => x).ToArray();
        decimal median = valid.Length == 0 ? 0 : valid.Length % 2 == 1 ? valid[valid.Length / 2] : (valid[valid.Length / 2 - 1] + valid[valid.Length / 2]) / 2;
        decimal suggested = prices.Where(x => x.Condition.Contains("中古")).Select(x => x.Price).DefaultIfEmpty(median).Min();
        return new(prices, valid.FirstOrDefault(), median, suggested);
    }

    private static void AddKeepa(List<MarketPrice> prices, JsonElement current, int index, string condition)
    {
        if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= index || !current[index].TryGetInt32(out int raw) || raw <= 0) return;
        prices.Add(new("Amazon（Keepa）", condition, raw / 100m));
    }

    private void ShowWork(Work w, string code, string type, MarketSummary market)
    {
        string image = string.IsNullOrWhiteSpace(w.Image) ? "" : $"<img class='cover' src='{E(w.Image)}'>";
        string link = string.IsNullOrWhiteSpace(w.Url) ? "" : $"<a class='button' href='{E(w.Url)}' target='_blank'>詳細ページを開く</a>";
        string marketRows = string.Join("", market.Prices.GroupBy(x => new { x.Source, x.Condition }).Select(g => $"<tr><td>{E(g.Key.Source)}</td><td>{E(g.Key.Condition)}</td><td>{g.Min(x => x.Price):N0}円</td><td>{g.Count()}件</td></tr>"));
        string judgment = market.SuggestedPrice <= 0 ? "相場データなし" : $"推奨販売価格 {market.SuggestedPrice:N0}円 / 全体最安 {market.Lowest:N0}円 / 中央値 {market.Median:N0}円";
        string body = $"<div class='meta'>{E(type)} ・ {E(code)} ・ {E(w.Category)} ・ 情報元: {E(w.Source)}</div><div class='result'>{image}<section><h1>{E(w.Title)}</h1>{Row("出演者・著者・販売元", w.Creator)}{Row("発売元・出版社", w.Publisher)}{Row("発売日", w.Date)}{(string.IsNullOrWhiteSpace(w.Description) ? "" : $"<p class='description'>{E(w.Description)}</p>")}{link}</section></div><h2>中古市場・販売相場の自動判定</h2><p><strong>{E(judgment)}</strong></p><table><tr><th>市場</th><th>状態</th><th>取得価格</th><th>件数</th></tr>{marketRows}</table><p>画面下の利益計算には推奨販売価格を自動入力しています。仕入額を入力すると利幅を判定できます。</p>";
        ShowPage(null, body, true);
    }

    private void ShowPage(string? title, string message, bool? raw)
    {
        if (!ready) return; string content = raw == true ? message : $"<div class='empty'><h1>{E(title)}</h1><p>{message}</p></div>";
        view.NavigateToString($"<!doctype html><html lang='ja'><meta charset='utf-8'><style>*{{box-sizing:border-box}}body{{margin:0;padding:28px;background:#f4f7fb;color:#172133;font-family:'Yu Gothic UI','Meiryo',sans-serif}}main{{max-width:1000px;margin:auto;background:#fff;border-radius:16px;padding:28px;box-shadow:0 6px 24px #20304018}}.empty{{text-align:center;padding:45px 20px}}.meta{{color:#587087;font-size:13px;margin-bottom:18px}}.result{{display:flex;gap:28px}}.cover{{width:190px;max-height:280px;object-fit:contain}}section{{flex:1}}h1{{font-size:26px;margin:0 0 18px}}p{{line-height:1.7}}.description{{white-space:pre-wrap;border-top:1px solid #dde4eb;padding-top:15px;max-height:220px;overflow:auto}}.button{{display:inline-block;padding:11px 18px;background:#1769aa;color:white;text-decoration:none;border-radius:8px;font-weight:bold}}table{{border-collapse:collapse;width:100%}}th,td{{border:1px solid #ccd6df;padding:8px;text-align:left}}th{{background:#eaf0f5}}</style><body><main>{content}</main></body></html>");
    }

    private void EnsureRakutenSettings() { if (string.IsNullOrWhiteSpace(appIdBox.Text) || string.IsNullOrWhiteSpace(accessKeyBox.Text)) throw new InvalidOperationException("DVD/JAN検索には画面下部の楽天アプリIDとアクセスキーが必要です。入力後「API設定を保存」を押してください。"); }
    private static NumericUpDown MoneyBox() => new() { Minimum = 0, Maximum = 100000000, ThousandsSeparator = true, Increment = 100, Width = 105 };
    private void CalculateProfit() { decimal profitValue = saleBox.Value - costBox.Value - saleBox.Value * feeBox.Value / 100m - shippingBox.Value; decimal rate = saleBox.Value == 0 ? 0 : profitValue / saleBox.Value * 100m; profitLabel.Text = $"見込み利益 {profitValue:N0}円（利益率 {rate:N1}%）"; profitLabel.ForeColor = profitValue >= 0 ? Color.FromArgb(25, 95, 65) : Color.Firebrick; }
    private void SelectInput() { codeBox.SelectAll(); codeBox.Focus(); }
    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Row(string label, string? value) => string.IsNullOrWhiteSpace(value) ? "" : $"<p><strong>{E(label)}:</strong> {E(value)}</p>";
    private static string? S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? Nested(JsonElement e, string p, string n) => e.TryGetProperty(p, out var x) ? S(x, n) : null;
    private static string? Join(JsonElement e, string n) => e.TryGetProperty(n, out var a) && a.ValueKind == JsonValueKind.Array ? string.Join("、", a.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))) : null;
    private static bool TryItems(JsonElement root, out JsonElement items) => root.TryGetProperty("Items", out items) || root.TryGetProperty("items", out items);
    private static string? FirstImage(JsonElement e, string n) { if (!e.TryGetProperty(n, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() == 0) return null; return a[0].ValueKind == JsonValueKind.String ? a[0].GetString() : S(a[0], "imageUrl"); }
    private static string ApiMessage(string b) { try { using var d = JsonDocument.Parse(b); return S(d.RootElement, "error_description") ?? S(d.RootElement, "message") ?? "詳細不明"; } catch { return "詳細不明"; } }

    private string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarcodeWorkInfo", "settings.json");
    private void SaveSettings() { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new ApiSettings(appIdBox.Text.Trim(), accessKeyBox.Text.Trim(), keepaKeyBox.Text.Trim()))); status.Text = "API設定を保存しました。"; codeBox.Focus(); }
    private void LoadSettings() { try { if (!File.Exists(SettingsPath)) return; var s = JsonSerializer.Deserialize<ApiSettings>(File.ReadAllText(SettingsPath)); appIdBox.Text = s?.ApplicationId ?? ""; accessKeyBox.Text = s?.AccessKey ?? ""; keepaKeyBox.Text = s?.KeepaKey ?? ""; } catch { } }

    private sealed record Work(string Title, string? Creator, string? Publisher, string? Date, string? Description, string? Image, string? Url, string Category, string Source);
    private sealed record ApiSettings(string ApplicationId, string AccessKey, string KeepaKey);
    private sealed record MarketPrice(string Source, string Condition, decimal Price);
    private sealed record MarketSummary(List<MarketPrice> Prices, decimal Lowest, decimal Median, decimal SuggestedPrice);
}

internal static class Barcode
{
    public static bool IsValid(string code, out string type, out string error)
    {
        type = ""; error = "";
        if (code.Length == 10 && Isbn10(code)) { type = "ISBN-10"; return true; }
        if (code.Length == 13 && code.All(char.IsDigit) && Ean13(code)) { type = code.StartsWith("978") || code.StartsWith("979") ? "ISBN-13" : "JAN"; return true; }
        error = "10桁のISBN、またはチェックデジットが正しい13桁のJAN/ISBNを入力してください。"; return false;
    }
    private static bool Ean13(string c) { int s = 0; for (int i = 0; i < 12; i++) s += (c[i] - '0') * (i % 2 == 0 ? 1 : 3); return (10 - s % 10) % 10 == c[12] - '0'; }
    private static bool Isbn10(string c) { if (!c.Take(9).All(char.IsDigit) || !(char.IsDigit(c[9]) || c[9] == 'X')) return false; int s = 0; for (int i = 0; i < 10; i++) s += (10 - i) * (i == 9 && c[i] == 'X' ? 10 : c[i] - '0'); return s % 11 == 0; }
}

