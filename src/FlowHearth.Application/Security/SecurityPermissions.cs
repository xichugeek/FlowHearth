namespace FlowHearth.Application.Security;

public static class SecurityPermissions
{
    public const string DashboardView = "dashboard.view";
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string OpportunitiesView = "opportunities.view";
    public const string OpportunitiesManage = "opportunities.manage";
    public const string ProjectsView = "projects.view";
    public const string ProjectsManage = "projects.manage";
    public const string EquipmentView = "equipment.view";
    public const string EquipmentManage = "equipment.manage";
    public const string ServiceView = "service.view";
    public const string ServiceManage = "service.manage";
    public const string AttachmentsView = "attachments.view";
    public const string AttachmentsManage = "attachments.manage";
    public const string AuditView = "audit.view";
    public const string SearchUse = "search.use";
    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";
    public const string SecurityUsersView = "security.users.view";
    public const string SecurityUsersManage = "security.users.manage";
    public const string SecurityRolesView = "security.roles.view";
    public const string SecurityRolesManage = "security.roles.manage";
    public const string FinanceDashboardView = "finance.dashboard.view";
    public const string SuppliersView = "suppliers.view";
    public const string SuppliersManage = "suppliers.manage";
    public const string ReceivablesView = "receivables.view";
    public const string ReceivablesManage = "receivables.manage";
    public const string ReceiptsView = "receipts.view";
    public const string ReceiptsManage = "receipts.manage";
    public const string PurchasesView = "purchases.view";
    public const string PurchasesManage = "purchases.manage";
    public const string PayablesView = "payables.view";
    public const string PayablesManage = "payables.manage";
    public const string PaymentsView = "payments.view";
    public const string PaymentsManage = "payments.manage";
    public const string ShipmentsView = "shipments.view";
    public const string ShipmentsManage = "shipments.manage";

    public static readonly IReadOnlyList<string> All =
    [
        DashboardView,
        CustomersView,
        CustomersManage,
        OpportunitiesView,
        OpportunitiesManage,
        ProjectsView,
        ProjectsManage,
        EquipmentView,
        EquipmentManage,
        ServiceView,
        ServiceManage,
        AttachmentsView,
        AttachmentsManage,
        AuditView,
        SearchUse,
        SettingsView,
        SettingsManage,
        SecurityUsersView,
        SecurityUsersManage,
        SecurityRolesView,
        SecurityRolesManage,
        FinanceDashboardView,
        SuppliersView,
        SuppliersManage,
        ReceivablesView,
        ReceivablesManage,
        ReceiptsView,
        ReceiptsManage,
        PurchasesView,
        PurchasesManage,
        PayablesView,
        PayablesManage,
        PaymentsView,
        PaymentsManage,
        ShipmentsView,
        ShipmentsManage,
    ];
}
