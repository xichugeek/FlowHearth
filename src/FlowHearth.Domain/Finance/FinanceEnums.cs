namespace FlowHearth.Domain.Finance;

public enum SupplierStatus
{
    Active,
    Inactive,
}

public enum ReceivableType
{
    AdvancePayment,
    ShipmentPayment,
    AcceptancePayment,
    WarrantyRetention,
    ProgressPayment,
    Other,
}

public enum PaymentMethod
{
    BankTransfer,
    Cash,
    Cheque,
    Other,
}

public enum PayableType
{
    AdvancePayment,
    DeliveryPayment,
    AcceptancePayment,
    RetentionPayment,
    PurchasePayment,
    Other,
}

public enum PaymentStatus
{
    Unpaid,
    PartiallyPaid,
    Paid,
}

public enum PayableAgingBucket
{
    NotDue,
    Days1To30,
    Days31To60,
    Days61To90,
    Over90Days,
}

public enum FinanceBalanceStatus
{
    NotDue,
    PartiallyPaid,
    Paid,
    Overdue,
    PartiallyOverdue,
}

public enum PurchaseOrderStatus
{
    Draft,
    Ordered,
    PartiallyReceived,
    Received,
    Cancelled,
}

public enum ShipmentStatus
{
    Preparing,
    Shipped,
    InTransit,
    Received,
    Cancelled,
}
