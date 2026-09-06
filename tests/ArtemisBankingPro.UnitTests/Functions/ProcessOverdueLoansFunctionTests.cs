using ArtemisBankingPro.Application.Features.Overdue.Commands;
using ArtemisBankingPro.Application.Features.Overdue.DTOs;
using ArtemisBankingPro.Application.Interfaces.Time;
using ArtemisBankingPro.Functions;
using ArtemisBankingPro.Domain.Common.ValueObjects;
using Mediator;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ArtemisBankingPro.UnitTests.Functions;

public sealed class ProcessOverdueLoansFunctionTests {
    [Fact]
    public async Task Run_CapturesBusinessDateOnceBeforeDispatchingCommand() {
        int clockReads = 0;
        var clock = new Mock<IBusinessClock>();
        clock
            .SetupGet(item => item.Today)
            .Returns(() => {
                clockReads++;
                if (clockReads > 1) {
                    throw new InvalidOperationException("La fecha de negocio se leyó más de una vez.");
                }

                return new DateOnly(2026, 8, 19);
            });

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(item => item.Send(
                It.IsAny<ProcessOverdueLoansCommand>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(new ValueTask<Result<OverdueProcessingResult>>(Result.Success(
                new OverdueProcessingResult(2, 1, 100m, 0, 0, false)
            )));

        var functionContext = new Mock<FunctionContext>();
        functionContext.SetupGet(item => item.InvocationId).Returns("invocation-123");
        using var telemetryConfiguration = new TelemetryConfiguration {
            DisableTelemetry = true,
        };
        var function = new ProcessOverdueLoansFunction(
            mediator.Object,
            clock.Object,
            NullLogger<ProcessOverdueLoansFunction>.Instance,
            new TelemetryClient(telemetryConfiguration)
        );

        await function.Run(new TimerInfo(), functionContext.Object, CancellationToken.None);

        clockReads.Should().Be(1);
        mediator.Verify(item => item.Send(
            It.Is<ProcessOverdueLoansCommand>(command =>
                command.BusinessDate == new DateOnly(2026, 8, 19)),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task Run_ThrowsWhenTheBatchRequiresAnotherExecution(
        int failedCount,
        bool hasMore
    ) {
        var clock = new Mock<IBusinessClock>();
        clock.SetupGet(item => item.Today).Returns(new DateOnly(2026, 8, 19));

        var mediator = new Mock<IMediator>();
        mediator
            .Setup(item => item.Send(
                It.IsAny<ProcessOverdueLoansCommand>(),
                It.IsAny<CancellationToken>()
            ))
            .Returns(new ValueTask<Result<OverdueProcessingResult>>(Result.Success(
                new OverdueProcessingResult(2, 1, 100m, failedCount, 0, hasMore)
            )));

        var functionContext = new Mock<FunctionContext>();
        functionContext.SetupGet(item => item.InvocationId).Returns("invocation-retry");
        using var telemetryConfiguration = new TelemetryConfiguration {
            DisableTelemetry = true,
        };
        var function = new ProcessOverdueLoansFunction(
            mediator.Object,
            clock.Object,
            NullLogger<ProcessOverdueLoansFunction>.Instance,
            new TelemetryClient(telemetryConfiguration)
        );

        Func<Task> act = () => function.Run(
            new TimerInfo(),
            functionContext.Object,
            CancellationToken.None
        );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
