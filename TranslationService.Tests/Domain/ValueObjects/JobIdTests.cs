using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Domain.ValueObjects;

public sealed class JobIdTests
{
    [Fact]
    public void New_ShouldProduceANonEmptyIdentifier()
    {
        var id = JobId.New();

        id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task New_ShouldProduceTimeOrderableIdentifiers()
    {
        // Se usa UUIDv7: los 48 bits de cabecera son la marca de tiempo en milisegundos, de
        // modo que los ids son ordenables por tiempo y no fragmentan un índice clúster.
        // Dentro del mismo milisegundo el resto del GUID es aleatorio, así que la garantía
        // que se verifica es que el prefijo temporal nunca decrece.
        var prefixes = new List<string>();
        for (var batch = 0; batch < 3; batch++)
        {
            prefixes.AddRange(Enumerable.Range(0, 20).Select(_ => TimestampPrefixOf(JobId.New())));
            await Task.Delay(5);
        }

        prefixes.Should().BeInAscendingOrder(StringComparer.Ordinal);
        prefixes.Distinct().Should().HaveCountGreaterThan(1, "el prefijo debe avanzar con el reloj");
    }

    private static string TimestampPrefixOf(JobId id) => id.Value.ToString()[..13];

    [Fact]
    public void Create_ShouldFailForAnEmptyGuid()
    {
        var result = JobId.Create(Guid.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldSucceedForAValidGuid()
    {
        var guid = Guid.NewGuid();

        var result = JobId.Create(guid);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(guid);
    }

    [Theory]
    [InlineData("no-soy-un-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryParse_ShouldFailForInvalidInput(string candidate)
    {
        var result = JobId.TryParse(candidate);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TwoIdsWithTheSameValue_ShouldBeEqual()
    {
        var guid = Guid.NewGuid();

        JobId.Create(guid).Value.Should().Be(JobId.Create(guid).Value);
    }
}
