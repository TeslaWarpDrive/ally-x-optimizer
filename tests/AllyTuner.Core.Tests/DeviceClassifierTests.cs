using AllyTuner.Core.Device;

namespace AllyTuner.Core.Tests;

public class DeviceClassifierTests
{
    private static DeviceIdentity Identity(
        string? manufacturer = "ASUSTeK COMPUTER INC.",
        string? model = null,
        string? sku = null,
        string? family = null,
        string? boardManufacturer = null,
        string? boardProduct = null) =>
        new(manufacturer, model, sku, family, boardManufacturer, boardProduct, "RC73XA.300");

    [Theory]
    [InlineData("RC73XA")]
    [InlineData("ROG Xbox Ally X RC73XA")]
    [InlineData("ROG Xbox Ally X RC73XA_RC73XA")]
    [InlineData("rc73xa")]
    [InlineData("  RC73XA  ")]
    public void Model_with_RC73XA_token_is_supported(string model)
    {
        var result = DeviceClassifier.Classify(Identity(model: model));

        Assert.Equal(DeviceKind.RogXboxAllyX, result.Kind);
        Assert.True(result.IsSupported);
        Assert.Equal("RC73XA", result.MatchedModelCode);
    }

    [Fact]
    public void Baseboard_product_alone_can_identify_the_device()
    {
        var result = DeviceClassifier.Classify(Identity(model: "To be filled by O.E.M.", boardProduct: "RC73XA"));

        Assert.True(result.IsSupported);
    }

    [Fact]
    public void System_sku_alone_can_identify_the_device()
    {
        var result = DeviceClassifier.Classify(Identity(model: null, sku: "RC73XA_RC73XA"));

        Assert.True(result.IsSupported);
    }

    [Fact]
    public void Asus_baseboard_manufacturer_counts_as_asus()
    {
        var result = DeviceClassifier.Classify(Identity(manufacturer: null, boardManufacturer: "ASUSTeK COMPUTER INC.", model: "RC73XA"));

        Assert.True(result.IsSupported);
    }

    [Theory]
    [InlineData("RC73XAB")]
    [InlineData("XRC73XA")]
    [InlineData("RC73XA2")]
    [InlineData("RC73X")]
    public void Lookalike_codes_are_not_supported(string model)
    {
        var result = DeviceClassifier.Classify(Identity(model: model));

        Assert.False(result.IsSupported);
    }

    [Fact]
    public void RC73XA_from_a_non_asus_manufacturer_is_not_supported()
    {
        var result = DeviceClassifier.Classify(Identity(manufacturer: "Contoso", model: "RC73XA"));

        Assert.Equal(DeviceKind.Unsupported, result.Kind);
        Assert.False(result.IsSupported);
    }

    [Theory]
    [InlineData("ROG Ally RC71L", "RC71L")]
    [InlineData("ROG Ally X RC72LA", "RC72LA")]
    public void Other_ally_models_are_detected_but_not_supported(string model, string expectedCode)
    {
        var result = DeviceClassifier.Classify(Identity(model: model));

        Assert.Equal(DeviceKind.OtherRogAlly, result.Kind);
        Assert.False(result.IsSupported);
        Assert.Equal(expectedCode, result.MatchedModelCode);
    }

    [Fact]
    public void Asus_device_mentioning_ally_without_a_code_is_other_ally()
    {
        var result = DeviceClassifier.Classify(Identity(model: "ROG Ally", family: null));

        Assert.Equal(DeviceKind.OtherRogAlly, result.Kind);
        Assert.Null(result.MatchedModelCode);
    }

    [Fact]
    public void Asus_family_field_mentioning_ally_is_other_ally()
    {
        var result = DeviceClassifier.Classify(Identity(model: "Unknown", family: "ROG Ally"));

        Assert.Equal(DeviceKind.OtherRogAlly, result.Kind);
    }

    [Fact]
    public void Ordinary_asus_laptop_is_unsupported()
    {
        var result = DeviceClassifier.Classify(Identity(model: "ROG Zephyrus G14 GA403UV"));

        Assert.Equal(DeviceKind.Unsupported, result.Kind);
    }

    [Fact]
    public void Completely_unknown_identity_is_unsupported()
    {
        var result = DeviceClassifier.Classify(DeviceIdentity.Unknown);

        Assert.Equal(DeviceKind.Unsupported, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
    }

    [Fact]
    public void Null_identity_throws()
    {
        Assert.Throws<ArgumentNullException>(() => DeviceClassifier.Classify(null!));
    }
}
