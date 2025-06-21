using Catalog.Api.Caching;
using Catalog.Api.Data;
using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Catalog.Api.Products;

public static class ProductEndpoints
{
    private const int MaxPageSize = 100;
    private const int MaxLookupIds = 50;

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var products = endpoints.MapGroup("/api/products").WithTags("Products");

        products.MapGet("/", ListProducts);
        products.MapGet("/lookup", LookupProducts);
        products.MapGet("/{id:guid}", GetProduct).WithName("GetProduct");
        products.MapPost("/", CreateProduct);
        products.MapPut("/{id:guid}", UpdateProduct);

        return endpoints;
    }

    private static async Task<IResult> ListProducts(
        CatalogDbContext db, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Products.AsNoTracking().OrderBy(p => p.Name).ThenBy(p => p.Id);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Description, p.Price, p.Currency))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<ProductDto>(items, page, pageSize, total));
    }

    private static async Task<IResult> GetProduct(Guid id, ProductCache cache, CancellationToken cancellationToken)
    {
        var product = await cache.GetAsync(id, cancellationToken);
        return product is null ? Results.NotFound() : Results.Ok(product);
    }

    /// <summary>
    /// Returns the products matching the given ids; unknown ids are simply absent from the
    /// result. Used by the ordering service to price an order in one call.
    /// </summary>
    private static async Task<IResult> LookupProducts(
        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "ids")] Guid[] ids, ProductCache cache, CancellationToken cancellationToken)
    {
        var distinctIds = ids.Distinct().ToArray();
        if (distinctIds.Length is 0 or > MaxLookupIds)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ids"] = [$"Provide between 1 and {MaxLookupIds} product ids."],
            });
        }

        var products = await cache.GetManyAsync(distinctIds, cancellationToken);
        return Results.Ok(products);
    }

    private static async Task<IResult> CreateProduct(
        CreateProductRequest request,
        CatalogDbContext db,
        IOptions<CatalogOptions> options,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var product = Product.Create(
            Guid.NewGuid(), request.Sku, request.Name, request.Description ?? string.Empty,
            request.Price, options.Value.Currency, clock.GetUtcNow());

        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Conflict(new { error = $"A product with SKU '{product.Sku}' already exists." });
        }

        return Results.CreatedAtRoute("GetProduct", new { id = product.Id }, ProductDto.From(product));
    }

    private static async Task<IResult> UpdateProduct(
        Guid id,
        UpdateProductRequest request,
        CatalogDbContext db,
        ProductCache cache,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Results.NotFound();
        }

        product.Update(request.Name, request.Description ?? string.Empty, request.Price, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await cache.InvalidateAsync(product.Id, cancellationToken);

        return Results.Ok(ProductDto.From(product));
    }
}
