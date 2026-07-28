using BillOra.Domain.Entities;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace BillOra.Infrastructure.Services;

// Draws a simple one-page A4 invoice PDF - deliberately plain (no logos or
// fancy layout) because this only needs to be legible on a phone screen
// after a customer opens it from WhatsApp, not print-shop quality. Uses
// PdfSharpCore (MIT licensed, genuinely free regardless of company size -
// picked over alternatives with revenue-based free tiers, since $0 cost
// was a hard requirement). Lives in Infrastructure (not Web/Utils) because
// WhatsAppCloudApiService needs it and Infrastructure cannot reference Web.
public static class InvoicePdfGenerator
{
    private const string FontName = "InvoiceFont";
    private static bool _resolverRegistered;

    public static byte[] Generate(Store store, Sale sale, IEnumerable<SaleItem> lines)
    {
        EnsureFontResolverRegistered();

        var document = new PdfDocument();
        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;

        using var gfx = XGraphics.FromPdfPage(page);
        var fontTitle = new XFont(FontName, 16, XFontStyle.Bold);
        var fontHeader = new XFont(FontName, 10, XFontStyle.Bold);
        var fontNormal = new XFont(FontName, 10, XFontStyle.Regular);
        var fontSmall = new XFont(FontName, 8, XFontStyle.Regular);

        double margin = 40;
        double width = page.Width - margin * 2;
        double y = margin;

        gfx.DrawString(store.Name, fontTitle, XBrushes.Black, new XRect(margin, y, width, 20), XStringFormats.TopLeft);
        y += 22;
        if (!string.IsNullOrEmpty(store.Address))
        {
            gfx.DrawString(store.Address, fontNormal, XBrushes.Black, new XRect(margin, y, width, 16), XStringFormats.TopLeft);
            y += 16;
        }
        var contactLine = string.Join("   |   ", new[] { store.Phone, store.GstNumber != null ? $"GSTIN: {store.GstNumber}" : null }
            .Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(contactLine))
        {
            gfx.DrawString(contactLine, fontSmall, XBrushes.Gray, new XRect(margin, y, width, 14), XStringFormats.TopLeft);
            y += 18;
        }

        gfx.DrawLine(XPens.Black, margin, y, page.Width - margin, y);
        y += 10;

        gfx.DrawString($"Invoice: {sale.InvoiceNumber}", fontHeader, XBrushes.Black, new XRect(margin, y, width / 2, 16), XStringFormats.TopLeft);
        gfx.DrawString(sale.SaleDate.ToLocalTime().ToString("dd MMM yyyy hh:mm tt"), fontNormal, XBrushes.Black, new XRect(margin + width / 2, y, width / 2, 16), XStringFormats.TopRight);
        y += 18;
        gfx.DrawString($"Customer: {(sale.Customer?.Name ?? "Walk-in")}", fontNormal, XBrushes.Black, new XRect(margin, y, width, 16), XStringFormats.TopLeft);
        y += 24;

        double col1 = margin, col2 = margin + width * 0.45, col3 = margin + width * 0.62, col4 = margin + width * 0.80;
        gfx.DrawRectangle(XBrushes.WhiteSmoke, margin, y, width, 18);
        gfx.DrawString("Item", fontHeader, XBrushes.Black, new XPoint(col1 + 4, y + 13));
        gfx.DrawString("Qty", fontHeader, XBrushes.Black, new XPoint(col2 + 4, y + 13));
        gfx.DrawString("Price", fontHeader, XBrushes.Black, new XPoint(col3 + 4, y + 13));
        gfx.DrawString("Amount", fontHeader, XBrushes.Black, new XPoint(col4 + 4, y + 13));
        y += 18;

        foreach (var line in lines)
        {
            gfx.DrawString(line.Item?.Name ?? "", fontNormal, XBrushes.Black, new XPoint(col1 + 4, y + 13));
            gfx.DrawString(line.Quantity.ToString("0.##"), fontNormal, XBrushes.Black, new XPoint(col2 + 4, y + 13));
            gfx.DrawString(line.UnitPrice.ToString("N2"), fontNormal, XBrushes.Black, new XPoint(col3 + 4, y + 13));
            gfx.DrawString(line.LineTotal.ToString("N2"), fontNormal, XBrushes.Black, new XPoint(col4 + 4, y + 13));
            y += 16;

            if (y > page.Height - 160) // simple overflow guard - keeps this generator dependency-free (no pagination library)
            {
                gfx.DrawString("(continued items truncated - see full invoice in-store)", fontSmall, XBrushes.Gray, new XPoint(col1 + 4, y + 13));
                y += 16;
                break;
            }
        }

        y += 8;
        gfx.DrawLine(XPens.Black, margin, y, page.Width - margin, y);
        y += 10;

        DrawTotalLine(gfx, fontNormal, "Subtotal", sale.SubTotal, margin, width, ref y);
        if (sale.DiscountAmount > 0) DrawTotalLine(gfx, fontNormal, "Discount", -sale.DiscountAmount, margin, width, ref y);
        if (sale.TaxAmount > 0) DrawTotalLine(gfx, fontNormal, "Tax (GST)", sale.TaxAmount, margin, width, ref y);
        DrawTotalLine(gfx, fontHeader, "Grand Total", sale.GrandTotal, margin, width, ref y);

        y += 20;
        gfx.DrawString("Thank you for your business!", fontSmall, XBrushes.Gray, new XRect(margin, y, width, 14), XStringFormats.TopCenter);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static void DrawTotalLine(XGraphics gfx, XFont font, string label, decimal amount, double margin, double width, ref double y)
    {
        gfx.DrawString(label, font, XBrushes.Black, new XPoint(margin + width - 160, y + 13));
        gfx.DrawString($"Rs. {amount:N2}", font, XBrushes.Black, new XPoint(margin + width - 60, y + 13));
        y += 16;
    }

    private static void EnsureFontResolverRegistered()
    {
        if (_resolverRegistered) return;
        PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = new InvoiceFontResolver();
        _resolverRegistered = true;
    }

    // Reads a TTF directly from well-known OS locations rather than requiring
    // a font file to be bundled with the project. See the setup guide if
    // none of these paths exist on your server (e.g. a minimal Linux
    // container) - the fix is a one-line package install.
    private class InvoiceFontResolver : PdfSharpCore.Fonts.IFontResolver
    {
        private static readonly string[] CandidatePaths =
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",              // most Linux servers/containers (fonts-dejavu-core)
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            @"C:\Windows\Fonts\arial.ttf",                                   // Windows
            "/System/Library/Fonts/Supplemental/Arial.ttf"                  // macOS
        };

	public string DefaultFontName => FontName;

        public byte[] GetFont(string faceName)
        {
            var path = CandidatePaths.FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException(
                    "No usable TTF font found for PDF generation. On Ubuntu/Debian, run: " +
                    "sudo apt-get install -y fonts-dejavu-core");
            return File.ReadAllBytes(path);
        }

        public PdfSharpCore.Fonts.FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => new PdfSharpCore.Fonts.FontResolverInfo(FontName);
    }
}
