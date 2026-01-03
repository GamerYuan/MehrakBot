<script setup>
import { useRouter } from "vue-router";
import Button from "primevue/button";
import Card from "primevue/card";
import Tag from "primevue/tag";

const router = useRouter();

const props = defineProps({
  userInfo: {
    type: Object,
    required: true,
  },
});

const toTitleCase = (str) => {
  if (!str) return "";
  return str.replace(
    /\w\S*/g,
    (text) => text.charAt(0).toUpperCase() + text.substring(1).toLowerCase()
  );
};
</script>

<template>
  <div class="dashboard-container">
    <header class="dashboard-header">
      <h1>Dashboard</h1>
    </header>

    <Card class="mb-4">
      <template #title>
        <div class="flex justify-between items-center">
          <span>User Profile</span>
          <Button
            label="Change Password"
            size="small"
            outlined
            @click="router.push('/dashboard/change-password')"
          />
        </div>
      </template>
      <template #content>
        <div class="flex flex-col gap-2">
          <div class="info-row">
            <span class="label">Username:</span>
            <span class="value">{{ userInfo.username }}</span>
          </div>
          <div class="info-row">
            <span class="label">Discord User ID:</span>
            <span class="value">{{ userInfo.discordUserId }}</span>
          </div>
          <div class="info-row">
            <span class="label">Super Admin:</span>
            <Tag
              :severity="userInfo.isSuperAdmin ? 'success' : 'secondary'"
              :value="userInfo.isSuperAdmin ? 'Yes' : 'No'"
            />
          </div>
        </div>
      </template>
    </Card>

    <Card>
      <template #title>Game Permissions</template>
      <template #content>
        <div
          v-if="
            userInfo.gameWritePermissions &&
            userInfo.gameWritePermissions.length > 0
          "
          class="flex flex-wrap gap-2"
        >
          <Tag
            v-for="perm in userInfo.gameWritePermissions"
            :key="perm"
            :value="toTitleCase(perm)"
            severity="info"
          />
        </div>
        <p v-else class="no-perms">
          No specific game write permissions assigned.
        </p>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.dashboard-container {
  max-width: 800px;
  margin: 0 auto;
  padding: 2rem;
  text-align: left;
}

.dashboard-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.info-row {
  display: flex;
  justify-content: space-between;
  padding: 0.8rem 0;
  border-bottom: 1px solid #333;
}

.info-row:last-child {
  border-bottom: none;
}

.label {
  color: #aaa;
  font-weight: bold;
}

.value {
  font-family: monospace;
  font-size: 1.1rem;
}

.no-perms {
  color: #888;
  font-style: italic;
}
</style>
