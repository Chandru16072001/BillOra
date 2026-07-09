using System.Text;

namespace BillOra.Web.Utils;

// Every report's "Export to Excel" button uses this. CSV (with a UTF-8 BOM
// so Excel renders special characters like ₹ correctly) opens directly in
// Excel without needing an extra charting/spreadsheet NuGet package - kept
// deliberately dependency-free since this sandbox can't restore new packages
// to verify them, and CSV-into-Excel is a completely standard workflow.
public static class CsvExportHelper
{
    public static byte[] ToCsv(IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return bom.Concat(bytes).ToArray();
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
