<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import dayjs from 'dayjs'
import { computed, onMounted, reactive, ref } from 'vue'

import * as securityApi from '../../api/security-administration'
import { ApiError } from '../../api/problem-details'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type {
  PermissionDetails,
  RoleDetails,
  UserDetails,
} from '../../types/security'

interface UserFormState {
  id: number | null
  username: string
  displayName: string
  email: string
  password: string
  isActive: boolean
  roleIds: number[]
  version: number
}

interface RoleFormState {
  id: number | null
  code: string
  name: string
  description: string
  isActive: boolean
  permissionIds: number[]
  version: number
}

const authStore = useAuthStore()
const settingsStore = useSettingsStore()
const canViewUsers = computed(() =>
  authStore.canAny([permissions.securityUsersView]),
)
const canManageUsers = computed(() =>
  authStore.canAny([permissions.securityUsersManage]),
)
const canViewRoles = computed(() =>
  authStore.canAny([permissions.securityRolesView]),
)
const canManageRoles = computed(() =>
  authStore.canAny([permissions.securityRolesManage]),
)

const activeTab = ref(canViewUsers.value ? 'users' : 'roles')
const loading = ref(false)
const saving = ref(false)
const users = ref<UserDetails[]>([])
const roles = ref<RoleDetails[]>([])
const allPermissions = ref<PermissionDetails[]>([])
const search = ref('')
const page = ref(1)
const pageSize = ref(settingsStore.defaultPageSize)
const total = ref(0)
const userDialogVisible = ref(false)
const roleDialogVisible = ref(false)

const userForm = reactive<UserFormState>({
  id: null,
  username: '',
  displayName: '',
  email: '',
  password: '',
  isActive: true,
  roleIds: [],
  version: 0,
})
const roleForm = reactive<RoleFormState>({
  id: null,
  code: '',
  name: '',
  description: '',
  isActive: true,
  permissionIds: [],
  version: 0,
})

const groupedPermissions = computed(() => {
  const groups = new Map<string, PermissionDetails[]>()
  for (const permission of allPermissions.value) {
    const group = groups.get(permission.module) ?? []
    group.push(permission)
    groups.set(permission.module, group)
  }
  return [...groups.entries()].map(([module, items]) => ({ module, items }))
})

onMounted(loadAll)

async function loadAll() {
  loading.value = true
  try {
    const tasks: Promise<unknown>[] = []
    if (canViewUsers.value) tasks.push(loadUsers())
    if (canViewRoles.value) {
      tasks.push(loadRoles())
      tasks.push(loadPermissions())
    }
    await Promise.all(tasks)
  } catch (error) {
    showError(error, '安全配置加载失败。')
  } finally {
    loading.value = false
  }
}

async function loadUsers() {
  const result = await securityApi.listUsers(
    page.value,
    pageSize.value,
    search.value.trim(),
  )
  users.value = result.items
  total.value = result.total
}

async function loadRoles() {
  roles.value = await securityApi.listRoles()
}

async function loadPermissions() {
  allPermissions.value = await securityApi.listPermissions()
}

async function runSearch() {
  page.value = 1
  loading.value = true
  try {
    await loadUsers()
  } catch (error) {
    showError(error, '用户查询失败。')
  } finally {
    loading.value = false
  }
}

function openCreateUser() {
  Object.assign(userForm, {
    id: null,
    username: '',
    displayName: '',
    email: '',
    password: '',
    isActive: true,
    roleIds: [],
    version: 0,
  })
  userDialogVisible.value = true
}

function openEditUser(value: unknown) {
  const user = value as UserDetails
  Object.assign(userForm, {
    id: user.id,
    username: user.username,
    displayName: user.displayName,
    email: user.email ?? '',
    password: '',
    isActive: user.isActive,
    roleIds: user.roles.map((role) => role.id),
    version: user.version,
  })
  userDialogVisible.value = true
}

async function saveUser() {
  if (!userForm.username.trim() || !userForm.displayName.trim()) {
    ElMessage.warning('请填写用户名和显示名称。')
    return
  }
  if (userForm.roleIds.length === 0) {
    ElMessage.warning('请至少选择一个角色。')
    return
  }
  if (userForm.id === null && !userForm.password) {
    ElMessage.warning('请设置初始密码。')
    return
  }

  saving.value = true
  try {
    if (userForm.id === null) {
      await securityApi.createUser({
        username: userForm.username.trim(),
        displayName: userForm.displayName.trim(),
        email: userForm.email.trim() || null,
        password: userForm.password,
        roleIds: userForm.roleIds,
      })
      ElMessage.success('用户已创建。')
    } else {
      await securityApi.updateUser(userForm.id, {
        displayName: userForm.displayName.trim(),
        email: userForm.email.trim() || null,
        isActive: userForm.isActive,
        roleIds: userForm.roleIds,
        version: userForm.version,
      })
      ElMessage.success('用户已更新。')
    }
    userDialogVisible.value = false
    await loadUsers()
  } catch (error) {
    showError(error, '用户保存失败。')
  } finally {
    saving.value = false
  }
}

async function resetPassword(value: unknown) {
  const user = value as UserDetails
  try {
    const value = await ElMessageBox.prompt(
      `为 ${user.displayName} 设置新密码`,
      '重置密码',
      {
        inputType: 'password',
        inputPlaceholder: '至少 12 位复杂密码',
        confirmButtonText: '确认重置',
        cancelButtonText: '取消',
        inputValidator: (input) => input.length >= 12 || '密码至少需要 12 位。',
      },
    )
    await securityApi.resetUserPassword(user.id, value.value)
    ElMessage.success('密码已重置，用户的旧会话已失效。')
    await loadUsers()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    showError(error, '密码重置失败。')
  }
}

function openCreateRole() {
  Object.assign(roleForm, {
    id: null,
    code: '',
    name: '',
    description: '',
    isActive: true,
    permissionIds: [],
    version: 0,
  })
  roleDialogVisible.value = true
}

function openEditRole(value: unknown) {
  const role = value as RoleDetails
  Object.assign(roleForm, {
    id: role.id,
    code: role.code,
    name: role.name,
    description: role.description ?? '',
    isActive: role.isActive,
    permissionIds: role.permissions.map((permission) => permission.id),
    version: role.version,
  })
  roleDialogVisible.value = true
}

async function saveRole() {
  if (!roleForm.code.trim() || !roleForm.name.trim()) {
    ElMessage.warning('请填写角色代码和名称。')
    return
  }
  if (roleForm.permissionIds.length === 0) {
    ElMessage.warning('请至少选择一个权限。')
    return
  }

  saving.value = true
  try {
    if (roleForm.id === null) {
      await securityApi.createRole({
        code: roleForm.code.trim(),
        name: roleForm.name.trim(),
        description: roleForm.description.trim() || null,
        permissionIds: roleForm.permissionIds,
      })
      ElMessage.success('角色已创建。')
    } else {
      await securityApi.updateRole(roleForm.id, {
        name: roleForm.name.trim(),
        description: roleForm.description.trim() || null,
        isActive: roleForm.isActive,
        permissionIds: roleForm.permissionIds,
        version: roleForm.version,
      })
      ElMessage.success('角色已更新，相关用户旧会话已失效。')
    }
    roleDialogVisible.value = false
    await loadRoles()
  } catch (error) {
    showError(error, '角色保存失败。')
  } finally {
    saving.value = false
  }
}

function formatDate(value?: string | null) {
  return value ? dayjs(value).format('YYYY-MM-DD HH:mm') : '—'
}

function showError(error: unknown, fallback: string) {
  ElMessage.error(error instanceof ApiError ? error.message : fallback)
}
</script>

<template>
  <section class="security-page">
    <div class="compact-page-actions">
      <el-button
        :icon="Refresh"
        :loading="loading"
        @click="loadAll"
      >
        刷新
      </el-button>
    </div>

    <el-tabs
      v-model="activeTab"
      class="security-tabs"
    >
      <el-tab-pane
        v-if="canViewUsers"
        label="用户"
        name="users"
      >
        <div class="table-toolbar">
          <el-input
            v-model="search"
            class="security-search"
            clearable
            placeholder="搜索用户名、姓名或邮箱"
            :prefix-icon="Search"
            @keyup.enter="runSearch"
            @clear="runSearch"
          />
          <div class="security-toolbar-actions">
            <el-button @click="runSearch">
              查询
            </el-button>
            <el-button
              v-if="canManageUsers"
              type="primary"
              :icon="Plus"
              :disabled="!canViewRoles"
              @click="openCreateUser"
            >
              新建用户
            </el-button>
          </div>
        </div>

        <el-table
          v-loading="loading"
          class="flowhearth-data-table security-data-table"
          :data="users"
          border
          stripe
        >
          <el-table-column
            prop="displayName"
            label="姓名"
            min-width="130"
          />
          <el-table-column
            prop="username"
            label="用户名"
            min-width="130"
          />
          <el-table-column
            prop="email"
            label="邮箱"
            min-width="190"
          >
            <template #default="scope">
              {{ scope.row.email || '—' }}
            </template>
          </el-table-column>
          <el-table-column
            label="角色"
            min-width="180"
          >
            <template #default="scope">
              <el-tag
                v-for="role in scope.row.roles"
                :key="role.id"
                class="inline-tag"
                effect="plain"
              >
                {{ role.name }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column
            label="状态"
            width="92"
          >
            <template #default="scope">
              <el-tag :type="scope.row.isActive ? 'success' : 'info'">
                {{ scope.row.isActive ? '启用' : '停用' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column
            label="最后登录"
            width="160"
          >
            <template #default="scope">
              {{ formatDate(scope.row.lastLoginAtUtc) }}
            </template>
          </el-table-column>
          <el-table-column
            v-if="canManageUsers"
            label="操作"
            fixed="right"
            width="150"
          >
            <template #default="scope">
              <el-button
                link
                type="primary"
                @click="openEditUser(scope.row)"
              >
                编辑
              </el-button>
              <el-button
                link
                @click="resetPassword(scope.row)"
              >
                重置密码
              </el-button>
            </template>
          </el-table-column>
        </el-table>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="table-pagination"
          layout="total, sizes, prev, pager, next"
          :page-sizes="[10, 20, 50, 100]"
          :total="total"
          @change="loadUsers"
        />
      </el-tab-pane>

      <el-tab-pane
        v-if="canViewRoles"
        label="角色与权限"
        name="roles"
      >
        <div class="table-toolbar table-toolbar-end">
          <el-button
            v-if="canManageRoles"
            type="primary"
            :icon="Plus"
            @click="openCreateRole"
          >
            新建角色
          </el-button>
        </div>
        <el-table
          v-loading="loading"
          class="flowhearth-data-table security-data-table"
          :data="roles"
          border
          stripe
        >
          <el-table-column
            prop="name"
            label="角色"
            min-width="130"
          />
          <el-table-column
            prop="code"
            label="代码"
            min-width="150"
          />
          <el-table-column
            prop="description"
            label="说明"
            min-width="180"
          />
          <el-table-column
            label="权限"
            min-width="320"
          >
            <template #default="scope">
              <el-tag
                v-for="permission in scope.row.permissions.slice(0, 5)"
                :key="permission.id"
                class="inline-tag"
                effect="plain"
              >
                {{ permission.name }}
              </el-tag>
              <span
                v-if="scope.row.permissions.length > 5"
                class="more-count"
              >+{{ scope.row.permissions.length - 5 }}</span>
            </template>
          </el-table-column>
          <el-table-column
            label="类型"
            width="100"
          >
            <template #default="scope">
              {{ scope.row.isSystem ? '系统角色' : '自定义' }}
            </template>
          </el-table-column>
          <el-table-column
            v-if="canManageRoles"
            label="操作"
            fixed="right"
            width="90"
          >
            <template #default="scope">
              <el-button
                link
                type="primary"
                :disabled="scope.row.code === 'administrator'"
                @click="openEditRole(scope.row)"
              >
                编辑
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <el-dialog
      v-model="userDialogVisible"
      draggable
      :title="userForm.id === null ? '新建用户' : '编辑用户'"
      width="min(560px, 94vw)"
    >
      <el-form label-position="top">
        <div class="two-column-form">
          <el-form-item label="用户名">
            <el-input
              v-model="userForm.username"
              :disabled="userForm.id !== null"
            />
          </el-form-item>
          <el-form-item label="显示名称">
            <el-input v-model="userForm.displayName" />
          </el-form-item>
        </div>
        <el-form-item label="邮箱">
          <el-input
            v-model="userForm.email"
            type="email"
          />
        </el-form-item>
        <el-form-item
          v-if="userForm.id === null"
          label="初始密码"
        >
          <el-input
            v-model="userForm.password"
            type="password"
            show-password
            autocomplete="new-password"
          />
        </el-form-item>
        <el-form-item label="角色">
          <el-select
            v-model="userForm.roleIds"
            multiple
            filterable
            placeholder="选择角色"
          >
            <el-option
              v-for="role in roles.filter((item) => item.isActive)"
              :key="role.id"
              :label="role.name"
              :value="role.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          v-if="userForm.id !== null"
          label="账号状态"
        >
          <el-switch
            v-model="userForm.isActive"
            active-text="启用"
            inactive-text="停用"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="userDialogVisible = false">
          取消
        </el-button>
        <el-button
          type="primary"
          :loading="saving"
          @click="saveUser"
        >
          保存
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="roleDialogVisible"
      draggable
      :title="roleForm.id === null ? '新建角色' : '编辑角色'"
      width="min(720px, 94vw)"
    >
      <el-form label-position="top">
        <div class="two-column-form">
          <el-form-item label="角色代码">
            <el-input
              v-model="roleForm.code"
              :disabled="roleForm.id !== null"
              placeholder="例如 sales-lead"
            />
          </el-form-item>
          <el-form-item label="角色名称">
            <el-input v-model="roleForm.name" />
          </el-form-item>
        </div>
        <el-form-item label="说明">
          <el-input
            v-model="roleForm.description"
            type="textarea"
            :rows="2"
          />
        </el-form-item>
        <el-form-item label="权限">
          <div class="permission-groups">
            <section
              v-for="group in groupedPermissions"
              :key="group.module"
              class="permission-group"
            >
              <strong>{{ group.module }}</strong>
              <el-checkbox-group v-model="roleForm.permissionIds">
                <el-checkbox
                  v-for="permission in group.items"
                  :key="permission.id"
                  :value="permission.id"
                >
                  {{ permission.name }}
                </el-checkbox>
              </el-checkbox-group>
            </section>
          </div>
        </el-form-item>
        <el-form-item
          v-if="roleForm.id !== null"
          label="角色状态"
        >
          <el-switch
            v-model="roleForm.isActive"
            active-text="启用"
            inactive-text="停用"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="roleDialogVisible = false">
          取消
        </el-button>
        <el-button
          type="primary"
          :loading="saving"
          @click="saveRole"
        >
          保存
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>
