<script setup>
import { computed } from "vue";
import Button from "primevue/button";

const props = defineProps({
  userInfo: {
    type: Object,
    required: true,
  },
});

const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;

const filerUrl = computed(() => {
  if (!backendUrl) return "";
  return `${backendUrl}/admin/seaweed-filer/`;
});

const openFiler = () => {
  if (!filerUrl.value) return;
  window.open(filerUrl.value, "_blank", "noopener,noreferrer");
};
</script>

<template>
  <div class="seaweed-page">
    <header class="seaweed-header">
      <h1>Seaweed Filer</h1>
      <p class="seaweed-subtitle">
        Open the filer UI in a new tab through the authenticated dashboard
        proxy.
      </p>
    </header>

    <div v-if="!userInfo.isSuperAdmin" class="seaweed-warning">
      This page is only available to superadmin users.
    </div>

    <div v-else class="seaweed-card">
      <p>
        You are signed in as <strong>{{ userInfo.username }}</strong> with
        superadmin access.
      </p>
      <Button @click="openFiler" :disabled="!filerUrl">
        Open Seaweed Filer UI
      </Button>
      <p v-if="!filerUrl" class="seaweed-error">
        Backend URL is not configured. Set <code>VITE_APP_BACKEND_URL</code>.
      </p>
    </div>
  </div>
</template>

<style scoped>
.seaweed-page {
  max-width: 900px;
  margin: 0 auto;
  text-align: left;
}

.seaweed-header {
  margin-bottom: 1.5rem;
}

.seaweed-header h1 {
  margin: 0;
}

.seaweed-subtitle {
  color: #b7b7b7;
  margin-top: 0.5rem;
}

.seaweed-card {
  background: #2b2b2b;
  border: 1px solid #3d3d3d;
  border-radius: 12px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
}

.seaweed-warning {
  border: 1px solid #8f7a00;
  background: rgba(143, 122, 0, 0.15);
  color: #ffd65a;
  border-radius: 10px;
  padding: 1rem;
}

.seaweed-error {
  color: #ff7d7d;
}
</style>
