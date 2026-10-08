using FlowHearth.Application.Common;
using FlowHearth.Application.Security;
using FlowHearth.Domain.Attachments;

namespace FlowHearth.Application.Attachments;

public static class AttachmentAccess
{
    public static string RequiredEntityViewPermission(AttachmentEntityType entityType) =>
        entityType switch
        {
            AttachmentEntityType.Customer => SecurityPermissions.CustomersView,
            AttachmentEntityType.Opportunity => SecurityPermissions.OpportunitiesView,
            AttachmentEntityType.Project => SecurityPermissions.ProjectsView,
            AttachmentEntityType.Equipment => SecurityPermissions.EquipmentView,
            AttachmentEntityType.ServiceTicket => SecurityPermissions.ServiceView,
            AttachmentEntityType.Supplier => SecurityPermissions.SuppliersView,
            AttachmentEntityType.PurchaseOrder => SecurityPermissions.PurchasesView,
            AttachmentEntityType.Shipment => SecurityPermissions.ShipmentsView,
            _ => throw FlowHearthValidationException.For(
                "entityType",
                "不支持该附件对象类型。"),
        };
}
