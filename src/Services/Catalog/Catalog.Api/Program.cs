using BuildingBlocks.Health;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using Catalog.Api.Data;
using Catalog.Api.Products;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("catalog-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection(CatalogOptions.SectionName));
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CatalogDb")));

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CatalogDbContext>("postgres", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapProductEndpoints();

await app.MigrateDatabaseAsync<CatalogDbContext>();
await app.RunAsync();
