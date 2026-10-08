<script setup lang="ts">
import { Plus,Refresh,Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed,onMounted,ref,watch } from 'vue'
import { useRoute } from 'vue-router'
import * as customerApi from '../../api/customers'
import * as equipmentApi from '../../api/equipment'
import { ApiError } from '../../api/problem-details'
import * as projectApi from '../../api/projects'
import * as serviceApi from '../../api/service-tickets'
import ServiceAssignmentDialog from '../../components/service/ServiceAssignmentDialog.vue'
import ServiceRecordDialog from '../../components/service/ServiceRecordDialog.vue'
import ServiceTicketDetailDrawer from '../../components/service/ServiceTicketDetailDrawer.vue'
import ServiceTicketFormDialog from '../../components/service/ServiceTicketFormDialog.vue'
import ServiceTransitionDialog from '../../components/service/ServiceTransitionDialog.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { CustomerSummary } from '../../types/customers'
import type { EquipmentSummary } from '../../types/equipment'
import type { ProjectSummary } from '../../types/projects'
import type { ServiceAssignee,ServicePriority,ServiceRecordDetails,ServiceRecordInput,ServiceStatus,ServiceTicketDetails,ServiceTicketInput,ServiceTicketSummary,UpdateServiceRecordInput,UpdateServiceTicketInput } from '../../types/service'
import { formatChinaDateTime } from '../../utils/customer-format'
import { servicePriorities,servicePriorityLabel,servicePriorityTagType,serviceStatuses,serviceStatusLabel,serviceStatusTagType } from '../../utils/service-format'

const auth=useAuthStore();const settingsStore=useSettingsStore();const route=useRoute();const canManage=computed(()=>auth.canAny([permissions.serviceManage]));const loading=ref(false);const saving=ref(false)
const tickets=ref<ServiceTicketSummary[]>([]);const total=ref(0);const page=ref(1);const pageSize=ref(settingsStore.defaultPageSize);const search=ref('');const archive=ref<'active'|'archived'|'all'>('active');const priority=ref<ServicePriority|''>('');const status=ref<ServiceStatus|''>('')
const selected=ref<ServiceTicketDetails|null>(null);const editing=ref<ServiceTicketDetails|null>(null);const customers=ref<CustomerSummary[]>([]);const projects=ref<ProjectSummary[]>([]);const equipment=ref<EquipmentSummary[]>([]);const assignees=ref<ServiceAssignee[]>([]);const editingRecord=ref<ServiceRecordDetails|null>(null);const transitionTarget=ref<ServiceStatus|null>(null)
const detailVisible=ref(false);const formVisible=ref(false);const assignmentVisible=ref(false);const transitionVisible=ref(false);const recordVisible=ref(false)
onMounted(async()=>{await Promise.all([loadTickets(),searchCustomers(''),loadAssignees()]);await openTicketFromRoute(route.query.entityId)})
watch(()=>route.query.entityId,openTicketFromRoute)
async function loadTickets(){loading.value=true;try{const result=await serviceApi.listServiceTickets({page:page.value,pageSize:pageSize.value,search:search.value.trim()||undefined,archive:archive.value,priority:priority.value||undefined,status:status.value||undefined,sortBy:'updatedAt',sortDescending:true});tickets.value=result.items;total.value=result.total}catch(error){showError(error,'工单列表加载失败。')}finally{loading.value=false}}
async function searchCustomers(query:string){try{customers.value=(await customerApi.listCustomers({page:1,pageSize:100,search:query.trim()||undefined,archive:'active',sortBy:'name',sortDescending:false})).items}catch(error){showError(error,'客户选项加载失败。')}}
async function loadRelations(customerId:number){try{const [projectResult,equipmentResult]=await Promise.all([projectApi.listProjects({page:1,pageSize:100,customerId,archive:'active',sortBy:'updatedAt',sortDescending:true}),equipmentApi.listEquipment({page:1,pageSize:100,customerId,archive:'active',sortBy:'updatedAt',sortDescending:true})]);projects.value=projectResult.items;equipment.value=equipmentResult.items}catch(error){showError(error,'项目/设备选项加载失败。')}}
async function loadAssignees(){if(!canManage.value)return;try{assignees.value=await serviceApi.listAssignees()}catch(error){showError(error,'指派用户加载失败。')}}
async function openTicket(row:ServiceTicketSummary){await openTicketById(row.id)}
async function openTicketFromRoute(value:unknown){const raw=Array.isArray(value)?value[0]:value;const id=Number(raw);if(!Number.isSafeInteger(id)||id<=0||selected.value?.id===id)return;await openTicketById(id)}
async function openTicketById(ticketId:number){loading.value=true;try{selected.value=await serviceApi.getServiceTicket(ticketId);detailVisible.value=true}catch(error){showError(error,'工单详情加载失败。')}finally{loading.value=false}}
async function refresh(){if(selected.value)selected.value=await serviceApi.getServiceTicket(selected.value.id)}
function openCreate(){editing.value=null;projects.value=[];equipment.value=[];formVisible.value=true}async function openEdit(){if(!selected.value)return;editing.value=selected.value;await Promise.all([searchCustomers(selected.value.customerCode),loadRelations(selected.value.customerId)]);formVisible.value=true}
async function saveTicket(input:ServiceTicketInput|UpdateServiceTicketInput){saving.value=true;try{const saved=editing.value&&'version'in input?await serviceApi.updateServiceTicket(editing.value.id,input):await serviceApi.createServiceTicket(input as ServiceTicketInput);selected.value=saved;formVisible.value=false;detailVisible.value=true;await loadTickets();ElMessage.success(editing.value?'工单已更新。':'工单已创建。')}catch(error){showError(error,'工单保存失败。')}finally{saving.value=false}}
async function saveAssignment(userId:number|null){if(!selected.value)return;saving.value=true;try{selected.value=await serviceApi.assignServiceTicket(selected.value.id,userId,selected.value.version);assignmentVisible.value=false;await loadTickets();ElMessage.success('工单指派已更新。')}catch(error){showError(error,'工单指派失败。')}finally{saving.value=false}}
function chooseTransition(target:ServiceStatus){transitionTarget.value=target;transitionVisible.value=true}async function saveTransition(input:{status:ServiceStatus;rootCause:string|null;solution:string|null;downtimeMinutes:number|null}){if(!selected.value)return;saving.value=true;try{selected.value=await serviceApi.transitionServiceTicket(selected.value.id,input.status,selected.value.version,input.rootCause,input.solution,input.downtimeMinutes);transitionVisible.value=false;await loadTickets();ElMessage.success('工单状态已更新。')}catch(error){showError(error,'工单状态更新失败。')}finally{saving.value=false}}
async function toggleArchive(){if(!selected.value)return;const action=selected.value.isArchived?'恢复':'归档';try{await ElMessageBox.confirm(`${action}工单“${selected.value.title}”？`,`${action}工单`);selected.value=await serviceApi.setServiceTicketArchived(selected.value.id,!selected.value.isArchived,selected.value.version);await loadTickets();ElMessage.success(`工单已${action}。`)}catch(error){if(error==='cancel'||error==='close')return;showError(error,`工单${action}失败。`)}}
function addRecord(){editingRecord.value=null;recordVisible.value=true}function editRecord(item:ServiceRecordDetails){editingRecord.value=item;recordVisible.value=true}async function saveRecord(input:ServiceRecordInput|UpdateServiceRecordInput){if(!selected.value)return;saving.value=true;try{if(editingRecord.value&&'version'in input)await serviceApi.updateServiceRecord(selected.value.id,editingRecord.value.id,input);else await serviceApi.createServiceRecord(selected.value.id,input as ServiceRecordInput);recordVisible.value=false;await Promise.all([refresh(),loadTickets()]);ElMessage.success('服务记录已保存。')}catch(error){showError(error,'服务记录保存失败。')}finally{saving.value=false}}async function deleteRecord(item:ServiceRecordDetails){if(!selected.value)return;try{await ElMessageBox.confirm('删除该服务记录？','删除记录');await serviceApi.deleteServiceRecord(selected.value.id,item.id,item.version);await Promise.all([refresh(),loadTickets()]);ElMessage.success('服务记录已删除。')}catch(error){if(error==='cancel'||error==='close')return;showError(error,'服务记录删除失败。')}}
async function runSearch(){page.value=1;await loadTickets()}function showError(error:unknown,fallback:string){if(error instanceof ApiError){const validation=Object.values(error.problem?.errors??{}).flat()[0];ElMessage.error(validation??error.problem?.detail??error.problem?.title??fallback);return}ElMessage.error(fallback)}
</script>
<template>
  <section class="service-page">
    <div class="service-toolbar">
      <el-input
        v-model="search"
        clearable
        :prefix-icon="Search"
        placeholder="搜索工单、客户、项目或设备"
        @keyup.enter="runSearch"
        @clear="runSearch"
      /><el-select
        v-model="priority"
        clearable
        placeholder="全部优先级"
        @change="runSearch"
      >
        <el-option
          v-for="item in servicePriorities"
          :key="item"
          :label="servicePriorityLabel(item)"
          :value="item"
        />
      </el-select><el-select
        v-model="status"
        clearable
        placeholder="全部状态"
        @change="runSearch"
      >
        <el-option
          v-for="item in serviceStatuses"
          :key="item"
          :label="serviceStatusLabel(item)"
          :value="item"
        />
      </el-select><el-select
        v-model="archive"
        @change="runSearch"
      >
        <el-option
          label="在用工单"
          value="active"
        /><el-option
          label="已归档"
          value="archived"
        /><el-option
          label="全部工单"
          value="all"
        />
      </el-select><el-button
        :icon="Refresh"
        @click="loadTickets"
      >
        刷新
      </el-button>
      <el-button
        v-if="canManage"
        class="toolbar-primary-action"
        type="primary"
        :icon="Plus"
        @click="openCreate"
      >
        新建工单
      </el-button>
    </div>
    <el-table
      v-loading="loading"
      class="flowhearth-data-table"
      :data="tickets"
      border
      stripe
      row-class-name="customer-table-row"
      @row-click="openTicket"
    >
      <el-table-column
        prop="code"
        label="工单编号"
        width="150"
      /><el-table-column
        label="工单"
        min-width="250"
      >
        <template #default="scope">
          <strong>{{ scope.row.title }}</strong><p class="cell-secondary">
            {{ scope.row.customerName }} · {{ scope.row.equipmentName||scope.row.projectName||'未关联设备/项目' }}
          </p>
        </template>
      </el-table-column><el-table-column
        label="优先级"
        width="105"
      >
        <template #default="scope">
          <el-tag :type="servicePriorityTagType(scope.row.priority)">
            {{ servicePriorityLabel(scope.row.priority) }}
          </el-tag>
        </template>
      </el-table-column><el-table-column
        label="状态"
        width="110"
      >
        <template #default="scope">
          <el-tag :type="serviceStatusTagType(scope.row.status)">
            {{ serviceStatusLabel(scope.row.status) }}
          </el-tag>
        </template>
      </el-table-column><el-table-column
        label="负责人"
        width="140"
      >
        <template #default="scope">
          {{ scope.row.assignedDisplayName||'未指派' }}
        </template>
      </el-table-column><el-table-column
        prop="recordCount"
        label="记录"
        width="80"
      /><el-table-column
        label="报修时间"
        width="175"
      >
        <template #default="scope">
          {{ formatChinaDateTime(scope.row.reportedAtUtc) }}
        </template>
      </el-table-column>
    </el-table><el-empty
      v-if="!loading&&tickets.length===0"
      description="暂无符合条件的服务工单"
    /><el-pagination
      v-if="total>0"
      v-model:current-page="page"
      v-model:page-size="pageSize"
      class="table-pagination"
      layout="total, sizes, prev, pager, next"
      :page-sizes="[10,20,50,100]"
      :total="total"
      @change="loadTickets"
    />
  </section>
  <ServiceTicketDetailDrawer
    v-model="detailVisible"
    :ticket="selected"
    :can-manage="canManage"
    :busy="saving"
    @edit="openEdit"
    @assign="assignmentVisible=true"
    @transition="chooseTransition"
    @archive="toggleArchive"
    @add-record="addRecord"
    @edit-record="editRecord"
    @delete-record="deleteRecord"
  /><ServiceTicketFormDialog
    v-model="formVisible"
    :ticket="editing"
    :customers="customers"
    :projects="projects"
    :equipment="equipment"
    :assignees="assignees"
    :saving="saving"
    @search-customer="searchCustomers"
    @customer-change="loadRelations"
    @save="saveTicket"
  /><ServiceAssignmentDialog
    v-model="assignmentVisible"
    :ticket="selected"
    :assignees="assignees"
    :saving="saving"
    @save="saveAssignment"
  /><ServiceTransitionDialog
    v-model="transitionVisible"
    :ticket="selected"
    :target="transitionTarget"
    :saving="saving"
    @save="saveTransition"
  /><ServiceRecordDialog
    v-model="recordVisible"
    :record="editingRecord"
    :saving="saving"
    @save="saveRecord"
  />
</template>
