using ArtemisBankingPro.Application.Features.Users.Commands;

namespace ArtemisBankingPro.UnitTests.Application.Features.Users;

public sealed class UserMutationCommandTests {
    [Fact]
    public void CreateUserFingerprint_excludes_raw_password_but_changes_when_password_changes() {
        var command = new CreateUserCommand(
            "Ana", "Pérez", "001", "ana@example.com", "ana",
            "P@ssw0rd123!", "P@ssw0rd123!", "Cliente", 100m,
            "https://example.test"
        );

        command.RequestFingerprint.Should().NotContain(command.Password);
        (command with { CallbackUrl = "https://other.example.test" }).RequestFingerprint
            .Should().NotBe(command.RequestFingerprint);
    }

    [Fact]
    public void UpdateUserFingerprint_excludes_raw_password_but_changes_when_password_changes() {
        var command = new UpdateUserCommand(
            "user-1", "Ana", "Pérez", "001", "ana@example.com", "ana",
            "P@ssw0rd123!", "P@ssw0rd123!", 100m
        );

        (command with { UserId = "user-2" }).RequestFingerprint
            .Should().NotBe(command.RequestFingerprint);
        command.RequestFingerprint.Should().NotContain(command.Password!);
    }
}
