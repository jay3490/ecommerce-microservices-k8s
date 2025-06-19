namespace Catalog.Api.Products;

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>ISO 4217 code every product is priced in.</summary>
    public string Currency { get; set; } = "USD";
}
