using Meili.Core.Export;

namespace Meili.Shared.Tests;

[Collection(nameof(UiPlatformCollection))]
public class ExportUiTests : IDisposable
{
    private readonly FakeUiPlatform _ui = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "meili-shared-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalizationService _loc = new();

    public ExportUiTests() => Ui.Platform = _ui;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static ExportTable Table(int rows) => new()
    {
        Name = "t",
        Headers = ["a", "b"],
        Rows = Enumerable.Range(0, rows).Select(i => (IReadOnlyList<string>)[i.ToString(), "x"]).ToList(),
    };

    [Fact]
    public async Task Empty_tables_report_no_data_without_a_dialog()
    {
        var msg = await ExportUi.SaveAsync("n", [Table(0)], _loc);
        Assert.Equal(_loc["Ex_NoData"], msg);
        Assert.Null(_ui.SaveThreadId);
    }

    [Fact]
    public async Task Cancelled_dialog_returns_null()
    {
        _ui.SavePath = null;
        Assert.Null(await ExportUi.SaveAsync("n", [Table(2)], _loc));
    }

    [Fact]
    public async Task Csv_and_xlsx_are_written()
    {
        _ui.SavePath = Path.Combine(_dir, "q.csv");
        var csv = await ExportUi.SaveAsync("n", [Table(3)], _loc);
        Assert.Contains("q.csv", csv);
        Assert.Equal(4, File.ReadAllLines(_ui.SavePath).Length);

        _ui.SavePath = Path.Combine(_dir, "q");
        var xlsx = await ExportUi.SaveAsync("n", [Table(3)], _loc);
        Assert.True(File.Exists(Path.Combine(_dir, "q.xlsx")), xlsx);
    }
}

[CollectionDefinition(nameof(UiPlatformCollection), DisableParallelization = true)]
public sealed class UiPlatformCollection;
