import type { CompanyFinanceSummary, CustomerFinanceOverview, CustomerProjectFinanceRow, EquipmentShipmentLookup, FinanceAgingOverview, FinanceDashboardSnapshot, FinanceProjectRankingMetric, FinanceProjectRankingRow, Page, PayableDetails, PayableInput, PayablePlanItemInput, PayableSummary, PayableUpdateInput, PaymentDetails, PaymentInput, PaymentSummary, PaymentUpdateInput, ProjectFinanceOverview, ProjectShipmentMetrics, PurchaseOrderDetails, PurchaseOrderInput, PurchaseOrderSummary, PurchaseOrderUpdateInput, PurchaseReceiptInput, ReceiptDetails, ReceiptInput, ReceiptSummary, ReceiptUpdateInput, ReceivableDetails, ReceivableInput, ReceivablePlanItemInput, ReceivableSummary, ReceivableUpdateInput, ShipmentDetails, ShipmentEquipmentCandidate, ShipmentInput, ShipmentSummary, ShipmentUpdateInput, SupplierDetails, SupplierInput, SupplierSummary, SupplierUpdateInput } from '../types/finance'
import { apiClient } from './client'
import { getCsrfHeaders } from './csrf'

const mutation = async () => ({ headers: await getCsrfHeaders() })

export async function listSuppliers(params: Record<string, unknown>) { return (await apiClient.get<Page<SupplierSummary>>('/suppliers', { params })).data }
export async function getSupplier(id: number) { return (await apiClient.get<SupplierDetails>(`/suppliers/${id}`)).data }
export async function createSupplier(input: SupplierInput) { return (await apiClient.post<SupplierDetails>('/suppliers', input, await mutation())).data }
export async function updateSupplier(id: number, input: SupplierUpdateInput) { return (await apiClient.put<SupplierDetails>(`/suppliers/${id}`, input, await mutation())).data }
export async function setSupplierArchived(id: number, archived: boolean, version: number) { return (await apiClient.post<SupplierDetails>(`/suppliers/${id}/${archived ? 'archive' : 'restore'}`, { version }, await mutation())).data }

export async function listPurchaseOrders(params: Record<string, unknown>) { return (await apiClient.get<Page<PurchaseOrderSummary>>('/purchase-orders', { params })).data }
export async function getPurchaseOrder(id: number) { return (await apiClient.get<PurchaseOrderDetails>(`/purchase-orders/${id}`)).data }
export async function createPurchaseOrder(input: PurchaseOrderInput) { return (await apiClient.post<PurchaseOrderDetails>('/purchase-orders', input, await mutation())).data }
export async function updatePurchaseOrder(id: number, input: PurchaseOrderUpdateInput) { return (await apiClient.put<PurchaseOrderDetails>(`/purchase-orders/${id}`, input, await mutation())).data }
export async function transitionPurchaseOrder(id: number, action: 'order' | 'cancel', version: number) { return (await apiClient.post<PurchaseOrderDetails>(`/purchase-orders/${id}/${action}`, { version }, await mutation())).data }
export async function setPurchaseOrderArchived(id: number, archived: boolean, version: number) { return (await apiClient.post<PurchaseOrderDetails>(`/purchase-orders/${id}/${archived ? 'archive' : 'restore'}`, { version }, await mutation())).data }
export async function receivePurchaseOrder(id: number, input: PurchaseReceiptInput) { return (await apiClient.post<PurchaseOrderDetails>(`/purchase-orders/${id}/receipts`, input, await mutation())).data }
export async function createPayablePlan(id: number, items: PayablePlanItemInput[]) { return (await apiClient.post<PayableDetails[]>(`/purchase-orders/${id}/payable-plan`, { items }, await mutation())).data }

export async function listReceivables(params: Record<string, unknown>) { return (await apiClient.get<Page<ReceivableSummary>>('/receivables', { params })).data }
export async function getReceivable(id: number) { return (await apiClient.get<ReceivableDetails>(`/receivables/${id}`)).data }
export async function createReceivable(input: ReceivableInput) { return (await apiClient.post<ReceivableDetails>('/receivables', input, await mutation())).data }
export async function updateReceivable(id: number, input: ReceivableUpdateInput) { return (await apiClient.put<ReceivableDetails>(`/receivables/${id}`, input, await mutation())).data }
export async function setReceivableArchived(id: number, archived: boolean, version: number) { return (await apiClient.post<ReceivableDetails>(`/receivables/${id}/${archived ? 'archive' : 'restore'}`, { version }, await mutation())).data }

export async function listReceipts(params: Record<string, unknown>) { return (await apiClient.get<Page<ReceiptSummary>>('/receipts', { params })).data }
export async function getReceipt(id: number) { return (await apiClient.get<ReceiptDetails>(`/receipts/${id}`)).data }
export async function createReceipt(input: ReceiptInput) { return (await apiClient.post<ReceiptDetails>('/receipts', input, await mutation())).data }
export async function updateReceipt(id: number, input: ReceiptUpdateInput) { return (await apiClient.put<ReceiptDetails>(`/receipts/${id}`, input, await mutation())).data }
export async function setReceiptArchived(id: number, archived: boolean, version: number) { return (await apiClient.post<ReceiptDetails>(`/receipts/${id}/${archived ? 'archive' : 'restore'}`, { version }, await mutation())).data }
export async function allocateReceipt(id: number, receiptVersion: number, allocations: Array<{ receivableId: number; amount: number }>) { return (await apiClient.post<ReceiptDetails>(`/receipts/${id}/allocations`, { receiptVersion, allocations }, await mutation())).data }
export async function cancelReceiptAllocation(receiptId: number, allocationId: number, version: number) { return (await apiClient.delete<ReceiptDetails>(`/receipts/${receiptId}/allocations/${allocationId}`, { ...(await mutation()), data: { version } })).data }

export async function listPayables(params: Record<string, unknown>) { return (await apiClient.get<Page<PayableSummary>>('/payables', { params })).data }
export async function getPayable(id: number) { return (await apiClient.get<PayableDetails>(`/payables/${id}`)).data }
export async function createPayable(input: PayableInput) { return (await apiClient.post<PayableDetails>('/payables', input, await mutation())).data }
export async function updatePayable(id: number, input: PayableUpdateInput) { return (await apiClient.put<PayableDetails>(`/payables/${id}`, input, await mutation())).data }
export async function setPayableArchived(id: number, archived: boolean, version: number) { return (await apiClient.post<PayableDetails>(`/payables/${id}/${archived ? 'archive' : 'restore'}`, { version }, await mutation())).data }

export async function listPayments(params: Record<string, unknown>) { return (await apiClient.get<Page<PaymentSummary>>('/payments', { params })).data }
export async function getPayment(id: number) { return (await apiClient.get<PaymentDetails>(`/payments/${id}`)).data }
export async function createPayment(input: PaymentInput) { return (await apiClient.post<PaymentDetails>('/payments', input, await mutation())).data }
export async function updatePayment(id: number, input: PaymentUpdateInput) { return (await apiClient.put<PaymentDetails>(`/payments/${id}`, input, await mutation())).data }
export async function setPaymentArchived(id: number, archived: boolean, version: number) { return (await apiClient.post<PaymentDetails>(`/payments/${id}/${archived ? 'archive' : 'restore'}`, { version }, await mutation())).data }
export async function allocatePayment(id: number, paymentVersion: number, allocations: Array<{ payableId: number; amount: number }>) { return (await apiClient.post<PaymentDetails>(`/payments/${id}/allocations`, { paymentVersion, allocations }, await mutation())).data }
export async function cancelPaymentAllocation(paymentId: number, allocationId: number, version: number) { return (await apiClient.delete<PaymentDetails>(`/payments/${paymentId}/allocations/${allocationId}`, { ...(await mutation()), data: { version } })).data }

export async function listShipments(params: Record<string, unknown>) { return (await apiClient.get<Page<ShipmentSummary>>('/shipments', { params })).data }
export async function getShipment(id: number) { return (await apiClient.get<ShipmentDetails>(`/shipments/${id}`)).data }
export async function createShipment(input: ShipmentInput) { return (await apiClient.post<ShipmentDetails>('/shipments', input, await mutation())).data }
export async function updateShipment(id: number, input: ShipmentUpdateInput) { return (await apiClient.put<ShipmentDetails>(`/shipments/${id}`, input, await mutation())).data }
export async function transitionShipment(id: number, action: 'ship' | 'in-transit' | 'cancel', version: number) { return (await apiClient.post<ShipmentDetails>(`/shipments/${id}/${action}`, { version }, await mutation())).data }
export async function receiveShipment(id: number, signedAt: string, receiverName: string | null, remark: string | null, version: number) { return (await apiClient.post<ShipmentDetails>(`/shipments/${id}/receive`, { signedAt, receiverName, remark, version }, await mutation())).data }
export async function setShipmentArchived(id: number, archived: boolean, version: number) { return (await apiClient.post<ShipmentDetails>(`/shipments/${id}/${archived ? 'archive' : 'restore'}`, { version }, await mutation())).data }
export async function listShipmentEquipmentCandidates(projectId: number) { return (await apiClient.get<ShipmentEquipmentCandidate[]>('/shipments/equipment-candidates', { params: { projectId } })).data }
export async function getProjectShipmentMetrics(projectId: number) { return (await apiClient.get<ProjectShipmentMetrics>(`/projects/${projectId}/shipment-metrics`)).data }
export async function getEquipmentShipment(equipmentId: number) { return (await apiClient.get<EquipmentShipmentLookup>(`/equipment/${equipmentId}/shipment`)).data }

export async function getCustomerFinance(customerId: number) { return (await apiClient.get<CustomerFinanceOverview>(`/customers/${customerId}/finance`)).data }
export async function getProjectFinance(projectId: number) { return (await apiClient.get<ProjectFinanceOverview>(`/projects/${projectId}/finance`)).data }
export async function listCustomerProjectFinance(customerId: number, params: Record<string, unknown>) { return (await apiClient.get<Page<CustomerProjectFinanceRow>>(`/customers/${customerId}/project-finance`, { params })).data }
export async function getCompanyFinanceSummary() { return (await apiClient.get<CompanyFinanceSummary>('/finance/summary')).data }
export async function getReceivableAging() { return (await apiClient.get<FinanceAgingOverview>('/finance/receivable-aging')).data }
export async function getPayableAging() { return (await apiClient.get<FinanceAgingOverview>('/finance/payable-aging')).data }
export async function getFinanceDashboard() { return (await apiClient.get<FinanceDashboardSnapshot>('/finance/dashboard')).data }
export async function getFinanceProjectRanking(sortBy: FinanceProjectRankingMetric, limit = 10) { return (await apiClient.get<FinanceProjectRankingRow[]>('/finance/project-ranking', { params: { sortBy, limit } })).data }
export async function createReceivablePlan(projectId: number, items: ReceivablePlanItemInput[]) { return (await apiClient.post<ProjectFinanceOverview>(`/projects/${projectId}/receivable-plan`, { items }, await mutation())).data }
