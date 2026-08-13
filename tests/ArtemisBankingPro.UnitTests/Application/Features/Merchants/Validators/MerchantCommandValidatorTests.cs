using ArtemisBankingPro.Application.Features.Merchants.Commands;
using ArtemisBankingPro.Application.Features.Merchants.Validators;

namespace ArtemisBankingPro.UnitTests.Application.Features.Merchants.Validators;

public sealed class CreateMerchantCommandValidatorTests {
    private readonly CreateMerchantCommandValidator _validator = new();

    private static CreateMerchantCommand ValidCommand() =>
        new(
            "Tienda Demo",
            "Comercio de prueba",
            "contacto@tiendademo.com",
            "8095551234",
            "101999999"
        );

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyName_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Name = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Name"
            && e.ErrorMessage == "El nombre del comercio es obligatorio."
        );
    }

    [Fact]
    public async Task Validate_EmptyEmail_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Email = "  " });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Email"
            && e.ErrorMessage == "El correo electrónico es obligatorio."
        );
    }

    [Fact]
    public async Task Validate_InvalidEmailFormat_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Email = "sin-arroba" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Email"
            && e.ErrorMessage == "El correo electrónico debe tener un formato válido."
        );
    }

    [Fact]
    public async Task Validate_EmptyPhoneNumber_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { PhoneNumber = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "PhoneNumber"
            && e.ErrorMessage == "El teléfono es obligatorio."
        );
    }

    [Fact]
    public async Task Validate_EmptyRnc_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Rnc = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Rnc"
            && e.ErrorMessage == "El RNC es obligatorio."
        );
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("101999999999999")]
    [InlineData("10199999")]
    [InlineData("1019999990")]
    public async Task Validate_RncWithoutExactlyNineDigits_Fails(string rnc) {
        var result = await _validator.ValidateAsync(ValidCommand() with { Rnc = rnc });

        result.Errors.Should().Contain(e => e.PropertyName == "Rnc");
    }

    [Fact]
    public async Task Validate_RncWithWrongLength_UsesLengthMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Rnc = "10199999" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Rnc"
            && e.ErrorMessage == "El RNC debe tener 9 caracteres."
        );
    }
}

public sealed class UpdateMerchantCommandValidatorTests {
    private readonly UpdateMerchantCommandValidator _validator = new();

    private static UpdateMerchantCommand ValidCommand() =>
        new(
            5,
            "Tienda Demo",
            "Comercio de prueba",
            "contacto@tiendademo.com",
            "8095551234",
            "101999999"
        );

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ZeroMerchantId_Fails() {
        var result = await _validator.ValidateAsync(ValidCommand() with { MerchantId = 0 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "MerchantId"
            && e.ErrorMessage == "El identificador del comercio es obligatorio."
        );
    }

    [Fact]
    public async Task Validate_InvalidEmail_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Email = "inválido" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Email"
            && e.ErrorMessage == "El correo electrónico debe tener un formato válido."
        );
    }

    [Fact]
    public async Task Validate_EmptyName_UsesSpecMessage() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Name = null! });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Name"
            && e.ErrorMessage == "El nombre del comercio es obligatorio."
        );
    }

    [Fact]
    public async Task Validate_RncWithoutNineDigits_Fails() {
        var result = await _validator.ValidateAsync(ValidCommand() with { Rnc = "1019999990" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Rnc"
            && e.ErrorMessage == "El RNC debe tener 9 caracteres."
        );
    }
}

public sealed class ChangeMerchantStatusCommandValidatorTests {
    private readonly ChangeMerchantStatusCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ValidCommand_Passes() {
        var result = await _validator.ValidateAsync(new ChangeMerchantStatusCommand(5, IsActive: true));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ZeroMerchantId_Fails() {
        var result = await _validator.ValidateAsync(new ChangeMerchantStatusCommand(0, IsActive: false));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "MerchantId");
    }
}
