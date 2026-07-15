using BillOra.Domain.Enums;

namespace BillOra.Web.Utils;

public record LineGstResult(decimal TaxableValue, decimal TaxAmount, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount, decimal LineTotal);

// Central place for GST math so POS checkout, Sale edit, and Sales Return
// all compute tax the same way. Handles GST-inclusive vs GST-exclusive
// pricing, and splits into CGST+SGST (same state) or IGST (different state)
// per standard Indian GST rules.
public static class GstCalculator
{
    public static LineGstResult Calculate(decimal unitPrice, decimal quantity, decimal discount,
        decimal gstPercent, GstPriceType priceType, bool gstEnabled, bool isInterState)
    {
        if (!gstEnabled || gstPercent <= 0)
        {
            var flatTaxable = (unitPrice * quantity) - discount;
            return new LineGstResult(flatTaxable, 0, 0, 0, 0, flatTaxable);
        }

        decimal taxableValue, taxAmount, lineTotal;

        if (priceType == GstPriceType.Inclusive)
        {
            // The entered price already includes GST - extract it back out.
            var gross = (unitPrice * quantity) - discount;
            taxableValue = gross / (1 + gstPercent / 100m);
            taxAmount = gross - taxableValue;
            lineTotal = gross;
        }
        else
        {
            // GST is added on top of the entered price.
            taxableValue = (unitPrice * quantity) - discount;
            taxAmount = taxableValue * gstPercent / 100m;
            lineTotal = taxableValue + taxAmount;
        }

        decimal cgst = 0, sgst = 0, igst = 0;
        if (isInterState)
        {
            igst = taxAmount;
        }
        else
        {
            cgst = Math.Round(taxAmount / 2, 2);
            sgst = taxAmount - cgst; // avoids a 1-paisa rounding mismatch between the two halves
        }

        return new LineGstResult(taxableValue, taxAmount, cgst, sgst, igst, lineTotal);
    }

    // Same-state => CGST+SGST. Different state => IGST. Unknown customer
    // state (walk-in, or address not filled in) defaults to same-state,
    // since that's the store's own default tax treatment.
    public static bool IsInterState(string? storeState, string? customerState)
    {
        if (string.IsNullOrWhiteSpace(storeState) || string.IsNullOrWhiteSpace(customerState)) return false;
        return !string.Equals(storeState.Trim(), customerState.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
