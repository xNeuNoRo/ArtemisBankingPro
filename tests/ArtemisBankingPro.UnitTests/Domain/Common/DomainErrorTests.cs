using ArtemisBankingPro.Domain.Common.Enums;
using ArtemisBankingPro.Domain.Common.ValueObjects;

namespace ArtemisBankingPro.UnitTests.Domain.Common;

public sealed class DomainErrorTests {
    [Fact]
    public void Validation_CreatesValidationCategory() =>
        DomainError.Validation("A.B", "msg").Category.Should().Be(ErrorCategory.Validation);

    [Fact]
    public void Conflict_CreatesConflictCategory() =>
        DomainError.Conflict("A.B", "msg").Category.Should().Be(ErrorCategory.Conflict);

    [Fact]
    public void Declined_CreatesDeclinedCategory() =>
        DomainError.Declined("A.B", "msg").Category.Should().Be(ErrorCategory.Declined);

    [Fact]
    public void NotFound_CreatesNotFoundCategory() =>
        DomainError.NotFound("A.B", "msg").Category.Should().Be(ErrorCategory.NotFound);

    [Fact]
    public void Unauthorized_CreatesUnauthorizedCategory() =>
        DomainError.Unauthorized("A.B", "msg").Category.Should().Be(ErrorCategory.Unauthorized);

    [Fact]
    public void Forbidden_CreatesForbiddenCategory() =>
        DomainError.Forbidden("A.B", "msg").Category.Should().Be(ErrorCategory.Forbidden);

    [Fact]
    public void PreconditionFailed_CreatesPreconditionFailedCategory() =>
        DomainError.PreconditionFailed("A.B", "msg").Category.Should().Be(ErrorCategory.PreconditionFailed);

    [Fact]
    public void AllFactories_PreserveCodeAndMessage() {
        var error = DomainError.NotFound("User.NotFound", "El usuario no existe.");

        error.Code.Should().Be("User.NotFound");
        error.Message.Should().Be("El usuario no existe.");
    }
}
