import type {
  ContactDetails,
  ContactInput,
  CustomerDetails,
  CustomerFollowUpDetails,
  CustomerFollowUpInput,
  CustomerInput,
  CustomerListParameters,
  CustomerPage,
  UpdateContactInput,
  UpdateCustomerInput,
} from '../types/customers'
import { apiClient } from './client'
import { getCsrfHeaders } from './csrf'

export async function listCustomers(parameters: CustomerListParameters) {
  const response = await apiClient.get<CustomerPage>('/customers', {
    params: parameters,
  })
  return response.data
}

export async function getCustomer(customerId: number) {
  const response = await apiClient.get<CustomerDetails>(`/customers/${customerId}`)
  return response.data
}

export async function createCustomer(input: CustomerInput) {
  const response = await apiClient.post<CustomerDetails>('/customers', input, {
    headers: await getCsrfHeaders(),
  })
  return response.data
}

export async function updateCustomer(
  customerId: number,
  input: UpdateCustomerInput,
) {
  const response = await apiClient.put<CustomerDetails>(
    `/customers/${customerId}`,
    input,
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function setCustomerArchived(
  customerId: number,
  archived: boolean,
  version: number,
) {
  const action = archived ? 'archive' : 'restore'
  const response = await apiClient.post<CustomerDetails>(
    `/customers/${customerId}/${action}`,
    { version },
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function createContact(customerId: number, input: ContactInput) {
  const response = await apiClient.post<ContactDetails>(
    `/customers/${customerId}/contacts`,
    input,
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function updateContact(
  customerId: number,
  contactId: number,
  input: UpdateContactInput,
) {
  const response = await apiClient.put<ContactDetails>(
    `/customers/${customerId}/contacts/${contactId}`,
    input,
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function deleteContact(
  customerId: number,
  contactId: number,
  version: number,
) {
  await apiClient.delete(`/customers/${customerId}/contacts/${contactId}`, {
    params: { version },
    headers: await getCsrfHeaders(),
  })
}

export async function createFollowUp(
  customerId: number,
  input: CustomerFollowUpInput,
) {
  const response = await apiClient.post<CustomerFollowUpDetails>(
    `/customers/${customerId}/followups`,
    input,
    { headers: await getCsrfHeaders() },
  )
  return response.data
}
