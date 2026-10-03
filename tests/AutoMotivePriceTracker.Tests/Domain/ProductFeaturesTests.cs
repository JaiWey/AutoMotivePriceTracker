using static AutoMotivePriceTracker.Tests.Support.Builders;

namespace AutoMotivePriceTracker.Tests.Domain;

public class ProductFeaturesTests
{
    [Fact]
    public void SetFeatures_trims_drops_blanks_and_stamps_the_time()
    {
        var p = Product();

        p.SetFeatures(["  Hyspec ", "", "   ", "5L"], T0);

        Assert.Equal(["Hyspec", "5L"], p.Features);
        Assert.Equal(T0, p.FeaturesScrapedAt);
    }

    [Fact]
    public void Features_are_untouched_by_a_price_rescrape()
    {
        var existing = Product(price: 10m);
        existing.SetFeatures(["Hyspec"], T0);

        existing.ApplyScrape(Product(price: 8m), T0.AddDays(1));

        Assert.Equal(["Hyspec"], existing.Features);
        Assert.Equal(T0, existing.FeaturesScrapedAt);
    }
}
