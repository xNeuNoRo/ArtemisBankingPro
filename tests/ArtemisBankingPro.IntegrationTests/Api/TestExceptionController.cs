using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ArtemisBankingPro.IntegrationTests.Api;

[ApiController]
[AllowAnonymous]
[Route("__tests__")]
public sealed class TestExceptionController : ControllerBase {
    private readonly string _internalFailureMessage = "internal database connection secret";

    [HttpGet("unhandled-exception")]
    public IActionResult Throw() => throw new InvalidOperationException(_internalFailureMessage);

    [HttpGet("payload-too-large")]
    public IActionResult PayloadTooLarge() => StatusCode(StatusCodes.Status413PayloadTooLarge);
}
