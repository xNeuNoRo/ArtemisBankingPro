using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Common;

public sealed class ResultTests {
    [Fact]
    public void Success_HasNoError() {
        Result result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_RequiresAnError() {
        Action action = () => Result.Failure(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Value_OnFailedResult_Throws() {
        Result<int> result = Result.Failure<int>(
            DomainError.Validation("Test.Error", "Failure"));

        Action action = () => _ = result.Value;

        action.Should().Throw<InvalidOperationException>();
    }
}
