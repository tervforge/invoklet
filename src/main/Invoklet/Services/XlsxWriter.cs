using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Invoklet.Services;

/// <summary>Writes sheets as an Excel workbook (.xlsx: a zip of a few XML parts) with no library: text, numbers, booleans and bold headers.</summary>
internal static class XlsxWriter
{
    // The limits of a worksheet; what is beyond them is left out.
    private const int MaxRows = 1_048_576;
    private const int MaxColumns = 16_384;
    private const int MaxCellLength = 32_767;
    private const int BoldStyle = 1;

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Write(Stream output, IReadOnlyList<SheetData> sheets)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Part(zip, "[Content_Types].xml", w =>
        {
            w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
            Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
            Default(w, "xml", "application/xml");
            Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            for (var i = 1; i <= sheets.Count; i++)
                Override(w, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            w.WriteEndElement();
        });
        Part(zip, "_rels/.rels", w =>
        {
            w.WriteStartElement("Relationships", Rels);
            Relationship(w, "rId1", OfficeRels + "/officeDocument", "xl/workbook.xml");
            w.WriteEndElement();
        });
        Part(zip, "xl/workbook.xml", w =>
        {
            w.WriteStartElement("workbook", Main);
            w.WriteAttributeString("xmlns", "r", null, OfficeRels);
            w.WriteStartElement("sheets");
            for (var i = 0; i < sheets.Count; i++)
            {
                w.WriteStartElement("sheet");
                w.WriteAttributeString("name", sheets[i].Name);
                w.WriteAttributeString("sheetId", (i + 1).ToString());
                w.WriteAttributeString("r", "id", OfficeRels, "rId" + (i + 1));
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        });
        Part(zip, "xl/_rels/workbook.xml.rels", w =>
        {
            w.WriteStartElement("Relationships", Rels);
            for (var i = 1; i <= sheets.Count; i++)
                Relationship(w, "rId" + i, OfficeRels + "/worksheet", $"worksheets/sheet{i}.xml");
            Relationship(w, "rId" + (sheets.Count + 1), OfficeRels + "/styles", "styles.xml");
            w.WriteEndElement();
        });
        Part(zip, "xl/styles.xml", Styles);
        for (var i = 0; i < sheets.Count; i++)
        {
            var sheet = sheets[i];
            Part(zip, $"xl/worksheets/sheet{i + 1}.xml", w => Sheet(w, sheet));
        }
    }

    private static void Styles(XmlWriter w)
    {
        // Two cell formats: the default, and bold for a header.
        w.WriteStartElement("styleSheet", Main);
        w.WriteStartElement("fonts"); w.WriteAttributeString("count", "2");
        for (var bold = 0; bold < 2; bold++)
        {
            w.WriteStartElement("font");
            if (bold == 1) w.WriteElementString("b", "");
            w.WriteStartElement("sz"); w.WriteAttributeString("val", "11"); w.WriteEndElement();
            w.WriteStartElement("name"); w.WriteAttributeString("val", "Calibri"); w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteStartElement("fills"); w.WriteAttributeString("count", "2");
        foreach (var pattern in new[] { "none", "gray125" })
        {
            w.WriteStartElement("fill"); w.WriteStartElement("patternFill"); w.WriteAttributeString("patternType", pattern); w.WriteEndElement(); w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteStartElement("borders"); w.WriteAttributeString("count", "1");
        w.WriteStartElement("border");
        foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" }) w.WriteElementString(side, "");
        w.WriteEndElement(); w.WriteEndElement();
        w.WriteStartElement("cellStyleXfs"); w.WriteAttributeString("count", "1");
        Xf(w, 0, withParent: false);
        w.WriteEndElement();
        w.WriteStartElement("cellXfs"); w.WriteAttributeString("count", "2");
        Xf(w, 0, withParent: true);
        Xf(w, 1, withParent: true);
        w.WriteEndElement();
        w.WriteStartElement("cellStyles"); w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle");
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement(); w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Xf(XmlWriter w, int font, bool withParent)
    {
        w.WriteStartElement("xf");
        w.WriteAttributeString("numFmtId", "0");
        w.WriteAttributeString("fontId", font.ToString());
        w.WriteAttributeString("fillId", "0");
        w.WriteAttributeString("borderId", "0");
        if (withParent) w.WriteAttributeString("xfId", "0");
        if (font == 1) w.WriteAttributeString("applyFont", "1");
        w.WriteEndElement();
    }

    private static void Sheet(XmlWriter w, SheetData sheet)
    {
        var rows = sheet.Rows.Take(MaxRows).Select(r => r.Take(MaxColumns).ToList()).ToList();
        w.WriteStartElement("worksheet", Main);

        var widths = new List<int>();
        foreach (var row in rows)
            for (var c = 0; c < row.Count; c++)
            {
                if (widths.Count <= c) widths.Add(0);
                widths[c] = Math.Max(widths[c], Math.Min(Clean(row[c].Text).Length, 60));
            }
        if (widths.Count > 0)
        {
            w.WriteStartElement("cols");
            for (var c = 0; c < widths.Count; c++)
            {
                w.WriteStartElement("col");
                w.WriteAttributeString("min", (c + 1).ToString());
                w.WriteAttributeString("max", (c + 1).ToString());
                w.WriteAttributeString("width", Math.Max(widths[c] + 2, 8).ToString());
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteStartElement("sheetData");
        for (var r = 0; r < rows.Count; r++)
        {
            w.WriteStartElement("row");
            w.WriteAttributeString("r", (r + 1).ToString());
            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = rows[r][c];
                if (cell.Kind == CellKind.Text && Clean(cell.Text).Length == 0) continue; // left blank, as a null is
                w.WriteStartElement("c");
                w.WriteAttributeString("r", ColumnName(c) + (r + 1));
                if (cell.Kind == CellKind.Header) w.WriteAttributeString("s", BoldStyle.ToString());
                switch (cell.Kind)
                {
                    case CellKind.Number:
                        w.WriteElementString("v", cell.Text);
                        break;
                    case CellKind.Boolean:
                        w.WriteAttributeString("t", "b");
                        w.WriteElementString("v", cell.Text == "true" ? "1" : "0");
                        break;
                    default:
                        w.WriteAttributeString("t", "inlineStr");
                        w.WriteStartElement("is");
                        w.WriteStartElement("t");
                        w.WriteAttributeString("xml", "space", null, "preserve");
                        w.WriteString(Clean(cell.Text));
                        w.WriteEndElement();
                        w.WriteEndElement();
                        break;
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteEndElement();
    }

    // A character XML cannot hold makes Excel call the whole file damaged; a cell holds at most 32,767 characters.
    private static string Clean(string text)
    {
        var sb = new StringBuilder(Math.Min(text.Length, MaxCellLength));
        for (var i = 0; i < text.Length && sb.Length < MaxCellLength; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                if (sb.Length + 2 > MaxCellLength) break;
                sb.Append(text[i]).Append(text[++i]);
            }
            else if (XmlConvert.IsXmlChar(text[i])) sb.Append(text[i]);
        }
        return sb.ToString();
    }

    // 0 is A, 25 is Z, 26 is AA.
    internal static string ColumnName(int index)
    {
        var name = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    private static void Part(ZipArchive zip, string path, Action<XmlWriter> write)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        writer.WriteStartDocument(standalone: true);
        write(writer);
        writer.WriteEndDocument();
    }

    private static void Default(XmlWriter w, string extension, string type)
    {
        w.WriteStartElement("Default");
        w.WriteAttributeString("Extension", extension);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Override(XmlWriter w, string part, string type)
    {
        w.WriteStartElement("Override");
        w.WriteAttributeString("PartName", part);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Relationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship");
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }
}
