using System.Text;
using ClosedXML.Excel;
using TyuStock.Core.Export;
using TyuStock.Core.Models;

namespace TyuStock.Core.Tests;

public class TableExporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tyu-export-" + Guid.NewGuid().ToString("N"));
    public TableExporterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ExportTable Sample() => new()
    {
        Name = "自选",
        Headers = ["代码", "名称", "价格"],
        Rows =
        [
            ["AAPL", "Apple, Inc.", "190.5"],
            ["SPY", "Line" + "\n" + "two", "500"],
        ],
    };

    [Fact]
    public void Csv_HasUtf8Bom_AndEscapesCommasAndQuotes()
    {
        var path = Path.Combine(_dir, "t.csv");
        TableExporter.WriteCsv(Sample(), path);
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = Encoding.UTF8.GetString(bytes[3..]).Replace("\r\n", "\n");
        Assert.Contains("代码,名称,价格", text);
        Assert.Contains("\"Apple, Inc.\"", text);
        Assert.Contains("\"Line\ntwo\"", text);
    }

    [Fact]
    public void CsvEscape_DoublesQuotes() =>
        Assert.Equal("\"say \"\"hi\"\"\"", TableExporter.CsvEscape("say \"hi\""));

    [Fact]
    public void Xlsx_RoundTripsTextAndNumbers_AndKeepsChineseSheetName()
    {
        var path = Path.Combine(_dir, "t.xlsx");
        TableExporter.WriteXlsx(Sample(), path);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("自选");
        Assert.Equal("代码", ws.Cell(1, 1).GetString());
        Assert.Equal("AAPL", ws.Cell(2, 1).GetString());
        Assert.Equal(190.5, ws.Cell(2, 3).GetDouble());
        Assert.Equal(500, ws.Cell(3, 3).GetDouble());
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
        Assert.True(ws.Cell(1, 1).Style.Font.FontSize >= 12);
    }

    [Fact]
    public void MarketExport_QuotesAndBars_PopulateExpectedColumns()
    {
        var q = new Quote
        {
            Symbol = "AAPL",
            Name = "Apple",
            Price = 190.5,
            Change = 1.25,
            ChangePercent = 0.66,
            Volume = 1000,
            Source = "yahoo",
        };
        var table = MarketExport.Quotes("Watchlist", ["Symbol", "Name", "Price", "Chg", "Chg%", "Vol", "Time", "Source", "Bid", "Ask", "Ext", "Ext%", "State"], [q]);
        Assert.Equal("AAPL", table.Rows[0][0]);
        Assert.Equal("190.5", table.Rows[0][2]);
        Assert.Equal("yahoo", table.Rows[0][7]);

        var series = new BarSeries
        {
            Symbol = "AAPL",
            Interval = BarInterval.Day1,
            Bars = [new Bar(new DateTimeOffset(2026, 11, 11, 16, 0, 0, TimeSpan.FromHours(-5)), 1, 2, 0.5, 1.5, 10)],
        };
        var bars = MarketExport.Bars("K", ["Time", "O", "H", "L", "C", "V"], series);
        Assert.Single(bars.Rows);
        Assert.Equal("1.5", bars.Rows[0][4]);
        Assert.Equal("10", bars.Rows[0][5]);
    }

    [Fact]
    public void MarketExport_Statement_UsesLocalizedItemNames()
    {
        var st = new StatementTable(
            FinancialStatementKind.Income,
            true,
            [new DateOnly(2025, 12, 31)],
            [new StatementRow("TotalRevenue", [1234.5])]);
        var table = MarketExport.Statement("利润表", st, ["营业收入"]);
        Assert.Equal(["利润表", "2025-12-31"], table.Headers);
        Assert.Equal("营业收入", table.Rows[0][0]);
        Assert.Equal("1234.5", table.Rows[0][1]);
    }

    // ---- F9: provenance metadata ----

    private static ExportTable WithMeta() => Sample() with
    {
        Metadata = [new("导出时间", "2026-11-12 21:00:00 +08:00"), new("时效", "延迟 15 分钟"), new("定价假设", "BSM · r = 4.12%")],
    };

    [Fact]
    public void Csv_writes_metadata_after_a_blank_line()
    {
        var path = Path.Combine(_dir, "m.csv");
        TableExporter.WriteCsv(WithMeta(), path);
        var lines = File.ReadAllText(path, Encoding.UTF8).Split(Environment.NewLine);
        var blank = Array.IndexOf(lines, "");
        Assert.True(blank > 2);                                  // header + rows first
        Assert.Equal("导出时间,2026-11-12 21:00:00 +08:00", lines[blank + 1]);
        Assert.Equal("时效,延迟 15 分钟", lines[blank + 2]);
    }

    [Fact]
    public void Xlsx_adds_an_info_sheet_with_text_values()
    {
        var path = Path.Combine(_dir, "m.xlsx");
        TableExporter.WriteXlsx([WithMeta()], path, ("导出信息", "表", "项目", "内容"));
        using var wb = new XLWorkbook(path);
        Assert.Equal(2, wb.Worksheets.Count);
        var info = wb.Worksheet("导出信息");
        Assert.Equal("项目", info.Cell(1, 2).GetString());
        Assert.Equal("自选", info.Cell(2, 1).GetString());
        Assert.Equal("BSM · r = 4.12%", info.Cell(4, 3).GetString());
        Assert.Equal(XLDataType.Text, info.Cell(2, 3).DataType);
        Assert.Equal(3, wb.Worksheet("自选").LastRowUsed()!.RowNumber());  // data sheet untouched
    }

    [Fact]
    public void No_metadata_means_no_info_sheet()
    {
        var path = Path.Combine(_dir, "n.xlsx");
        TableExporter.WriteXlsx([Sample()], path);
        using var wb = new XLWorkbook(path);
        Assert.Single(wb.Worksheets);
    }
}
