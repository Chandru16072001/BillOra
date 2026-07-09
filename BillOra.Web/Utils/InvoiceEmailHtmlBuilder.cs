using BillOra.Domain.Entities;

namespace BillOra.Web.Utils;

public static class InvoiceEmailHtmlBuilder
{
    public static string BuildSaleInvoiceHtml(Store store, Sale sale, IEnumerable<SaleItem> lines)
    {
        var rows = string.Join("", lines.Select(l =>
            $"<tr><td>{l.Item?.Name}</td><td style='text-align:right'>{l.Quantity}</td><td style='text-align:right'>₹{l.LineTotal:N2}</td></tr>"));

        return $@"
            <div style='font-family:Arial,sans-serif;max-width:480px;margin:auto;'>
                <h2>{store.Name}</h2>
                <p>Invoice: <strong>{sale.InvoiceNumber}</strong><br/>
                   Date: {sale.SaleDate:dd MMM yyyy hh:mm tt}</p>
                <table style='width:100%;border-collapse:collapse;' cellpadding='6'>
                    <thead><tr style='background:#f5f6fb;'><th style='text-align:left'>Item</th><th>Qty</th><th>Amount</th></tr></thead>
                    <tbody>{rows}</tbody>
                </table>
                <h3 style='text-align:right'>Total: ₹{sale.GrandTotal:N2}</h3>
                <p style='color:#666'>Thank you for shopping with {store.Name}!</p>
            </div>";
    }
}
