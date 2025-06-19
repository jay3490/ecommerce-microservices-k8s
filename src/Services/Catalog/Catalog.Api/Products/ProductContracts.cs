using Catalog.Api.Domain;

namespace Catalog.Api.Products;

public sealed record ProductDto(
    Guid Id, string Sku, string Name, string Description, decimal Price, string Currency)
{
    public static ProductDto From(Product product) =>
        new(product.Id, product.Sku, product.Name, product.Description, product.Price, product.Currency);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record CreateProductRequest(string Sku, string Name, string? Description, decimal Price)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Sku) || Sku.Length > Product.MaxSkuLength)
        {
            errors[nameof(Sku)] = [$"SKU is required and must be at most {Product.MaxSkuLength} characters."];
        }

        ProductValidation.ValidateDetails(errors, Name, Description, Price);
        return errors;
    }
}

public sealed record UpdateProductRequest(string Name, string? Description, decimal Price)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        ProductValidation.ValidateDetails(errors, Name, Description, Price);
        return errors;
    }
}

internal static class ProductValidation
{
    public static void ValidateDetails(Dictionary<string, string[]> errors, string name, string? description, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > Product.MaxNameLength)
        {
            errors["Name"] = [$"Name is required and must be at most {Product.MaxNameLength} characters."];
        }

        if (description is { Length: > Product.MaxDescriptionLength })
        {
            errors["Description"] = [$"Description must be at most {Product.MaxDescriptionLength} characters."];
        }

        if (price <= 0 || decimal.Round(price, 2) != price)
        {
            errors["Price"] = ["Price must be positive with at most two decimal places."];
        }
    }
}
