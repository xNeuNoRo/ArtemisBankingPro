using System.Linq.Expressions;
using ArtemisBankingPro.Application.Interfaces.Persistence.Repositories;
using ArtemisBankingPro.Application.Services;
using ArtemisBankingPro.Domain.Common.Entities;
using Moq;

namespace ArtemisBankingPro.UnitTests.Application.Services;

public sealed class GenericServiceTests {
    [Fact]
    public async Task Delegates_maintenance_operations_without_saving_or_query_leakage() {
        var repository = new Mock<IGenericRepository<TestEntity>>();
        var entity = new TestEntity(7);
        Expression<Func<TestEntity, bool>> predicate = item => item.Id == 7;
        repository.Setup(item => item.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        repository.Setup(item => item.ExistsAsync(predicate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository.Setup(item => item.CountAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repository.Setup(item => item.AddAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var service = new GenericService<TestEntity>(repository.Object);

        ReferenceEquals(await service.GetByIdAsync(7), entity).Should().BeTrue();
        (await service.ExistsAsync(predicate)).Should().BeTrue();
        (await service.CountAsync()).Should().Be(1);
        ReferenceEquals(await service.AddAsync(entity), entity).Should().BeTrue();
        service.Update(entity);

        repository.Verify(item => item.GetByIdAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.ExistsAsync(predicate, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.CountAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.AddAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(item => item.Update(entity), Times.Once);
        repository.VerifyNoOtherCalls();
    }

    public sealed class TestEntity : Entity<int> {
        public TestEntity(int id) {
            Id = id;
        }
    }
}
