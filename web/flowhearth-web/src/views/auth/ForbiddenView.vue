<template>
  <main class="standalone-message">
    <div class="message-code">
      403
    </div>
    <h1>当前账号无权访问此模块</h1>
    <p>权限由后台角色策略控制。如需访问，请联系系统管理员调整角色。</p>
    <RouterLink to="/dashboard">
      <el-button type="primary">
        返回工作台
      </el-button>
    </RouterLink>
  </main>
</template>
