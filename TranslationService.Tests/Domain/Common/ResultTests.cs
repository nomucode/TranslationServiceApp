using TranslationService.Domain.Common;

namespace TranslationService.Tests.Domain.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_ShouldNotCarryAnError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_ShouldCarryTheProvidedError()
    {
        var error = Error.Validation("Test.Code", "descripción");

        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void SuccessOfT_ShouldExposeTheValue()
    {
        var result = Result.Success(42);

        result.Value.Should().Be(42);
    }

    [Fact]
    public void FailureOfT_ShouldThrowWhenValueIsAccessed()
    {
        var result = Result.Failure<int>(Error.NotFound("X", "y"));

        var access = () => result.Value;

        access.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitConversion_ShouldProduceASuccessfulResult()
    {
        Result<string> result = "hola";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hola");
    }

    [Fact]
    public void Match_ShouldInvokeTheSuccessBranchOnSuccess()
    {
        var result = Result.Success(10);

        var matched = result.Match(value => $"ok:{value}", error => $"ko:{error.Code}");

        matched.Should().Be("ok:10");
    }

    [Fact]
    public void Match_ShouldInvokeTheFailureBranchOnFailure()
    {
        var result = Result.Failure<int>(Error.Failure("Boom", "algo falló"));

        var matched = result.Match(value => $"ok:{value}", error => $"ko:{error.Code}");

        matched.Should().Be("ko:Boom");
    }

    [Fact]
    public void Success_ShouldRejectBeingBuiltWithAnError()
    {
        // Blinda la invariante del propio Result: un éxito con error es un estado imposible.
        var construction = () => Result.Failure(Error.None);

        construction.Should().Throw<InvalidOperationException>();
    }
}
