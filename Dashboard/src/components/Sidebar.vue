<script setup>
import { useRouter, useRoute } from "vue-router";
import { computed } from "vue";

const router = useRouter();
const route = useRoute();

const props = defineProps({
  userInfo: {
    type: Object,
    required: true,
  },
});

const isActive = (path) => route.path === path;

const hasPermission = (perm) => {
  if (props.userInfo.isSuperAdmin) return true;
  return props.userInfo.gameWritePermissions?.includes(perm);
};

const handleLogout = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    if (backendUrl) {
      await fetch(`${backendUrl}/auth/logout`, {
        method: "POST",
        credentials: "include",
      });
    }
  } catch (e) {
    console.error("Logout error", e);
  } finally {
    localStorage.removeItem("mehrak_user");
    router.push("/login");
  }
};
</script>

<template>
  <aside class="sidebar">
    <div class="sidebar-header">
      <h2>MehrakBot</h2>
    </div>

    <nav class="sidebar-nav">
      <router-link
        to="/dashboard"
        class="nav-item"
        :class="{ active: isActive('/dashboard') }"
      >
        Profile
      </router-link>

      <router-link
        v-if="userInfo.isSuperAdmin"
        to="/dashboard/users"
        class="nav-item"
        :class="{ active: isActive('/dashboard/users') }"
      >
        User Management
      </router-link>

      <router-link
        to="/dashboard/genshin"
        class="nav-item"
        :class="{ active: isActive('/dashboard/genshin') }"
      >
        Genshin Impact
      </router-link>

      <router-link
        to="/dashboard/hsr"
        class="nav-item"
        :class="{ active: isActive('/dashboard/hsr') }"
      >
        Honkai: Star Rail
      </router-link>
    </nav>

    <div class="sidebar-footer">
      <div class="user-mini-profile">
        <span class="username">{{ userInfo.username }}</span>
      </div>
      <button @click="handleLogout" class="btn logout-btn">Logout</button>
    </div>
  </aside>
</template>

<style scoped>
.sidebar {
  width: 250px;
  background-color: #1e1e1e;
  border-right: 1px solid #333;
  display: flex;
  flex-direction: column;
  height: 100vh;
  position: fixed;
  left: 0;
  top: 0;
}

.sidebar-header {
  padding: 1.5rem;
  border-bottom: 1px solid #333;
}

.sidebar-header h2 {
  margin: 0;
  color: var(--primary-color);
  font-size: 1.5rem;
}

.sidebar-nav {
  flex: 1;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.nav-item {
  padding: 0.8rem 1rem;
  border-radius: 8px;
  color: #ccc;
  text-decoration: none;
  transition: background-color 0.2s, color 0.2s;
}

.nav-item:hover {
  background-color: #2c2c2c;
  color: white;
}

.nav-item.active {
  background-color: var(--primary-color);
  color: white;
}

.sidebar-footer {
  padding: 1.5rem;
  border-top: 1px solid #333;
}

.user-mini-profile {
  margin-bottom: 1rem;
  font-weight: bold;
  color: #fff;
}

.logout-btn {
  width: 100%;
  padding: 0.6rem;
  background-color: #333;
  color: white;
  border: 1px solid #444;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.2s;
}

.logout-btn:hover {
  background-color: #444;
}
</style>
