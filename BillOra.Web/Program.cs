using BillOra.Application.Common.Interfaces;
using BillOra.Infrastructure.Services;
using BillOra.Persistence;
using BillOra.Persistence.Identity;
using BillOra.Persistence.Seed;
using BillOra.Web.Middleware;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Database (Sqlite for single-store / dev, SQL Server for multi-store / prod) ----
var provider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
builder.Services.AddDbContext<BillOraDbContext>((sp, options) =>
{
    if (provider == "SqlServer")
        options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServerConnection"));
    else
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// ---- Identity ----
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<BillOraDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
});

// AJAX calls (POS checkout) send the antiforgery token via a header rather
// than a form field, so it needs to be told which header to look for.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// ---- Multi-tenant plumbing ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<IActivityLogger, ActivityLogger>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IAccountingService, AccountingService>();
builder.Services.AddScoped<IBatchStockService, BatchStockService>();
builder.Services.AddDataProtection();

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // Sale -> SaleItems -> Sale is a real navigation cycle (used by
        // ResumeHeldBill); avoid a serialization crash instead of flattening
        // every JSON response to DTOs for this scaffold pass.
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

var app = builder.Build();

// ---- Seed roles / demo tenant / dev login on startup ----
using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Blocks non-Developer users once their Company's subscription has lapsed.
app.UseMiddleware<SubscriptionMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
