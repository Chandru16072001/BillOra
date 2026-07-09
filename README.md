# BillOra - Multi-Tenant Billing & POS

A multi-tenant billing and POS platform built on **ASP.NET Core 8 MVC**.

## Setup

Requires the **.NET 8 SDK**.

```bash
cd BillOra.Web
dotnet restore
dotnet ef migrations add InitialCreate --project ../BillOra.Persistence --startup-project .
dotnet ef database update --project ../BillOra.Persistence --startup-project .
dotnet run
```

If you have an existing database from an earlier version, delete `billora.db`
and the `Migrations` folder under `BillOra.Persistence` first, then redo the
two `dotnet ef` commands — this pass added several new tables (Sales
Returns, Accounts ledger).

## Seeded logins

| Role | Email | Password |
|---|---|---|
| Developer | dev@billora.local | Dev@12345 |
| Store Admin | admin@billora.local | Admin@12345 |
| Cashier | cashier@billora.local | Cashier@12345 |

## What's new in this pass

### Sales Details screen (`/Sales`)
Search sales by date range, customer name, or invoice number. Click through
to a full invoice view with **View**, **Print**, **Email**, and — Store
Admin only — **Modify**, which lets you edit line items and quantities on an
already-saved invoice. Modifying correctly reverses and reapplies the stock
impact and re-posts the corrected amount to the Accounts ledger, so nothing
gets double-counted.

### Sales Return module (`/SalesReturn`)
Search the original invoice by number, pick which items and quantities to
return, give a reason, and choose **Load Stock: Yes/No** — Yes puts the
quantity back into sellable inventory, No doesn't (e.g. damaged goods).
Refund is calculated automatically. A return automatically:
- updates inventory per the Load Stock choice and logs it to Stock History
- reduces the customer's outstanding balance if applicable
- posts a Debit to the Mini Accounts ledger
- writes an audit log entry
- supports Print and Email of the return receipt
- has its own filterable report (date range / customer / invoice / item)
  with Excel export

### Mini Accounts Module (`/Accounts`)
- **Transaction entry**: date, name, amount, Credit/Debit, reason, category,
  payment method, reference number, notes, optional file attachment.
- **Account History**: search/filter by date range, type, category, payment
  method; manual entries can be edited inline; Excel export.
- **Balance Sheet**: Total Credits, Total Debits, Current Balance, and a
  Profit/Loss summary, broken down by category on each side.
- **Fully automatic**: every financial event elsewhere in the app posts here
  by itself — Sales Invoice (Credit), Purchase/GRN (Debit), Sales Return
  (Debit), and editing a sale re-posts the corrected amount. No manual entry
  needed for those; the entry screen is for things like expenses, income,
  and opening balance that don't have their own module.

### Reports (`/Reports`)
A real reports hub, not just the old dashboard. Every report below supports
**Excel export** (CSV, opens directly in Excel) and date-range filtering
where relevant:
- Sales Report, Item-wise Sales, Category-wise Sales, Payment Report
- Inventory/Stock Report, GRN Report, Purchase Report (vendor-wise)
- Customer Report, Vendor Report, Outstanding Report
- GST Report, Profit & Loss Report
- User Activity Report / Audit Log
- Sales Return Report (linked from the hub, lives under Sales Return)
- Account Ledger, Balance Sheet (linked from the hub, live under Accounts —
  these are just filtered views of the same ledger, so Cash Book / Day Book
  / Credit Report / Debit Report / Expense Report / Income Report /
  Transaction History Report are all one filter away on `/Accounts/History`
  rather than separate screens)

All reports share one code pattern (a `(headers, rows)` builder feeding both
the on-screen table and the CSV export), so adding another report type is a
short, mechanical addition — worth knowing if you want more of the "many
other useful business reports" mentioned.

## Everything from the previous pass is still in place
Staff Master + RBAC + licensing, Settings (GST/printer/email config), item
images, automatic invoice emailing, and the mobile-first UI pass.

## What's still a natural next step
- Purchase Return module (mirrors Sales Return; entities/patterns are ready)
- True .xlsx export (currently CSV, which opens in Excel natively but isn't
  a native workbook) if you want charts/formatting in the exported file
- Multi-store switching UI for a Store Admin managing more than one store
- Low-stock / subscription-expiry notification banner

## Notes on this build
I wrote all source files directly rather than compiling in this sandbox (no
NuGet access here). Please run `dotnet restore` first and send me the exact
error text if anything doesn't build. Also worth a clean `bin`/`obj` delete +
rebuild after pulling this update.
