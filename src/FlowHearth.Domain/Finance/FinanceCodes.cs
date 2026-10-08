namespace FlowHearth.Domain.Finance;

public static class FinanceCodes
{
    public const string SupplierPrefix = "SP";
    public const string ReceivablePrefix = "AR";
    public const string ReceiptPrefix = "RC";
    public const string PurchaseOrderPrefix = "PO";
    public const string PurchaseReceiptPrefix = "GR";
    public const string PayablePrefix = "AP";
    public const string PaymentPrefix = "PM";
    public const string ShipmentPrefix = "SH";

    public static string Format(string prefix, int year, ulong sequence) =>
        Common.BusinessCodeFormatter.Format(prefix, year, sequence);
}
