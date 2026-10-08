using FlowHearth.Application.Attachments;
using FlowHearth.Application.Audit;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Dashboard;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Opportunities;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Search;
using FlowHearth.Application.Security;
using FlowHearth.Application.Service;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Attachments;
using FlowHearth.Domain.Customers;
using FlowHearth.Domain.Equipment;
using FlowHearth.Domain.Opportunities;
using FlowHearth.Domain.Projects;
using FlowHearth.Domain.Service;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowHearth.IntegrationTests;

public sealed class SecurityApiApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthenticationService>();
            services.RemoveAll<IAuthenticationRepository>();
            services.RemoveAll<ISecurityAdministrationService>();
            services.RemoveAll<ICustomerService>();
            services.RemoveAll<IOpportunityService>();
            services.RemoveAll<IProjectService>();
            services.RemoveAll<IEquipmentService>();
            services.RemoveAll<IServiceTicketService>();
            services.RemoveAll<IAttachmentService>();
            services.RemoveAll<IAuditLogService>();
            services.RemoveAll<IDashboardService>();
            services.RemoveAll<IFinanceOverviewService>();
            services.RemoveAll<ISupplierService>();
            services.RemoveAll<IReceivableService>();
            services.RemoveAll<IPurchaseOrderService>();
            services.RemoveAll<IPayableService>();
            services.RemoveAll<IShipmentService>();
            services.RemoveAll<IGlobalSearchService>();
            services.RemoveAll<ISettingsService>();
            services.RemoveAll<IRuntimeSettingsProvider>();
            services.AddSingleton<IAuthenticationService, FakeAuthenticationService>();
            services.AddSingleton<IAuthenticationRepository, FakeAuthenticationRepository>();
            services.AddSingleton<ISecurityAdministrationService, FakeAdministrationService>();
            services.AddSingleton<ICustomerService, FakeCustomerService>();
            services.AddSingleton<IOpportunityService, FakeOpportunityService>();
            services.AddSingleton<IProjectService, FakeProjectService>();
            services.AddSingleton<IEquipmentService, FakeEquipmentService>();
            services.AddSingleton<IServiceTicketService, FakeServiceTicketService>();
            services.AddSingleton<IAttachmentService, FakeAttachmentService>();
            services.AddSingleton<IAuditLogService, FakeAuditLogService>();
            services.AddSingleton<IDashboardService, FakeDashboardService>();
            services.AddSingleton<IFinanceOverviewService, FakeFinanceOverviewService>();
            services.AddSingleton<ISupplierService, FakeSupplierService>();
            services.AddSingleton<IReceivableService, FakeReceivableService>();
            services.AddSingleton<IPurchaseOrderService, FakePurchaseOrderService>();
            services.AddSingleton<IPayableService, FakePayableService>();
            services.AddSingleton<IShipmentService, FakeShipmentService>();
            services.AddSingleton<IGlobalSearchService, FakeGlobalSearchService>();
            services.AddSingleton<FakeSettingsService>();
            services.AddSingleton<ISettingsService>(serviceProvider =>
                serviceProvider.GetRequiredService<FakeSettingsService>());
            services.AddSingleton<IRuntimeSettingsProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<FakeSettingsService>());
        });
    }

    private static AuthenticatedUser CreateUser(ulong userId)
    {
        var administrator = userId == 1;
        var attachmentOnly = userId == 3;
        var searchOnly = userId == 4;
        var dashboardOnly = userId == 5;
        var settingsReader = userId == 6;
        var financeReader = userId == 7;
        var username = userId switch
        {
            1 => "admin",
            2 => "viewer",
            3 => "files",
            4 => "search",
            5 => "dashboard",
            6 => "settings",
            7 => "finance-reader",
            _ => "unknown",
        };
        return new AuthenticatedUser(
            userId,
            username,
            administrator ? "Administrator" : username,
            null,
            1,
            [administrator ? "administrator" : "viewer"],
            administrator
                ? SecurityPermissions.All
                : attachmentOnly
                    ? [SecurityPermissions.AttachmentsView]
                : searchOnly
                    ? [SecurityPermissions.SearchUse, SecurityPermissions.CustomersView]
                : dashboardOnly
                    ? [SecurityPermissions.DashboardView, SecurityPermissions.CustomersView]
                : settingsReader
                    ? [SecurityPermissions.SettingsView]
                : financeReader
                    ?
                    [
                        SecurityPermissions.CustomersView,
                        SecurityPermissions.ProjectsView,
                        SecurityPermissions.EquipmentView,
                        SecurityPermissions.FinanceDashboardView,
                        SecurityPermissions.SuppliersView,
                        SecurityPermissions.ReceivablesView,
                        SecurityPermissions.ReceiptsView,
                        SecurityPermissions.PurchasesView,
                        SecurityPermissions.PayablesView,
                        SecurityPermissions.PaymentsView,
                        SecurityPermissions.ShipmentsView,
                    ]
                :
                [
                    SecurityPermissions.DashboardView,
                    SecurityPermissions.CustomersView,
                    SecurityPermissions.OpportunitiesView,
                    SecurityPermissions.ProjectsView,
                    SecurityPermissions.EquipmentView,
                    SecurityPermissions.ServiceView,
                    SecurityPermissions.AttachmentsView,
                    SecurityPermissions.SearchUse,
                ]);
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task<LoginResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var userId = username switch
            {
                "admin" when password == "Strong!Password1" => 1UL,
                "viewer" when password == "Strong!Password1" => 2UL,
                "files" when password == "Strong!Password1" => 3UL,
                "search" when password == "Strong!Password1" => 4UL,
                "dashboard" when password == "Strong!Password1" => 5UL,
                "settings" when password == "Strong!Password1" => 6UL,
                "finance-reader" when password == "Strong!Password1" => 7UL,
                _ => 0UL,
            };
            return Task.FromResult(
                userId == 0
                    ? new LoginResult(LoginStatus.InvalidCredentials)
                    : new LoginResult(LoginStatus.Succeeded, CreateUser(userId)));
        }

        public Task<AuthenticatedUser?> GetCurrentUserAsync(
            ulong userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<AuthenticatedUser?>(CreateUser(userId));
        }

        public Task ChangePasswordAsync(
            ulong userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthenticationRepository : IAuthenticationRepository
    {
        public Task<SecuritySession?> GetSecuritySessionAsync(
            ulong userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<SecuritySession?>(new SecuritySession(userId, true, 1));
        }

        public Task<UserAuthenticationRecord?> FindByNormalizedUsernameAsync(
            string normalizedUsername,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserAuthenticationRecord?> FindByIdAsync(
            ulong userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateTime?> RecordFailedLoginAsync(
            ulong userId,
            DateTime nowUtc,
            int maximumAttempts,
            TimeSpan lockoutDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthenticatedUser?> RecordSuccessfulLoginAsync(
            ulong userId,
            string? replacementPasswordHash,
            DateTime nowUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthenticatedUser?> GetAuthenticatedUserAsync(
            ulong userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdatePasswordAsync(
            ulong userId,
            string passwordHash,
            DateTime nowUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAdministrationService : ISecurityAdministrationService
    {
        public Task<PagedResult<UserSummary>> ListUsersAsync(
            int page,
            int pageSize,
            string? search,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new PagedResult<UserSummary>([], page, pageSize, 0));
        }

        public Task<UserDetails> GetUserAsync(
            ulong userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserDetails> CreateUserAsync(
            CreateUserCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) => throw new ForbiddenException(
                "不能授予当前用户不具备的角色权限。");

        public Task<UserDetails> UpdateUserAsync(
            ulong userId,
            UpdateUserCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResetPasswordAsync(
            ulong userId,
            string newPassword,
            ulong actorUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<RoleDetails>> ListRolesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RoleDetails>>([]);
        }

        public Task<IReadOnlyList<PermissionDetails>> ListPermissionsAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PermissionDetails>>([]);
        }

        public Task<RoleDetails> CreateRoleAsync(
            CreateRoleCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RoleDetails> UpdateRoleAsync(
            ulong roleId,
            UpdateRoleCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeCustomerService : ICustomerService
    {
        private static readonly DateTime Timestamp = new(2026, 8, 29, 6, 0, 0, DateTimeKind.Utc);

        public Task<PagedResult<CustomerSummary>> ListAsync(
            int page,
            int pageSize,
            string? search,
            string? archive,
            string? status,
            string? level,
            string? sortBy,
            bool sortDescending,
            DateTime? nextFollowUpBeforeUtc,
            CancellationToken cancellationToken,
            string? provinceCode = null,
            string? cityCode = null,
            string? districtCode = null)
        {
            return Task.FromResult(
                new PagedResult<CustomerSummary>(
                    [new CustomerSummary(42, "CU-2026-0042", "测试客户", null, null, null, null, false, null, 1, 1, Timestamp, "张工", "13800000000")],
                    page,
                    pageSize,
                    1));
        }

        public Task<CustomerDetails> GetAsync(
            ulong customerId,
            CancellationToken cancellationToken) => Task.FromResult(Customer(customerId));

        public Task<CustomerDetails> CreateAsync(
            CreateCustomerCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) => Task.FromResult(Customer(42, command.Name));

        public Task<CustomerDetails> UpdateAsync(
            ulong customerId,
            UpdateCustomerCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken)
        {
            if (command.Version != 1)
            {
                throw new ConflictException("客户已被其他操作修改，请刷新后重试。");
            }

            return Task.FromResult(Customer(customerId, command.Name) with { Version = 2 });
        }

        public Task<CustomerDetails> SetArchivedAsync(
            ulong customerId,
            bool archived,
            CustomerVersionCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Customer(customerId) with { IsArchived = archived, Version = command.Version + 1 });

        public Task<ContactDetails> CreateContactAsync(
            ulong customerId,
            CreateContactCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ContactDetails(8, customerId, command.Name, command.Title, command.Department, command.Mobile, command.Phone, command.Email, command.WeChat, command.IsPrimary, command.Notes, 1, Timestamp, Timestamp));

        public Task<ContactDetails> UpdateContactAsync(
            ulong customerId,
            ulong contactId,
            UpdateContactCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteContactAsync(
            ulong customerId,
            ulong contactId,
            CustomerVersionCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CustomerFollowUpDetails> CreateFollowUpAsync(
            ulong customerId,
            CreateCustomerFollowUpCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerFollowUpDetails(9, customerId, command.ContactId, null, command.Method, command.OccurredAtUtc, command.Summary, command.Details, command.NextFollowUpAtUtc, "Administrator", Timestamp));

        private static CustomerDetails Customer(ulong id, string name = "测试客户") =>
            new(id, "CU-2026-0042", name, null, null, null, null, null, null, null, false, null, 1, Timestamp, Timestamp, [], []);
    }

    private sealed class FakeOpportunityService : IOpportunityService
    {
        private static readonly DateTime Timestamp = new(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);

        public Task<PagedResult<OpportunitySummary>> ListAsync(
            int page,
            int pageSize,
            string? search,
            string? archive,
            string? stage,
            ulong? customerId,
            string? sortBy,
            bool sortDescending,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new PagedResult<OpportunitySummary>(
                    [Summary(20)],
                    page,
                    pageSize,
                    1));

        public Task<OpportunityDetails> GetAsync(
            ulong opportunityId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Details(opportunityId));

        public Task<OpportunityDetails> CreateAsync(
            CreateOpportunityCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Details(20, command.Title));

        public Task<OpportunityDetails> UpdateAsync(
            ulong opportunityId,
            UpdateOpportunityCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken)
        {
            if (command.Version != 1)
            {
                throw new ConflictException("商机已被其他操作修改，请刷新后重试。");
            }

            return Task.FromResult(Details(opportunityId, command.Title) with { Version = 2 });
        }

        public Task<OpportunityDetails> TransitionAsync(
            ulong opportunityId,
            TransitionOpportunityCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Details(opportunityId) with
                {
                    Stage = command.Stage,
                    LostReason = command.LostReason,
                    Version = command.Version + 1,
                });

        public Task<OpportunityDetails> SetArchivedAsync(
            ulong opportunityId,
            bool archived,
            OpportunityVersionCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Details(opportunityId) with
                {
                    IsArchived = archived,
                    Version = command.Version + 1,
                });

        public Task<ProjectReference> ConvertToProjectAsync(
            ulong opportunityId,
            OpportunityVersionCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProjectReference(30, "TN-2026-0001", "测试项目", "Planning"));

        private static OpportunitySummary Summary(ulong id) =>
            new(
                id,
                "OP-2026-0001",
                42,
                "CU-2026-0042",
                "测试客户",
                "测试商机",
                OpportunityStage.Lead,
                100000m,
                30,
                new DateTime(2026, 10, 1),
                false,
                null,
                1,
                Timestamp);

        private static OpportunityDetails Details(ulong id, string title = "测试商机") =>
            new(
                id,
                "OP-2026-0001",
                42,
                "CU-2026-0042",
                "测试客户",
                title,
                OpportunityStage.Lead,
                100000m,
                30,
                new DateTime(2026, 10, 1),
                null,
                null,
                false,
                null,
                null,
                1,
                Timestamp,
                Timestamp);
    }

    private sealed class FakeProjectService : IProjectService
    {
        private static readonly DateTime Timestamp = new(2026, 8, 29, 9, 0, 0, DateTimeKind.Utc);
        public Task<PagedResult<ProjectSummary>> ListAsync(int page, int pageSize, string? search, string? archive, string? status, ulong? customerId, string? sortBy, bool sortDescending, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<ProjectSummary>([], page, pageSize, 0));
        public Task<ProjectDetails> GetAsync(ulong projectId, CancellationToken cancellationToken) => Task.FromResult(Project(projectId));
        public Task<IReadOnlyList<ProjectMemberCandidate>> ListMemberCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProjectMemberCandidate>>([]);
        public Task<ProjectDetails> CreateAsync(CreateProjectCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Project(30, command.Name));
        public Task<ProjectDetails> UpdateAsync(ulong projectId, UpdateProjectCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Project(projectId, command.Name));
        public Task<ProjectDetails> TransitionAsync(ulong projectId, TransitionProjectCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Project(projectId) with { Status = command.Status, Version = command.Version + 1 });
        public Task<ProjectDetails> SetArchivedAsync(ulong projectId, bool archived, ProjectVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Project(projectId) with { IsArchived = archived });
        public Task<ProjectMemberDetails> CreateMemberAsync(ulong projectId, CreateProjectMemberCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(new ProjectMemberDetails(1, projectId, command.UserId, "user", "成员", command.RoleName, command.Responsibility, 1, Timestamp, Timestamp));
        public Task<ProjectMemberDetails> UpdateMemberAsync(ulong projectId, ulong memberId, UpdateProjectMemberCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteMemberAsync(ulong projectId, ulong memberId, ProjectVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ProjectMilestoneDetails> CreateMilestoneAsync(ulong projectId, CreateProjectMilestoneCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(new ProjectMilestoneDetails(1, projectId, command.Name, command.DueDate, null, command.Notes, command.SortOrder, 1, Timestamp, Timestamp));
        public Task<ProjectMilestoneDetails> UpdateMilestoneAsync(ulong projectId, ulong milestoneId, UpdateProjectMilestoneCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteMilestoneAsync(ulong projectId, ulong milestoneId, ProjectVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        private static ProjectDetails Project(ulong id, string name = "测试项目") => new(id, "TN-2026-0001", 42, "CU-2026-0042", "测试客户", null, null, name, ProjectStatus.Planning, 100000m, 0, null, null, null, null, null, false, null, 1, Timestamp, Timestamp, [], []);
    }

    private sealed class FakeEquipmentService : IEquipmentService
    {
        private static readonly DateTime Timestamp = new(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        public Task<PagedResult<EquipmentSummary>> ListAsync(int page, int pageSize, string? search, string? archive, string? category, ulong? customerId, ulong? projectId, string? sortBy, bool sortDescending, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<EquipmentSummary>([new EquipmentSummary(50, "EQ-2026-0001", 42, "CU-2026-0042", "测试客户", 30, "TN-2026-0001", "测试项目", "装配设备", EquipmentCategory.PLC, "Siemens", "S7-1500", "SN-1", false, 0, 0, 0, 1, Timestamp)], page, pageSize, 1));

        public Task<EquipmentDetails> GetAsync(ulong equipmentId, CancellationToken cancellationToken) => Task.FromResult(Equipment(equipmentId));
        public Task<EquipmentDetails> CreateAsync(CreateEquipmentCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Equipment(50, command.Name, command.Category));
        public Task<EquipmentDetails> UpdateAsync(ulong equipmentId, UpdateEquipmentCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Equipment(equipmentId, command.Name, command.Category) with { Version = command.Version + 1 });
        public Task<EquipmentDetails> SetArchivedAsync(ulong equipmentId, bool archived, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Equipment(equipmentId) with { IsArchived = archived, Version = command.Version + 1 });
        public Task<EquipmentComponentDetails> CreateComponentAsync(ulong equipmentId, CreateEquipmentComponentCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(new EquipmentComponentDetails(1, equipmentId, command.Category, command.Name, command.Manufacturer, command.Model, command.SerialNumber, command.FirmwareVersion, command.Quantity, command.InstallLocation, command.Notes, command.SortOrder, 1, Timestamp, Timestamp));
        public Task<EquipmentComponentDetails> UpdateComponentAsync(ulong equipmentId, ulong componentId, UpdateEquipmentComponentCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteComponentAsync(ulong equipmentId, ulong componentId, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<EquipmentParameterDetails> CreateParameterAsync(ulong equipmentId, CreateEquipmentParameterCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(new EquipmentParameterDetails(2, equipmentId, command.ParameterGroup, command.Name, command.Value, command.Unit, command.Notes, command.SortOrder, 1, Timestamp, Timestamp));
        public Task<EquipmentParameterDetails> UpdateParameterAsync(ulong equipmentId, ulong parameterId, UpdateEquipmentParameterCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteParameterAsync(ulong equipmentId, ulong parameterId, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<EquipmentVersionDetails> CreateVersionAsync(ulong equipmentId, CreateEquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(new EquipmentVersionDetails(3, equipmentId, command.VersionType, command.VersionLabel, command.GitCommit, command.Changelog, command.ReleasedDate, command.Notes, 1, Timestamp, Timestamp));
        public Task<EquipmentVersionDetails> UpdateVersionAsync(ulong equipmentId, ulong versionId, UpdateEquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteVersionAsync(ulong equipmentId, ulong versionId, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;

        private static EquipmentDetails Equipment(ulong id, string name = "装配设备", EquipmentCategory category = EquipmentCategory.PLC) =>
            new(id, "EQ-2026-0001", 42, "CU-2026-0042", "测试客户", 30, "TN-2026-0001", "测试项目", name, category, "Siemens", "S7-1500", "SN-1", "Line 1", null, null, false, null, 1, Timestamp, Timestamp, [], [], []);
    }

    private sealed class FakeServiceTicketService : IServiceTicketService
    {
        private static readonly DateTime Timestamp = new(2026, 8, 29, 11, 0, 0, DateTimeKind.Utc);
        public Task<PagedResult<ServiceTicketSummary>> ListAsync(int page, int pageSize, string? search, string? archive, string? priority, string? status, ulong? customerId, ulong? projectId, ulong? equipmentId, ulong? assignedUserId, string? sortBy, bool sortDescending, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<ServiceTicketSummary>([new ServiceTicketSummary(60, "SR-2026-0001", 42, "CU-2026-0042", "测试客户", 30, "TN-2026-0001", "测试项目", 50, "EQ-2026-0001", "装配设备", "设备停机", ServiceTicketPriority.P1, ServiceTicketStatus.New, null, null, Timestamp, 0, false, 0, 1, Timestamp)], page, pageSize, 1));
        public Task<ServiceTicketDetails> GetAsync(ulong ticketId, CancellationToken cancellationToken) => Task.FromResult(Ticket(ticketId));
        public Task<IReadOnlyList<ServiceAssignee>> ListAssigneesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ServiceAssignee>>([new ServiceAssignee(1, "admin", "Administrator", null)]);
        public Task<ServiceTicketDetails> CreateAsync(CreateServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Ticket(60, command.Title));
        public Task<ServiceTicketDetails> UpdateAsync(ulong ticketId, UpdateServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Ticket(ticketId, command.Title) with { Version = command.Version + 1 });
        public Task<ServiceTicketDetails> AssignAsync(ulong ticketId, AssignServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Ticket(ticketId) with { AssignedUserId = command.UserId, AssignedDisplayName = "Administrator", Version = command.Version + 1 });
        public Task<ServiceTicketDetails> TransitionAsync(ulong ticketId, TransitionServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Ticket(ticketId) with { Status = command.Status, RootCause = command.RootCause, Solution = command.Solution, DowntimeMinutes = command.DowntimeMinutes ?? 0, Version = command.Version + 1 });
        public Task<ServiceTicketDetails> SetArchivedAsync(ulong ticketId, bool archived, ServiceTicketVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(Ticket(ticketId) with { IsArchived = archived, Version = command.Version + 1 });
        public Task<ServiceRecordDetails> CreateRecordAsync(ulong ticketId, CreateServiceRecordCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.FromResult(new ServiceRecordDetails(1, ticketId, command.RecordType, command.Content, null, null, command.DurationMinutes, command.OccurredAtUtc ?? Timestamp, actorUserId, "Administrator", 1, Timestamp, Timestamp));
        public Task<ServiceRecordDetails> UpdateRecordAsync(ulong ticketId, ulong recordId, UpdateServiceRecordCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteRecordAsync(ulong ticketId, ulong recordId, ServiceTicketVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        private static ServiceTicketDetails Ticket(ulong id, string title = "设备停机") => new(id, "SR-2026-0001", 42, "CU-2026-0042", "测试客户", 30, "TN-2026-0001", "测试项目", 50, "EQ-2026-0001", "装配设备", title, "无法启动", ServiceTicketPriority.P1, ServiceTicketStatus.New, null, null, Timestamp, null, null, null, null, null, 0, false, null, 1, Timestamp, Timestamp, []);
    }

    private sealed class FakeAttachmentService : IAttachmentService
    {
        private static readonly DateTime Timestamp = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

        public Task<IReadOnlyList<AttachmentSummary>> ListAsync(AttachmentEntityType entityType, ulong entityId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AttachmentSummary>>([Attachment(70, entityType, entityId)]);

        public Task<AttachmentSummary> GetAsync(ulong attachmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Attachment(attachmentId, AttachmentEntityType.Customer, 42));

        public Task<AttachmentSummary> UploadAsync(UploadAttachmentCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Attachment(70, command.EntityType, command.EntityId, command.OriginalFileName));

        public Task<AttachmentDownload> DownloadAsync(ulong attachmentId, CancellationToken cancellationToken)
        {
            var bytes = "phase8 attachment"u8.ToArray();
            return Task.FromResult(new AttachmentDownload(new MemoryStream(bytes), "phase8.txt", "text/plain", (ulong)bytes.Length));
        }

        public Task DeleteAsync(ulong attachmentId, ulong version, ulong actorUserId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        private static AttachmentSummary Attachment(ulong id, AttachmentEntityType entityType, ulong entityId, string fileName = "phase8.txt") =>
            new(id, entityType, entityId, "CU-2026-0042", fileName, "text/plain", 17, new string('a', 64), null, 1, Timestamp, 1, "Administrator");
    }

    private sealed class FakeAuditLogService : IAuditLogService
    {
        private static readonly DateTime Timestamp = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

        public Task<PagedResult<AuditLogSummary>> ListAsync(int page, int pageSize, string? search, string? entityType, string? action, ulong? actorUserId, DateTime? occurredFromUtc, DateTime? occurredToUtc, string? sortBy, bool sortDescending, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<AuditLogSummary>([new AuditLogSummary(80, Timestamp, 1, "admin", "Administrator", "AttachmentUploaded", "Attachment", 70, "CU-2026-0042", "上传附件 phase8.txt", null, false, true)], page, pageSize, 1));

        public Task<AuditLogDetails> GetAsync(ulong auditLogId, CancellationToken cancellationToken) =>
            Task.FromResult(new AuditLogDetails(auditLogId, Timestamp, 1, "admin", "Administrator", "AttachmentUploaded", "Attachment", 70, "CU-2026-0042", "上传附件 phase8.txt", null, "{\"originalFileName\":\"phase8.txt\"}", null));
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private static readonly DateTime Timestamp =
            new(2026, 8, 31, 2, 30, 0, DateTimeKind.Utc);

        private static readonly LookupItemDetails Industry = new(
            1,
            LookupDictionaryCodes.CustomerIndustry,
            "工业自动化",
            "工业自动化",
            null,
            10,
            true,
            1,
            Timestamp,
            Timestamp);

        private static readonly SystemSettingDetails PageSize = new(
            SystemSettingKeys.DefaultPageSize,
            "默认每页数量",
            "10",
            SystemSettingValueType.WholeNumber,
            null,
            true,
            1,
            Timestamp);

        public Task<RuntimeSettings> GetRuntimeAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                RuntimeSettings.Defaults with
                {
                    Lookups = new Dictionary<string, IReadOnlyList<LookupItemDetails>>
                    {
                        [LookupDictionaryCodes.CustomerIndustry] = [Industry],
                    },
                });

        public Task<SettingsAdministrationSnapshot> GetAdministrationAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new SettingsAdministrationSnapshot(
                    [
                        new LookupDictionaryDetails(
                            LookupDictionaryCodes.CustomerIndustry,
                            "客户所属行业",
                            "客户行业建议值",
                            [Industry]),
                    ],
                    [PageSize]));

        public Task<LookupItemDetails> CreateLookupItemAsync(
            CreateLookupItemCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Industry with
                {
                    Id = 2,
                    Value = command.Value,
                    Label = command.Label,
                    Description = command.Description,
                    SortOrder = command.SortOrder,
                });

        public Task<LookupItemDetails> UpdateLookupItemAsync(
            ulong itemId,
            UpdateLookupItemCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Industry with
                {
                    Id = itemId,
                    Label = command.Label,
                    Description = command.Description,
                    SortOrder = command.SortOrder,
                    IsActive = command.IsActive,
                    Version = command.Version + 1,
                });

        public Task<SystemSettingDetails> UpdateSystemSettingAsync(
            string key,
            UpdateSystemSettingCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                PageSize with
                {
                    Key = key,
                    Value = command.Value,
                    Version = command.Version + 1,
                });
    }

    private sealed class FakeDashboardService : IDashboardService
    {
        public Task<DashboardSnapshot> GetAsync(
            DashboardAccessScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DashboardSnapshot(
                new DashboardMetrics(
                    scope.Customers ? 12 : null,
                    scope.Customers ? 2 : null,
                    scope.Opportunities ? 3 : null,
                    scope.Opportunities ? 450000m : null,
                    scope.Projects ? 4 : null,
                    scope.Projects ? 1 : null,
                    scope.Service ? 5 : null,
                    scope.Service ? 2 : null,
                    scope.Customers ? 1 : null,
                    scope.Customers ? 3 : null),
                scope.Opportunities ? [new DashboardDistributionItem("Lead", 3)] : [],
                scope.Projects ? [new DashboardDistributionItem("Active", 4)] : [],
                scope.Customers ? [new DashboardTrendPoint("2026-08", 2)] : []));
    }

    private sealed class FakeSupplierService : ISupplierService
    {
        public Task<PagedResult<SupplierSummary>> ListAsync(int page, int pageSize,
            string? search, string? category, string? archive, string? status,
            string? sortBy, bool sortDescending, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<SupplierSummary>([], page, pageSize, 0));

        public Task<SupplierDetails> GetAsync(ulong supplierId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<SupplierDetails> CreateAsync(CreateSupplierCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<SupplierDetails> UpdateAsync(ulong supplierId, UpdateSupplierCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<SupplierDetails> SetArchivedAsync(ulong supplierId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeReceivableService : IReceivableService
    {
        public Task<PagedResult<ReceivableSummary>> ListReceivablesAsync(int page, int pageSize,
            string? search, string? archive, ulong? customerId, ulong? projectId,
            string? status, string? receivableType, DateOnly? dueFrom, DateOnly? dueTo,
            bool? overdueOnly, string? sortBy, bool sortDescending,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<ReceivableSummary>([], page, pageSize, 0));

        public Task<PagedResult<ReceiptSummary>> ListReceiptsAsync(int page, int pageSize,
            string? search, string? archive, ulong? customerId, DateOnly? dateFrom,
            DateOnly? dateTo, bool? hasUnallocated, string? sortBy,
            bool sortDescending, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<ReceiptSummary>([], page, pageSize, 0));

        public Task<ReceivableDetails> GetReceivableAsync(ulong receivableId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceivableDetails> CreateReceivableAsync(CreateReceivableCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceivableDetails> UpdateReceivableAsync(ulong receivableId, UpdateReceivableCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceivableDetails> SetReceivableArchivedAsync(ulong receivableId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceiptDetails> GetReceiptAsync(ulong receiptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceiptDetails> CreateReceiptAsync(CreateReceiptCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceiptDetails> UpdateReceiptAsync(ulong receiptId, UpdateReceiptCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceiptDetails> SetReceiptArchivedAsync(ulong receiptId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceiptDetails> AllocateReceiptAsync(ulong receiptId, AllocateReceiptCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReceiptDetails> CancelReceiptAllocationAsync(ulong receiptId, ulong allocationId, CancelAllocationCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePurchaseOrderService : IPurchaseOrderService
    {
        public Task<PagedResult<PurchaseOrderSummary>> ListAsync(int page, int pageSize,
            string? search, string? archive, ulong? supplierId, ulong? projectId,
            string? status, DateOnly? orderFrom, DateOnly? orderTo, string? sortBy,
            bool sortDescending, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<PurchaseOrderSummary>([], page, pageSize, 0));

        public Task<PurchaseOrderDetails> GetAsync(ulong purchaseOrderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PurchaseOrderDetails> CreateAsync(CreatePurchaseOrderCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PurchaseOrderDetails> UpdateAsync(ulong purchaseOrderId, UpdatePurchaseOrderCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PurchaseOrderDetails> OrderAsync(ulong purchaseOrderId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PurchaseOrderDetails> CancelAsync(ulong purchaseOrderId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PurchaseOrderDetails> SetArchivedAsync(ulong purchaseOrderId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PurchaseOrderDetails> ReceiveAsync(ulong purchaseOrderId, CreatePurchaseReceiptCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePayableService : IPayableService
    {
        public Task<PagedResult<PayableSummary>> ListPayablesAsync(int page, int pageSize,
            string? search, string? archive, ulong? supplierId, ulong? projectId,
            ulong? purchaseOrderId, string? payableType, string? paymentStatus,
            bool? overdueOnly, DateOnly? dueFrom, DateOnly? dueTo, string? sortBy,
            bool sortDescending, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<PayableSummary>([], page, pageSize, 0));

        public Task<PagedResult<PaymentSummary>> ListPaymentsAsync(int page, int pageSize,
            string? search, string? archive, ulong? supplierId, DateOnly? dateFrom,
            DateOnly? dateTo, string? paymentMethod, bool? hasUnallocated,
            string? sortBy, bool sortDescending, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<PaymentSummary>([], page, pageSize, 0));

        public Task<PayableDetails> GetPayableAsync(ulong payableId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PayableDetails> CreatePayableAsync(CreatePayableCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PayableDetails> UpdatePayableAsync(ulong payableId, UpdatePayableCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PayableDetails> SetPayableArchivedAsync(ulong payableId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PayableDetails>> CreatePayablePlanAsync(ulong purchaseOrderId, CreatePayablePlanCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDetails> GetPaymentAsync(ulong paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDetails> CreatePaymentAsync(CreatePaymentCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDetails> UpdatePaymentAsync(ulong paymentId, UpdatePaymentCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDetails> SetPaymentArchivedAsync(ulong paymentId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDetails> AllocatePaymentAsync(ulong paymentId, AllocatePaymentCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymentDetails> CancelPaymentAllocationAsync(ulong paymentId, ulong allocationId, CancelAllocationCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeShipmentService : IShipmentService
    {
        public Task<PagedResult<ShipmentSummary>> ListAsync(int page, int pageSize,
            string? search, string? archive, ulong? customerId, ulong? projectId,
            string? status, DateOnly? shipmentFrom, DateOnly? shipmentTo,
            bool? isReceived, string? sortBy, bool sortDescending,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<ShipmentSummary>([], page, pageSize, 0));

        public Task<ShipmentDetails> GetAsync(ulong shipmentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShipmentDetails> CreateAsync(CreateShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShipmentDetails> UpdateAsync(ulong shipmentId, UpdateShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShipmentDetails> ShipAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShipmentDetails> SetInTransitAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShipmentDetails> ReceiveAsync(ulong shipmentId, ReceiveShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShipmentDetails> CancelAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ShipmentDetails> SetArchivedAsync(ulong shipmentId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ShipmentEquipmentCandidate>> ListEquipmentCandidatesAsync(ulong projectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ShipmentEquipmentCandidate>>([]);
        public Task<EquipmentShipmentLookup> GetEquipmentShipmentAsync(ulong equipmentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectShipmentMetrics> GetProjectMetricsAsync(ulong projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeFinanceOverviewService : IFinanceOverviewService
    {
        private static readonly DateOnly BusinessDate = new(2026, 9, 2);

        public Task<CustomerFinanceOverview> GetCustomerAsync(
            ulong customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerFinanceOverview(
                customerId, "CU-2026-0042", "测试客户", 1, 1, 100m, 80m, 60m, 20m,
                100m, 40m, 10m, 30m, 50m, 40m, 20m, 20m, 5m, 15m, 1, 1,
                BusinessDate, 50m, 50m, []));

        public Task<ProjectFinanceOverview> GetProjectAsync(
            ulong projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProjectFinanceOverview(
                projectId, 42, "TN-2026-0001", "测试项目", "CU-2026-0042", "测试客户",
                100m, 100m, 60m, 40m, 10m, 30m, 50m, 1, 1, 40m, 20m, 20m,
                5m, 15m, 1, 1, 0, BusinessDate, 50m, 50m, 40m,
                [], [], [], [], [], [], []));

        public Task<PagedResult<CustomerProjectFinanceRow>> ListCustomerProjectsAsync(
            ulong customerId,
            int page,
            int pageSize,
            string? sortBy,
            bool sortDescending,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<CustomerProjectFinanceRow>([], page, pageSize, 0));

        public Task<CompanyFinanceSummary> GetCompanyAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new CompanyFinanceSummary(
                BusinessDate, 100m, 40m, 10m, 80m, 80m, 60m, 60m, 50m, 50m,
                20m, 5m, 30m, 30m, 20m, 20m, 100m, 50m, 50m, 1, 50m, []));

        public Task<FinanceAgingOverview> GetReceivableAgingAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceAgingOverview(BusinessDate, 40m,
                [new FinanceAgingBucket("NotDue", 30m, 1),
                 new FinanceAgingBucket("1-30", 10m, 1)]));

        public Task<FinanceAgingOverview> GetPayableAgingAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceAgingOverview(BusinessDate, 20m,
                [new FinanceAgingBucket("NotDue", 15m, 1),
                 new FinanceAgingBucket("1-30", 5m, 1)]));

        public Task<FinanceDashboardSnapshot> GetDashboardAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceDashboardSnapshot(
                BusinessDate,
                new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc),
                new CompanyFinanceSummary(
                    BusinessDate, 100m, 40m, 10m, 80m, 80m, 60m, 60m, 50m, 50m,
                    20m, 5m, 30m, 30m, 20m, 20m, 100m, 50m, 50m, 1, 50m, []),
                new FinanceAgingOverview(BusinessDate, 40m,
                    [new FinanceAgingBucket("NotDue", 30m, 1),
                     new FinanceAgingBucket("1-30", 10m, 1)]),
                new FinanceAgingOverview(BusinessDate, 20m,
                    [new FinanceAgingBucket("NotDue", 15m, 1),
                     new FinanceAgingBucket("1-30", 5m, 1)]),
                [new FinanceCashFlowPoint("2026-09", 80m, 30m, 50m)],
                [new FinanceRiskAlert(
                    "overdue_receivable", "逾期应收", 1, 10m, "Danger",
                    "/finance/receivables")],
                [], [], [], "contractAmount", [], []));

        public Task<IReadOnlyList<FinanceProjectRankingRow>> GetProjectRankingAsync(
            string? sortBy,
            int limit,
            CancellationToken cancellationToken)
        {
            if (sortBy == "updatedAt")
            {
                throw FlowHearthValidationException.For("sortBy", "项目排行指标无效。");
            }

            return Task.FromResult<IReadOnlyList<FinanceProjectRankingRow>>([]);
        }

        public Task<ProjectFinanceOverview> CreateReceivablePlanAsync(
            ulong projectId,
            CreateReceivablePlanCommand command,
            ulong actorUserId,
            CancellationToken cancellationToken) =>
            GetProjectAsync(projectId, cancellationToken);
    }

    private sealed class FakeGlobalSearchService : IGlobalSearchService
    {
        public Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
            string? query,
            int limit,
            GlobalSearchAccessScope scope,
            CancellationToken cancellationToken)
        {
            var rows = new List<GlobalSearchResult>();
            if (scope.Customers)
            {
                rows.Add(new GlobalSearchResult("Contact", "Customer", 42, "CU-2026-0042", "测试联系人", "测试客户 · 13800000000", false));
            }

            if (scope.Projects)
            {
                rows.Add(new GlobalSearchResult("Project", "Project", 30, "TN-2026-0001", "测试项目", "测试客户", false));
            }

            return Task.FromResult<IReadOnlyList<GlobalSearchResult>>(
                rows.Take(limit).ToArray());
        }
    }
}
