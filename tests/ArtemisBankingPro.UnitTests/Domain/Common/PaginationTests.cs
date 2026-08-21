using ArtemisBankingPro.Domain.Common.Pagination;

namespace ArtemisBankingPro.UnitTests.Domain.Common;

public sealed class PageRequestTests {
    [Fact]
    public void Defaults_WhenNullOrInvalid_ApplyContractDefaults() {
        new PageRequest().Page.Should().Be(PageRequest.DefaultPage);
        new PageRequest().PageSize.Should().Be(PageRequest.DefaultPageSize);

        new PageRequest(page: 0).Page.Should().Be(PageRequest.DefaultPage);
        new PageRequest(page: -3).Page.Should().Be(PageRequest.DefaultPage);
        new PageRequest(pageSize: 0).PageSize.Should().Be(PageRequest.DefaultPageSize);
        new PageRequest(pageSize: -1).PageSize.Should().Be(PageRequest.DefaultPageSize);
    }

    [Fact]
    public void PageSize_CappedAtMaximum_WhenRequestedLarger() {
        new PageRequest(pageSize: 50).PageSize.Should().Be(PageRequest.MaxPageSize);
        new PageRequest(pageSize: 21).PageSize.Should().Be(PageRequest.MaxPageSize);
    }

    [Fact]
    public void ValidValues_ArePreserved() {
        var request = new PageRequest(page: 3, pageSize: 15);

        request.Page.Should().Be(3);
        request.PageSize.Should().Be(15);
    }

    [Fact]
    public void Skip_IsZeroBasedFromPage() {
        new PageRequest(page: 1, pageSize: 20).Skip.Should().Be(0);
        new PageRequest(page: 2, pageSize: 20).Skip.Should().Be(20);
        new PageRequest(page: 5, pageSize: 10).Skip.Should().Be(40);
    }

    [Fact]
    public void Skip_DoesNotWrapForTheLargestSupportedPageValue() {
        new PageRequest(page: int.MaxValue, pageSize: PageRequest.MaxPageSize)
            .Skip
            .Should()
            .Be(int.MaxValue);
    }
}

public sealed class PageResultTests {
    [Fact]
    public void TotalPages_ComputesCeiling() {
        new PageResult<string>(["a", "b", "c"], 3, 1, 2).TotalPages.Should().Be(2);
        new PageResult<string>(["a"], 1, 1, 20).TotalPages.Should().Be(1);
    }

    [Fact]
    public void TotalPages_ZeroWhenNoItems() {
        new PageResult<string>([], 0, 1, 20).TotalPages.Should().Be(0);
    }

    [Fact]
    public void HasNextPage_IsTrueOnlyWhenMorePagesExist() {
        new PageResult<string>(["a", "b"], 5, 1, 2).HasNextPage.Should().BeTrue();
        new PageResult<string>(["a", "b"], 2, 1, 2).HasNextPage.Should().BeFalse();
        new PageResult<string>([], 0, 1, 2).HasNextPage.Should().BeFalse();
    }
}
