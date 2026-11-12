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
}