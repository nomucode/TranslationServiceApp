using TranslationService.Domain.Translations.ValueObjects;

namespace TranslationService.Tests.Domain.ValueObjects;

public sealed class SourceTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_ShouldFailForBlankInput(string? candidate)
    {
        var result = SourceText.Create(candidate);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SourceText.Empty");
    }

    [Fact]
    public void Create_ShouldFailWhenExceedingTheAzureLimit()
    {
        var tooLong = new string('a', SourceText.MaxLength + 1);

        var result = SourceText.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SourceText.TooLong");
    }

    [Fact]
    public void Create_ShouldAcceptTextExactlyAtTheLimit()
    {
        var atLimit = new string('a', SourceText.MaxLength);

        SourceText.Create(atLimit).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldTrimSurroundingWhitespace()
    {
        var result = SourceText.Create("  Hello world  ");

        result.Value.Value.Should().Be("Hello world");
    }

    [Fact]
    public void TwoTextsWithTheSameValue_ShouldBeEqual()
    {
        SourceText.Create("Hola").Value.Should().Be(SourceText.Create("Hola").Value);
    }
}
