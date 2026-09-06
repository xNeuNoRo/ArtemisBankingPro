using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;
using ArtemisBankingPro.Application.Common.Behaviors;
using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace ArtemisBankingPro.UnitTests.Application.Common.Behaviors;

public sealed record TestRequest(string Value) : IRequest<Result<Unit>>;

public sealed class ValidationBehaviorTests {
    private static MessageHandlerDelegate<TestRequest, Result<Unit>> OkDelegate() =>
        (_, _) => ValueTask.FromResult(Result.Success(Unit.Value));

    [Fact]
    public async Task Handle_WithoutValidators_InvokesNext() {
        var behavior = new ValidationBehavior<TestRequest, Result<Unit>>([]);
        bool invoked = false;

        MessageHandlerDelegate<TestRequest, Result<Unit>> next = (_, _) => {
            invoked = true;
            return ValueTask.FromResult(Result.Success(Unit.Value));
        };

        var result = await behavior.Handle(new TestRequest("ok"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithValidValidators_InvokesNext() {
        var validator = new Mock<IValidator<TestRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var behavior = new ValidationBehavior<TestRequest, Result<Unit>>([validator.Object]);
        bool invoked = false;

        MessageHandlerDelegate<TestRequest, Result<Unit>> next = (_, _) => {
            invoked = true;
            return ValueTask.FromResult(Result.Success(Unit.Value));
        };

        var result = await behavior.Handle(new TestRequest("ok"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithValidationFailures_ThrowsValidationException() {
        var validator = new Mock<IValidator<TestRequest>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("Value", "Value is required.")]));

        var behavior = new ValidationBehavior<TestRequest, Result<Unit>>([validator.Object]);

        Func<Task> act = () => behavior
            .Handle(new TestRequest(""), OkDelegate(), CancellationToken.None)
            .AsTask();

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == "Value");
    }

    [Fact]
    public async Task Handle_WithMultipleValidators_AggregatesAllFailures() {
        var first = new Mock<IValidator<TestRequest>>();
        first
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("A", "A failed.")]));

        var second = new Mock<IValidator<TestRequest>>();
        second
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("B", "B failed.")]));

        var behavior = new ValidationBehavior<TestRequest, Result<Unit>>([first.Object, second.Object]);

        Func<Task> act = () => behavior
            .Handle(new TestRequest(""), OkDelegate(), CancellationToken.None)
            .AsTask();

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
    }
}
