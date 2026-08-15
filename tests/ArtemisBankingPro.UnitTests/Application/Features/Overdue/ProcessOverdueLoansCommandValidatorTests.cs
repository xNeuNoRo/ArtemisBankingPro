using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.Validators;
using ArtemisBankingPro.Application.Interfaces.Time;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Overdue;

public sealed class ProcessOverdueLoansCommandValidatorTests {
    private static ProcessOverdueLoansCommandValidator CreateValidator() {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(item => item.Today).Returns(new DateOnly(2026, 8, 14));
        return new ProcessOverdueLoansCommandValidator(clock.Object);
    }

    [Fact]
    public void Validate_ValidDateAndBatch_Succeeds() {
        var result = CreateValidator().Validate(
            new ProcessOverdueLoansCommand(new DateOnly(2026, 8, 14))
        );

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveBatch_Fails(int batchSize) {
        var result = CreateValidator().Validate(
            new ProcessOverdueLoansCommand(new DateOnly(2026, 8, 14), batchSize)
        );

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_FutureDate_Fails() {
        var result = CreateValidator().Validate(
            new ProcessOverdueLoansCommand(new DateOnly(2026, 8, 15))
        );

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_PastDate_Fails() {
        var result = CreateValidator().Validate(
            new ProcessOverdueLoansCommand(new DateOnly(2026, 8, 13))
        );

        result.IsValid.Should().BeFalse();
    }
}
