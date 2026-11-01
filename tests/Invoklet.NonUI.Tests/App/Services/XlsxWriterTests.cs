using System.IO.Compression;
using System.Xml.Linq;
using Invoklet.Services;
using Xunit;

namespace Invoklet.NonUI.Tests.App.Services;

public sealed class XlsxWriterTests
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static ZipArchive Write(params SheetData[] sheets)
    {
        var stream = new MemoryStream();
        XlsxWriter.Write(stream, sheets);
        stream.Position = 0;
        return new ZipArchive(stream);
    }

    private static SheetData Sheet(string name, params SheetCell[][] rows) => new(name, rows);

    [Fact]
    public void Write_Sheets_MakesAWorkbookWhoseEveryPartIsWellFormedXml()
    {
        // Act
        using var zip = Write(Sheet("A", [new SheetCell("h", CellKind.Header)]), Sheet("B", [new SheetCell("x", CellKind.Text)]));

        // Assert
        Assert.All(zip.Entries, entry => XDocument.Load(entry.Open()));
        var workbook = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open());
        Assert.Equal(["A", "B"], workbook.Descendants(Main + "sheet").Select(s => (string)s.Attribute("name")!));
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));
    }

    [Fact]
    public void Write_Cells_KeepTheirKindsAndPositions()
    {
        // Act
        using var zip = Write(Sheet("S",
            [new SheetCell("Name", CellKind.Header), new SheetCell("Age", CellKind.Header)],
            [new SheetCell("a < b & c", CellKind.Text), new SheetCell("31.5", CellKind.Number), new SheetCell("true", CellKind.Boolean)]));

        // Assert
        var cells = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).Descendants(Main + "c").ToDictionary(c => (string)c.Attribute("r")!);
        Assert.Equal("1", (string)cells["A1"].Attribute("s")!);
        Assert.Equal("a < b & c", cells["A2"].Value);
        Assert.Equal("31.5", cells["B2"].Element(Main + "v")!.Value);
        Assert.Equal(("b", "1"), ((string)cells["C2"].Attribute("t")!, cells["C2"].Element(Main + "v")!.Value));
    }

    [Fact]
    public void Write_ACharacterXmlCannotHold_IsLeftOutAndAHugeCellCut()
    {
        // Act
        using var zip = Write(Sheet("S", [new SheetCell("a\u0001b\ud800c", CellKind.Text), new SheetCell(new string('x', 40_000), CellKind.Text)]));

        // Assert
        var cells = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).Descendants(Main + "c").ToList();
        Assert.Equal("abc", cells[0].Value);
        Assert.Equal(32_767, cells[1].Value.Length);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ColumnName_Index_IsTheLettersExcelUses(int index, string expected) => Assert.Equal(expected, XlsxWriter.ColumnName(index));
}
