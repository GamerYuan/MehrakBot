<script setup>
import { ref, computed, onMounted } from "vue";

const props = defineProps({
  userInfo: {
    type: Object,
    required: true,
  },
});

const users = ref([]);
const loading = ref(false);
const error = ref("");
const searchQuery = ref("");
const filterPermission = ref("All");

// Modal states
const showAddModal = ref(false);
const showUpdateModal = ref(false);
const showDeleteConfirm = ref(false);
const showResetConfirm = ref(false);
const showTempPasswordModal = ref(false);

const selectedUser = ref(null);
const tempPassword = ref("");

// Form data
const formData = ref({
  username: "",
  discordUserId: "",
  isSuperAdmin: false,
  isActive: true,
  permissions: {
    genshin: false,
    honkaistarrail: false,
    honkaiimpact3: false,
    zenlesszonezero: false,
  },
});

const availablePermissions = [
  "genshin",
  "honkaistarrail",
  "honkaiimpact3",
  "zenlesszonezero",
];

const permissionLabels = {
  genshin: "Genshin Impact",
  honkaistarrail: "Honkai: Star Rail",
  honkaiimpact3: "Honkai Impact 3rd",
  zenlesszonezero: "Zenless Zone Zero",
};

const formatPermission = (str) => {
  if (!str) return "";
  return permissionLabels[str] || str;
};

const fetchUsers = async () => {
  loading.value = true;
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/users/list`, {
      credentials: "include",
    });
    if (!response.ok) throw new Error("Failed to fetch users");
    users.value = await response.json();
  } catch (err) {
    error.value = err.message;
  } finally {
    loading.value = false;
  }
};

const filteredUsers = computed(() => {
  return users.value.filter((user) => {
    const matchesSearch = user.username
      .toLowerCase()
      .includes(searchQuery.value.toLowerCase());

    if (filterPermission.value === "All") return matchesSearch;
    if (filterPermission.value === "SuperAdmin")
      return matchesSearch && user.isSuperAdmin;

    return (
      matchesSearch &&
      user.gameWritePermissions &&
      user.gameWritePermissions.includes(filterPermission.value)
    );
  });
});

const resetForm = () => {
  formData.value = {
    username: "",
    discordUserId: "",
    isSuperAdmin: false,
    isActive: true,
    permissions: {
      genshin: false,
      honkaistarrail: false,
      honkaiimpact3: false,
      zenlesszonezero: false,
    },
  };
};

const openAddModal = () => {
  resetForm();
  showAddModal.value = true;
};

const openUpdateModal = (user) => {
  selectedUser.value = user;
  const userPerms = (user.gameWritePermissions || []).map((p) =>
    p.toLowerCase()
  );

  const newPermissions = {};
  availablePermissions.forEach((perm) => {
    newPermissions[perm] = userPerms.includes(perm);
  });

  formData.value = {
    username: user.username,
    discordUserId: user.discordUserId || "",
    isSuperAdmin: user.isSuperAdmin,
    isActive: true,
    permissions: newPermissions,
  };
  showUpdateModal.value = true;
};

const openDeleteConfirm = (user) => {
  selectedUser.value = user;
  showDeleteConfirm.value = true;
};

const openResetConfirm = (user) => {
  selectedUser.value = user;
  showResetConfirm.value = true;
};

const getSelectedPermissions = () => {
  return Object.entries(formData.value.permissions)
    .filter(([_, value]) => value)
    .map(([key, _]) => key);
};

const handleAddUser = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const payload = {
      username: formData.value.username,
      discordUserId: BigInt(formData.value.discordUserId).toString(),
      isSuperAdmin: formData.value.isSuperAdmin,
      gameWritePermissions: getSelectedPermissions(),
    };

    const response = await fetch(`${backendUrl}/users/add`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to add user");
    }

    const result = await response.json();
    tempPassword.value = result.temporaryPassword;
    showAddModal.value = false;
    showTempPasswordModal.value = true;
    fetchUsers();
  } catch (err) {
    alert(err.message);
  }
};

const handleUpdateUser = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const payload = {
      username: formData.value.username,
      discordUserId: BigInt(formData.value.discordUserId).toString(),
      isSuperAdmin: formData.value.isSuperAdmin,
      isActive: formData.value.isActive,
      gameWritePermissions: getSelectedPermissions(),
    };

    const response = await fetch(
      `${backendUrl}/users/${selectedUser.value.userId}`,
      {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify(payload),
      }
    );

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to update user");
    }

    showUpdateModal.value = false;
    fetchUsers();
  } catch (err) {
    alert(err.message);
  }
};

const handleDeleteUser = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/users/${selectedUser.value.userId}`,
      {
        method: "DELETE",
        credentials: "include",
      }
    );

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to delete user");
    }

    showDeleteConfirm.value = false;
    fetchUsers();
  } catch (err) {
    alert(err.message);
  }
};

const handleResetPassword = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/users/${selectedUser.value.userId}/password/require-reset`,
      {
        method: "POST",
        credentials: "include",
      }
    );

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to reset password");
    }

    alert(
      "Password reset required for user. They will be prompted to set a new password on next login."
    );
    showResetConfirm.value = false;
  } catch (err) {
    alert(err.message);
  }
};

onMounted(() => {
  fetchUsers();
});
</script>

<template>
  <div class="user-management">
    <div class="header">
      <h1>User Management</h1>
      <button @click="openAddModal" class="btn primary">Add User</button>
    </div>

    <div class="controls">
      <input
        v-model="searchQuery"
        type="text"
        placeholder="Search by username..."
        class="search-box"
      />

      <select v-model="filterPermission" class="filter-select">
        <option value="All">All Permissions</option>
        <option value="SuperAdmin">Super Admin</option>
        <option v-for="perm in availablePermissions" :key="perm" :value="perm">
          {{ formatPermission(perm) }}
        </option>
      </select>
    </div>

    <div class="table-container">
      <table>
        <thead>
          <tr>
            <th>Username</th>
            <th>Discord ID</th>
            <th>Role</th>
            <th>Permissions</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="user in filteredUsers" :key="user.userId">
            <td>{{ user.username }}</td>
            <td>{{ user.discordUserId }}</td>
            <td>
              <span v-if="user.isSuperAdmin" class="badge admin"
                >Super Admin</span
              >
              <span v-else class="badge user">User</span>
            </td>
            <td>
              <div class="perms-tags">
                <span
                  v-for="perm in user.gameWritePermissions"
                  :key="perm"
                  class="tag"
                >
                  {{ formatPermission(perm) }}
                </span>
              </div>
            </td>
            <td class="actions-cell">
              <button
                @click="openUpdateModal(user)"
                class="btn icon-btn"
                title="Edit"
              >
                ✏️
              </button>
              <button
                @click="openResetConfirm(user)"
                class="btn icon-btn"
                title="Reset Password"
              >
                🔒
              </button>
              <button
                @click="openDeleteConfirm(user)"
                class="btn icon-btn delete"
                title="Delete"
              >
                🗑️
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Modals -->
    <div v-if="showAddModal || showUpdateModal" class="modal-overlay">
      <div class="modal">
        <h2>{{ showAddModal ? "Add New User" : "Update User" }}</h2>
        <form
          @submit.prevent="showAddModal ? handleAddUser() : handleUpdateUser()"
        >
          <div class="form-group">
            <label>Username</label>
            <input v-model="formData.username" required />
          </div>
          <div class="form-group">
            <label>Discord User ID</label>
            <input
              v-model="formData.discordUserId"
              required
              type="text"
              pattern="\d+"
              title="Numeric ID"
            />
          </div>

          <div class="form-group checkbox">
            <label class="checkbox-label">
              <input type="checkbox" v-model="formData.isSuperAdmin" />
              Super Admin
            </label>
          </div>

          <div class="form-group">
            <label>Game Write Permissions</label>
            <div class="toggle-grid">
              <div
                v-for="perm in availablePermissions"
                :key="perm"
                class="toggle-card"
                :class="{ active: formData.permissions[perm] }"
                @click="
                  formData.permissions[perm] = !formData.permissions[perm]
                "
              >
                {{ formatPermission(perm) }}
              </div>
            </div>
          </div>

          <div class="modal-actions">
            <button
              type="button"
              @click="
                showAddModal = false;
                showUpdateModal = false;
              "
              class="btn secondary"
            >
              Cancel
            </button>
            <button type="submit" class="btn primary">Save</button>
          </div>
        </form>
      </div>
    </div>

    <div v-if="showDeleteConfirm" class="modal-overlay">
      <div class="modal small">
        <h2>Confirm Delete</h2>
        <p>
          Are you sure you want to delete user
          <strong>{{ selectedUser.username }}</strong
          >?
        </p>
        <div class="modal-actions">
          <button @click="showDeleteConfirm = false" class="btn secondary">
            Cancel
          </button>
          <button @click="handleDeleteUser" class="btn delete">Delete</button>
        </div>
      </div>
    </div>

    <div v-if="showResetConfirm" class="modal-overlay">
      <div class="modal small">
        <h2>Confirm Password Reset</h2>
        <p>
          Force password reset for <strong>{{ selectedUser.username }}</strong
          >?
        </p>
        <div class="modal-actions">
          <button @click="showResetConfirm = false" class="btn secondary">
            Cancel
          </button>
          <button @click="handleResetPassword" class="btn primary">
            Confirm
          </button>
        </div>
      </div>
    </div>

    <div v-if="showTempPasswordModal" class="modal-overlay">
      <div class="modal small">
        <h2>User Created</h2>
        <p>Temporary Password:</p>
        <div class="code-block">{{ tempPassword }}</div>
        <p class="text-warning">
          Please copy this password. It will not be shown again.
        </p>
        <div class="modal-actions">
          <button @click="showTempPasswordModal = false" class="btn primary">
            Close
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.controls {
  display: flex;
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.search-box {
  flex: 1;
  max-width: 300px;
}

.filter-select {
  width: auto;
  min-width: 200px;
  padding: 0.6rem;
}

.perms-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.actions-cell {
  display: flex;
  gap: 0.5rem;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}
</style>
