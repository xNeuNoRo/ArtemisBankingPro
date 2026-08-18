using ArtemisBankingPro.Application.Common.Exceptions;
using ArtemisBankingPro.Application.Features.Client.Commands;
using ArtemisBankingPro.Application.Features.Client.Handlers;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Persistence;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Domain.Accounts.Beneficiaries.Entities;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Features.Client;

/// <summary>
/// Verifica la eliminación de beneficiarios (spec §2353-§2371): solo elimina
/// la relación del cliente autenticado; un beneficiario ajeno se rechaza con
/// ForbiddenAccessException y uno inexistente con NotFound.
/// </summary>
public sealed class RemoveBeneficiaryCommandHandlerTests {
    private sealed class Fixture {
        public Mock<IBeneficiaryRepository> Beneficiaries { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();

        public Fixture(string ownerUserId = "client-1") {
            CurrentUser.SetupGet(user => user.UserId).Returns(ownerUserId);
            UnitOfWork
                .Setup(unit => unit.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Result>>>(),
                    It.IsAny<System.Data.IsolationLevel>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (Func<CancellationToken, Task<Result>> operation,
                        System.Data.IsolationLevel _,
                        CancellationToken ct) => operation(ct)
                );
        }

        public RemoveBeneficiaryCommandHandler Handler =>
            new(Beneficiaries.Object, UnitOfWork.Object, CurrentUser.Object);
    }

    private static Beneficiary NewBeneficiary(int id = 7, string owner = "client-1") {
        Beneficiary beneficiary = Beneficiary
            .Create(
                owner,
                42,
                new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(-4)))
            .Value;
        typeof(Beneficiary)
            .GetProperty(nameof(Beneficiary.Id))!
            .GetSetMethod(true)!
            .Invoke(beneficiary, [id]);
        return beneficiary;
    }

    [Fact]
    public async Task Handle_OwnBeneficiary_DeletesIt() {
        var fixture = new Fixture();
        var beneficiary = NewBeneficiary();
        fixture.Beneficiaries
            .Setup(repository => repository.GetByIdAsync(
                beneficiary.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(beneficiary);

        var result = await fixture.Handler.Handle(
            new RemoveBeneficiaryCommand(beneficiary.Id),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        fixture.Beneficiaries.Verify(
            repository => repository.Delete(beneficiary),
            Times.Once
        );
    }

    [Fact]
    public async Task Handle_UnknownBeneficiary_ReturnsNotFound() {
        var fixture = new Fixture();
        fixture.Beneficiaries
            .Setup(repository => repository.GetByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Beneficiary?)null);

        var result = await fixture.Handler.Handle(
            new RemoveBeneficiaryCommand(99),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Be("El beneficiario indicado no existe.");
    }

    [Fact]
    public async Task Handle_OtherClientsBeneficiary_ThrowsForbidden() {
        var fixture = new Fixture();
        var beneficiary = NewBeneficiary(owner: "client-2");
        fixture.Beneficiaries
            .Setup(repository => repository.GetByIdAsync(
                beneficiary.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(beneficiary);

        var act = async () => await fixture.Handler.Handle(
            new RemoveBeneficiaryCommand(beneficiary.Id),
            CancellationToken.None
        );

        (await act.Should().ThrowAsync<ForbiddenAccessException>())
            .WithMessage("El beneficiario no pertenece al cliente autenticado.");
    }
}
