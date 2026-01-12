<script setup>
import { ref, onMounted } from "vue";
import { useRouter } from "vue-router";
import Sidebar from "../components/Sidebar.vue";

const router = useRouter();
const userInfo = ref(null);
const loading = ref(true);

onMounted(() => {
  const storedUser = localStorage.getItem("mehrak_user");
  if (!storedUser) {
    router.push("/login");
    return;
  }
  userInfo.value = JSON.parse(storedUser);
  loading.value = false;
});
</script>

<template>
  <div class="dashboard-layout" v-if="!loading && userInfo">
    <Sidebar :userInfo="userInfo" />
    <main class="dashboard-content">
      <router-view :userInfo="userInfo" />
    </main>
  </div>
</template>

<style scoped>
.dashboard-layout {
  display: flex;
  min-height: 100vh;
}

.dashboard-content {
  flex: 1;
  margin-left: 250px; /* Width of sidebar */
  padding: 2rem;
  background-color: var(--bg-color);
  overflow-y: auto;
}
</style>
