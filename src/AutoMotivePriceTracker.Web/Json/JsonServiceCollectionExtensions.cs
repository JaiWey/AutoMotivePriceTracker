using System.Text.Json.Serialization;

namespace AutoMotivePriceTracker.Web.Json;

public static class JsonServiceCollectionExtensions
{
    /// <summary>Enums as strings, DateTimes always UTC with a Z suffix.</summary>
    public static IServiceCollection AddApiJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
        });
}
