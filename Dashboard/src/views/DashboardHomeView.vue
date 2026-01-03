<script setup>
import { useRouter } from "vue-router";

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

    <div class="user-info-card">
      <div class="card-header">
        <h2>User Profile</h2>
        <button
          @click="router.push('/dashboard/change-password')"
          class="btn small"
        >
          Change Password
        </button>
      </div>
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
        <span
          class="value"
          :class="{ yes: userInfo.isSuperAdmin, no: !userInfo.isSuperAdmin }"
        >
          {{ userInfo.isSuperAdmin ? "Yes" : "No" }}
        </span>
      </div>
    </div>

    <div class="permissions-card">
      <h2>Game Permissions</h2>
      <ul
        v-if="
          userInfo.gameWritePermissions &&
          userInfo.gameWritePermissions.length > 0
        "
        class="permissions-list"
      >
        <li
          v-for="perm in userInfo.gameWritePermissions"
          :key="perm"
          class="permission-item"
        >
          {{ toTitleCase(perm) }}
        </li>
      </ul>
      <p v-else class="no-perms">
        No specific game write permissions assigned.
      </p>
    </div>
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

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid #444;
  padding-bottom: 1rem;
  margin-bottom: 1.5rem;
}

.card-header h2 {
  margin: 0;
  border: none;
  padding: 0;
}

.user-info-card,
.permissions-card {
  background-color: var(--card-bg);
  padding: 2rem;
  border-radius: 12px;
  border: 1px solid #333;
  margin-bottom: 2rem;
}

h2 {
  color: var(--secondary-color);
  margin-top: 0;
  border-bottom: 1px solid #444;
  padding-bottom: 1rem;
  margin-bottom: 1.5rem;
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

.value.yes {
  color: #4caf50;
}
.value.no {
  color: #aaa;
}

.permissions-list {
  list-style: none;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
}

.permission-item {
  background-color: #1a1a1a;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  border: 1px solid var(--primary-color);
  color: white;
}

.no-perms {
  color: #888;
  font-style: italic;
}

.btn {
  padding: 0.6rem 1.2rem;
  border-radius: 8px;
  font-weight: bold;
  cursor: pointer;
  border: none;
  font-size: 0.9rem;
  transition: background-color 0.2s;
}

.btn.secondary {
  background-color: #333;
  color: white;
  border: 1px solid #555;
}

.btn.small {
  padding: 0.4rem 0.8rem;
  font-size: 0.8rem;
  background-color: transparent;
  border: 1px solid var(--primary-color);
  color: var(--primary-color);
}

.btn.small:hover {
  background-color: var(--primary-color);
  color: white;
}
</style>
