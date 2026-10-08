export interface DashboardMetrics {
  totalCustomers?: number | null
  newCustomersThisMonth?: number | null
  activeOpportunities?: number | null
  expectedOpportunityAmount?: number | null
  activeProjects?: number | null
  projectsNearingDelivery?: number | null
  openTickets?: number | null
  openPriorityTickets?: number | null
  dueFollowUpsToday?: number | null
  nextSevenDaysFollowUps?: number | null
}

export interface DashboardDistributionItem {
  key: string
  count: number
}

export interface DashboardTrendPoint {
  month: string
  count: number
}

export interface DashboardSnapshot {
  metrics: DashboardMetrics
  opportunityStages: DashboardDistributionItem[]
  projectStatuses: DashboardDistributionItem[]
  monthlyNewCustomers: DashboardTrendPoint[]
}
