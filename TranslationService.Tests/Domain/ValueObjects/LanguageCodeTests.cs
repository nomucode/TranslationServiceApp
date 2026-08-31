using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Domain.ValueObjects;

public sealed class LanguageCodeTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("pt-br")]
    [InlineData("zh-Hans")]
    public void Create_ShouldAcceptValidBcp47Codes(string candidate)
    {
        LanguageCode.Create(candidate).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("e")]
    [InlineData("english!")]
    [InlineData("es_ES")]
    public void Create_ShouldRejectInvalidCodes(string? candidate)
    {
        LanguageCode.Create(candidate).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldNormalizeToLowercase()
    {
        // La normalización hace que la regla de negocio "detectado == destino" sea
        // insensible a mayúsculas sin tener que recordarlo en cada comparación.
        LanguageCode.Create("ES").Value.Should().Be(LanguageCode.Spanish);
    }

    [Fact]
    public void Spanish_ShouldBeTheWellKnownEsCode()
    {
        LanguageCode.Spanish.Value.Should().Be("es");
    }

    [Fact]
    public void DifferentCodes_ShouldNotBeEqual()
    {
        LanguageCode.Create("en").Value.Should().NotBe(LanguageCode.Spanish);
    }
}
