<script setup lang="ts">
defineProps<{
  title: string
  description: string
}>()
</script>

<template>
  <section class="placeholder-page">
    <div class="placeholder-index">
      FLOWHEARTH
    </div>
    <p class="section-kicker">
      MODULE ROADMAP
    </p>
    <h2>{{ title }}</h2>
    <p>{{ description }}</p>
    <el-tag
      effect="plain"
      round
    >
      按开发计划实施中
    </el-tag>
  </section>
</template>
