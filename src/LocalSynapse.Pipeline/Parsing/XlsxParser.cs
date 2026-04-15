using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LocalSynapse.Core.Diagnostics;
using LocalSynapse.Pipeline.Interfaces;

namespace LocalSynapse.Pipeline.Parsing;

/// <summary>
/// XLSX 파서. 시트별 셀 텍스트를 추출하고, SharedStringTable을 참조한다.
/// </summary>
internal static class XlsxParser
{
    /// <summary>XLSX 파일에서 텍스트를 추출한다.</summary>
    public static ExtractionResult Parse(string filePath)
    {
        long sizeBytes = -1;
        try { sizeBytes = new FileInfo(filePath).Length; }
        catch (Exception sEx) { Debug.WriteLine($"[XlsxParser] Size probe: {sEx.Message}"); }

        var openSw = Stopwatch.StartNew();
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null)
            return ExtractionResult.Ok("");

        var sst = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList();
        openSw.Stop();
        SpeedDiagLog.Log("PARSE_DETAIL",
            "ext", ".xlsx", "stage", "open",
            "time_ms", openSw.ElapsedMilliseconds,
            "sheet_count", sheets?.Count ?? 0, "size_bytes", sizeBytes);
        if (sheets == null || sheets.Count == 0)
            return ExtractionResult.Ok("");

        var sheetsSw = Stopwatch.StartNew();
        var sb = new StringBuilder();
        var sheetIndex = 0;

        foreach (var sheet in sheets)
        {
            sheetIndex++;
            var sheetName = sheet.Name?.Value ?? $"Sheet{sheetIndex}";
            var worksheetPart = workbookPart.GetPartById(sheet.Id!) as WorksheetPart;
            if (worksheetPart == null) continue;

            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData == null) continue;

            var sheetSb = new StringBuilder();

            foreach (var row in sheetData.Elements<Row>())
            {
                var cells = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    cells.Add(GetCellText(cell, sst));
                }
                var line = string.Join("\t", cells).Trim();
                if (!string.IsNullOrEmpty(line))
                    sheetSb.AppendLine(line);
            }

            if (sheetSb.Length > 0)
            {
                sb.AppendLine($"[{sheetName}]");
                sb.Append(sheetSb);
                sb.AppendLine();
            }
        }
        sheetsSw.Stop();
        SpeedDiagLog.Log("PARSE_DETAIL",
            "ext", ".xlsx", "stage", "sheets",
            "time_ms", sheetsSw.ElapsedMilliseconds);

        return ExtractionResult.Ok(sb.ToString(), "sheet", null);
    }

    private static string GetCellText(Cell cell, SharedStringTable? sst)
    {
        if (cell.CellValue == null) return "";
        var value = cell.CellValue.Text;

        if (cell.DataType?.Value == CellValues.SharedString && sst != null)
        {
            if (int.TryParse(value, out var idx))
            {
                var item = sst.ElementAt(idx);
                return item.InnerText;
            }
        }

        return value ?? "";
    }
}
