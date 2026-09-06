using System.Security.Cryptography;
using System.Text;
using ArtemisBankingPro.Application.Common.Interfaces;
using ArtemisBankingPro.Application.Interfaces.Identity;
using ArtemisBankingPro.Application.Interfaces.Services;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Services;

public sealed class ConfirmationGuardTests {
    [Fact]
    public async Task Validate_binds_nonce_to_actor_command_and_fingerprint() {
        var tokens = new Mock<IConfirmationTokenService>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(user => user.UserId).Returns("actor-1");
        tokens.Setup(service => service.ValidateAndConsumeAsync(
                "nonce",
                "actor-1",
                OperationType,
                "payload-fingerprint",
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ConfirmationValidationResult.Valid());

        var guard = new ConfirmationGuard(tokens.Object, currentUser.Object);
        Result result = await guard.ValidateAsync(new TestCommand(), "nonce");

        result.IsSuccess.Should().BeTrue();
        tokens.VerifyAll();
    }

    [Fact]
    public async Task Validate_rejects_invalid_nonce_without_sending_a_command() {
        var tokens = new Mock<IConfirmationTokenService>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(user => user.UserId).Returns("actor-1");
        tokens.Setup(service => service.ValidateAndConsumeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ConfirmationValidationResult.Invalid(
                ConfirmationTokenInvalidReason.FingerprintMismatch
            ));

        var guard = new ConfirmationGuard(tokens.Object, currentUser.Object);
        Result result = await guard.ValidateAsync(new TestCommand(), "nonce");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Confirmation.Invalid");
    }

    private sealed record TestCommand : IIdempotentCommand {
        public string IdempotencyKey => "idempotency-key";

        public string RequestFingerprint => "payload-fingerprint";
    }

    private static string OperationType {
        get {
            string fullName = typeof(TestCommand).FullName!;
            string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fullName))
            );
            return $"mvc:{digest[..44]}";
        }
    }
}
