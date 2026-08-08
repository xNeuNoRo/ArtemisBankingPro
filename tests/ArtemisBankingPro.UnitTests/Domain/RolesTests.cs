using ArtemisBankingPro.Domain.Enums;

namespace ArtemisBankingPro.UnitTests.Domain;

public sealed class RolesTests {
    [Fact]
    public void EnumValues_MatchContractNames() {
        nameof(Roles.Administrador).Should().Be("Administrador");
        nameof(Roles.Cajero).Should().Be("Cajero");
        nameof(Roles.Cliente).Should().Be("Cliente");
        nameof(Roles.Comercio).Should().Be("Comercio");
    }

    [Fact]
    public void RoleSets_All_ContainsExactlyTheFourRoles() {
        RoleSets.All.Should().BeEquivalentTo(
            nameof(Roles.Administrador),
            nameof(Roles.Cajero),
            nameof(Roles.Cliente),
            nameof(Roles.Comercio)
        );
    }

    [Fact]
    public void RoleSets_Mvc_ContainsOnlyRolesWithMvcAccess() {
        RoleSets.Mvc.Should().BeEquivalentTo(
            nameof(Roles.Administrador),
            nameof(Roles.Cajero),
            nameof(Roles.Cliente)
        );
    }

    [Fact]
    public void RoleSets_Api_ContainsOnlyApiRoles() {
        RoleSets.Api.Should().BeEquivalentTo(
            nameof(Roles.Administrador),
            nameof(Roles.Comercio)
        );
    }

    [Fact]
    public void RoleSets_Comercio_HasApiAccessButNoMvcAccess() {
        RoleSets.Mvc.Should().NotContain(nameof(Roles.Comercio));
        RoleSets.Api.Should().Contain(nameof(Roles.Comercio));
    }

    [Fact]
    public void RoleSets_Cliente_HasMvcAccessButNoApiAccess() {
        RoleSets.Mvc.Should().Contain(nameof(Roles.Cliente));
        RoleSets.Api.Should().NotContain(nameof(Roles.Cliente));
    }
}
