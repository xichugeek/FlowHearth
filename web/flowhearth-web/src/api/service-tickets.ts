import type { ServiceAssignee, ServiceRecordDetails, ServiceRecordInput, ServiceStatus, ServiceTicketDetails, ServiceTicketInput, ServiceTicketListParameters, ServiceTicketPage, UpdateServiceRecordInput, UpdateServiceTicketInput } from '../types/service'
import { apiClient } from './client'
import { getCsrfHeaders } from './csrf'

export async function listServiceTickets(parameters: ServiceTicketListParameters) { return (await apiClient.get<ServiceTicketPage>('/service-tickets', { params: parameters })).data }
export async function getServiceTicket(id: number) { return (await apiClient.get<ServiceTicketDetails>(`/service-tickets/${id}`)).data }
export async function listAssignees() { return (await apiClient.get<ServiceAssignee[]>('/service-tickets/assignees')).data }
export async function createServiceTicket(input: ServiceTicketInput) { return (await apiClient.post<ServiceTicketDetails>('/service-tickets', input, { headers: await getCsrfHeaders() })).data }
export async function updateServiceTicket(id: number, input: UpdateServiceTicketInput) { return (await apiClient.put<ServiceTicketDetails>(`/service-tickets/${id}`, input, { headers: await getCsrfHeaders() })).data }
export async function assignServiceTicket(id: number, userId: number | null, version: number) { return (await apiClient.post<ServiceTicketDetails>(`/service-tickets/${id}/assign`, { userId, version }, { headers: await getCsrfHeaders() })).data }
export async function transitionServiceTicket(id: number, status: ServiceStatus, version: number, rootCause?: string | null, solution?: string | null, downtimeMinutes?: number | null) { return (await apiClient.post<ServiceTicketDetails>(`/service-tickets/${id}/transition`, { status, version, rootCause, solution, downtimeMinutes }, { headers: await getCsrfHeaders() })).data }
export async function setServiceTicketArchived(id: number, archived: boolean, version: number) { const action = archived ? 'archive' : 'restore'; return (await apiClient.post<ServiceTicketDetails>(`/service-tickets/${id}/${action}`, { version }, { headers: await getCsrfHeaders() })).data }
export async function createServiceRecord(ticketId: number, input: ServiceRecordInput) { return (await apiClient.post<ServiceRecordDetails>(`/service-tickets/${ticketId}/records`, input, { headers: await getCsrfHeaders() })).data }
export async function updateServiceRecord(ticketId: number, recordId: number, input: UpdateServiceRecordInput) { return (await apiClient.put<ServiceRecordDetails>(`/service-tickets/${ticketId}/records/${recordId}`, input, { headers: await getCsrfHeaders() })).data }
export async function deleteServiceRecord(ticketId: number, recordId: number, version: number) { await apiClient.delete(`/service-tickets/${ticketId}/records/${recordId}`, { params: { version }, headers: await getCsrfHeaders() }) }
