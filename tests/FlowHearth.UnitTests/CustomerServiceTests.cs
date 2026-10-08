using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Domain.Customers;

namespace FlowHearth.UnitTests;

public sealed class CustomerServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 29, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CustomerCodeUsesYearAndMonotonicSequence()
    {
        Assert.Equal("CU-2026-0001", CustomerCode.Format(2026, 1));
        Assert.Equal("CU-2026-10000", CustomerCode.Format(2026, 10000));
    }

    [Theory]
    [InlineData(0, 20, "updatedAt")]
    [InlineData(1, 101, "updatedAt")]
    [InlineData(1, 20, "unsafe sql")]
    public async Task ListRejectsInvalidPagingAndSort(
        int page,
        int pageSize,
        string sortBy)
    {
        var service = CreateService(new FakeCustomerRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.ListAsync(
                page,
                pageSize,
                null,
                "active",
                null,
                null,
                sortBy,
                true,
                null,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData(null, "premium")]
    public async Task ListRejectsInvalidCustomerClassification(
        string? status,
        string? level)
    {
        var service = CreateService(new FakeCustomerRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.ListAsync(
                1,
                10,
                null,
                "active",
                status,
                level,
                "updatedAt",
                true,
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task ListNormalizesAndForwardsCustomerClassification()
    {
        var repository = new FakeCustomerRepository();
        var service = CreateService(repository);

        await service.ListAsync(
            1,
            10,
            null,
            "active",
            " prospect ",
            " a ",
            "updatedAt",
            true,
            null,
            CancellationToken.None,
            provinceCode: " 42 ",
            cityCode: " 4201 ",
            districtCode: " 420106 ");

        Assert.Equal(CustomerStatus.Prospect, repository.LastCriteria!.Status);
        Assert.Equal(CustomerLevel.A, repository.LastCriteria.Level);
        Assert.Equal("42", repository.LastCriteria.ProvinceCode);
        Assert.Equal("4201", repository.LastCriteria.CityCode);
        Assert.Equal("420106", repository.LastCriteria.DistrictCode);
    }

    [Theory]
    [InlineData(null, "4201", null)]
    [InlineData("42", null, "420106")]
    [InlineData("42", "4401", null)]
    [InlineData("42", "4201", "440106")]
    [InlineData("4A", null, null)]
    public async Task ListRejectsInvalidCustomerRegion(
        string? provinceCode,
        string? cityCode,
        string? districtCode)
    {
        var service = CreateService(new FakeCustomerRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.ListAsync(
            1,
            10,
            null,
            "active",
            null,
            null,
            "updatedAt",
            true,
            null,
            CancellationToken.None,
            provinceCode,
            cityCode,
            districtCode));
    }

    [Fact]
    public async Task UpdateRejectsStaleCustomerVersionBeforeRepositoryWrite()
    {
        var repository = new FakeCustomerRepository { Current = Customer(version: 3) };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(
                10,
                new UpdateCustomerCommand(
                    "示例客户",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    2),
                1,
                CancellationToken.None));
        Assert.False(repository.UpdateCalled);
    }

    [Fact]
    public async Task UpdatePreservesClassificationWhenLegacyClientOmitsIt()
    {
        var repository = new FakeCustomerRepository
        {
            Current = Customer(
                status: CustomerStatus.Prospect,
                level: CustomerLevel.A,
                provinceCode: "42",
                cityCode: "4201",
                districtCode: "420106"),
        };
        var service = CreateService(repository);

        await service.UpdateAsync(
            10,
            new UpdateCustomerCommand(
                "示例客户",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                1),
            1,
            CancellationToken.None);

        Assert.NotNull(repository.Updated);
        Assert.Equal(CustomerStatus.Prospect, repository.Updated.Status);
        Assert.Equal(CustomerLevel.A, repository.Updated.Level);
        Assert.Equal("42", repository.Updated.ProvinceCode);
        Assert.Equal("4201", repository.Updated.CityCode);
        Assert.Equal("420106", repository.Updated.DistrictCode);
    }

    [Fact]
    public async Task UpdateClearsCustomerRegionOnlyWhenExplicitlyRequested()
    {
        var repository = new FakeCustomerRepository
        {
            Current = Customer(
                provinceCode: "42",
                cityCode: "4201",
                districtCode: "420106"),
        };
        var service = CreateService(repository);

        await service.UpdateAsync(
            10,
            new UpdateCustomerCommand(
                "示例客户",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                1,
                ClearRegion: true),
            1,
            CancellationToken.None);

        Assert.NotNull(repository.Updated);
        Assert.Null(repository.Updated.ProvinceCode);
        Assert.Null(repository.Updated.CityCode);
        Assert.Null(repository.Updated.DistrictCode);
    }

    [Fact]
    public async Task ArchivedCustomerRejectsChildMutation()
    {
        var repository = new FakeCustomerRepository
        {
            Current = Customer(version: 2, archived: true),
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateContactAsync(
                10,
                new CreateContactCommand(
                    "张工",
                    null,
                    null,
                    "13800000000",
                    null,
                    null,
                    null,
                    true,
                    null),
                1,
                CancellationToken.None));
    }

    [Fact]
    public async Task FollowUpRequiresNextDateAfterOccurredDate()
    {
        var repository = new FakeCustomerRepository { Current = Customer() };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateFollowUpAsync(
                10,
                new CreateCustomerFollowUpCommand(
                    null,
                    CustomerFollowUpMethod.Visit,
                    NowUtc.AddHours(-1),
                    "现场沟通",
                    null,
                    NowUtc.AddHours(-2)),
                1,
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateNormalizesCustomerTextBeforeWriting()
    {
        var repository = new FakeCustomerRepository();
        var service = CreateService(repository);

        await service.CreateAsync(
            new CreateCustomerCommand(
                "  深圳客户  ",
                "  客户简称 ",
                null,
                null,
                " contact@example.com ",
                " https://example.com ",
                null,
                null,
                CustomerStatus.Active,
                CustomerLevel.Unrated,
                " 44 ",
                " 4403 ",
                " 440305 "),
            7,
            CancellationToken.None);

        Assert.NotNull(repository.Created);
        Assert.Equal("深圳客户", repository.Created.Name);
        Assert.Equal("客户简称", repository.Created.ShortName);
        Assert.Equal("contact@example.com", repository.Created.Email);
        Assert.Equal("https://example.com", repository.Created.Website);
        Assert.Equal(CustomerStatus.Active, repository.Created.Status);
        Assert.Equal(CustomerLevel.Unrated, repository.Created.Level);
        Assert.Equal("44", repository.Created.ProvinceCode);
        Assert.Equal("4403", repository.Created.CityCode);
        Assert.Equal("440305", repository.Created.DistrictCode);
        Assert.Equal(7UL, repository.Created.ActorUserId);
    }

    [Fact]
    public async Task CreateRejectsUndefinedCustomerClassification()
    {
        var repository = new FakeCustomerRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateAsync(
                new CreateCustomerCommand(
                    "客户",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    (CustomerStatus)999,
                    CustomerLevel.A),
                1,
                CancellationToken.None));

        Assert.Null(repository.Created);
    }

    private static CustomerService CreateService(FakeCustomerRepository repository)
    {
        return new CustomerService(repository, new FixedTimeProvider(NowUtc));
    }

    private static CustomerDetails Customer(
        ulong version = 1,
        bool archived = false,
        CustomerStatus status = CustomerStatus.Active,
        CustomerLevel level = CustomerLevel.Unrated,
        string? provinceCode = null,
        string? cityCode = null,
        string? districtCode = null)
    {
        return new CustomerDetails(
            10,
            "CU-2026-0001",
            "客户",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            archived,
            archived ? NowUtc : null,
            version,
            NowUtc,
            NowUtc,
            [],
            [],
            status,
            level,
            provinceCode,
            cityCode,
            districtCode);
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }

    private sealed class FakeCustomerRepository : ICustomerRepository
    {
        public CustomerDetails? Current { get; set; }

        public CreateCustomerData? Created { get; private set; }

        public CustomerListCriteria? LastCriteria { get; private set; }

        public bool UpdateCalled { get; private set; }

        public UpdateCustomerData? Updated { get; private set; }

        public Task<PagedResult<CustomerSummary>> ListAsync(
            CustomerListCriteria criteria,
            CancellationToken cancellationToken)
        {
            LastCriteria = criteria;
            return Task.FromResult(new PagedResult<CustomerSummary>([], criteria.Page, criteria.PageSize, 0));
        }

        public Task<CustomerDetails?> GetAsync(
            ulong customerId,
            CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<CustomerDetails> CreateAsync(
            CreateCustomerData data,
            CancellationToken cancellationToken)
        {
            Created = data;
            return Task.FromResult(Customer());
        }

        public Task<CustomerDetails?> UpdateAsync(
            ulong customerId,
            UpdateCustomerData data,
            CancellationToken cancellationToken)
        {
            UpdateCalled = true;
            Updated = data;
            return Task.FromResult(Current);
        }

        public Task<CustomerDetails?> SetArchivedAsync(
            ulong customerId,
            SetCustomerArchiveData data,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ContactDetails> CreateContactAsync(
            ulong customerId,
            CreateContactData data,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ContactDetails?> UpdateContactAsync(
            ulong customerId,
            ulong contactId,
            UpdateContactData data,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteContactAsync(
            ulong customerId,
            ulong contactId,
            ulong version,
            ulong actorUserId,
            DateTime nowUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CustomerFollowUpDetails> CreateFollowUpAsync(
            ulong customerId,
            CreateCustomerFollowUpData data,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
