namespace PrototypeManager.Web;

public sealed record PrototypeHostingPlanOption(
    string Sku,
    string Name,
    string Summary);

public static class PrototypeHostingPlans
{
    public const string FreeSku = "F1";

    public static IReadOnlyList<PrototypeHostingPlanOption> HigherPlans { get; } =
    [
        new(
            "B1",
            "Basic B1",
            "Dedicated Basic tier. Better for heavier research sessions than Free, still cost-conscious."),
        new(
            "B2",
            "Basic B2",
            "More CPU and memory than B1 for sustained concurrent research activity.")
    ];

    public static PrototypeHostingPlanOption? Find(string sku) =>
        HigherPlans.FirstOrDefault(plan =>
            string.Equals(plan.Sku, sku, StringComparison.OrdinalIgnoreCase));
}
