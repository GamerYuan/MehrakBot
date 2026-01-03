<script setup>
import { ref, computed, onMounted } from "vue";
import { useRouter } from "vue-router";
import { useConfirm } from "primevue/useconfirm";
import { useToast } from "primevue/usetoast";
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import Button from "primevue/button";
import InputText from "primevue/inputtext";
import Dialog from "primevue/dialog";
import Checkbox from "primevue/checkbox";
import Tag from "primevue/tag";
import Select from "primevue/select";

const router = useRouter();
const confirm = useConfirm();
const toast = useToast();

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
    if (response.status === 401) {
      router.push("/login");
      return;
    }
    if (!response.ok) throw new Error("Failed to fetch users");
    users.value = await response.json();
  } catch (err) {
    error.value = err.message;
    toast.add({
      severity: "error",
      summary: "Error",
      detail: err.message,
      life: 5000,
    });
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

const confirmDelete = (user) => {
  confirm.require({
    message: `Are you sure you want to delete user ${user.username}?`,
    header: "Confirm Delete",
    icon: "pi pi-exclamation-triangle",
    rejectProps: {
      label: "Cancel",
      severity: "secondary",
      outlined: true,
    },
    acceptProps: {
      label: "Delete",
      severity: "danger",
    },
    accept: () => handleDeleteUser(user),
  });
};

const confirmReset = (user) => {
  confirm.require({
    message: `Force password reset for ${user.username}?`,
    header: "Confirm Password Reset",
    icon: "pi pi-exclamation-triangle",
    rejectProps: {
      label: "Cancel",
      severity: "secondary",
      outlined: true,
    },
    acceptProps: {
      label: "Confirm",
      severity: "primary",
    },
    accept: () => handleResetPassword(user),
  });
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

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to add user");
    }

    const result = await response.json();
    tempPassword.value = result.temporaryPassword;
    showAddModal.value = false;
    showTempPasswordModal.value = true;
    fetchUsers();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: "User added successfully",
      life: 3000,
    });
  } catch (err) {
    toast.add({
      severity: "error",
      summary: "Error",
      detail: err.message,
      life: 5000,
    });
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

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to update user");
    }

    showUpdateModal.value = false;
    fetchUsers();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: "User updated successfully",
      life: 3000,
    });
  } catch (err) {
    toast.add({
      severity: "error",
      summary: "Error",
      detail: err.message,
      life: 5000,
    });
  }
};

const handleDeleteUser = async (user) => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/users/${user.userId}`, {
      method: "DELETE",
      credentials: "include",
    });

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to delete user");
    }

    fetchUsers();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: "User deleted successfully",
      life: 3000,
    });
  } catch (err) {
    toast.add({
      severity: "error",
      summary: "Error",
      detail: err.message,
      life: 5000,
    });
  }
};

const handleResetPassword = async (user) => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/users/${user.userId}/password/require-reset`,
      {
        method: "POST",
        credentials: "include",
      }
    );

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to reset password");
    }

    toast.add({
      severity: "success",
      summary: "Success",
      detail: "Password reset required for user.",
      life: 5000,
    });
  } catch (err) {
    toast.add({
      severity: "error",
      summary: "Error",
      detail: err.message,
      life: 5000,
    });
  }
};

onMounted(() => {
  fetchUsers();
});

const permissionOptions = computed(() => {
  return [
    { label: "All Permissions", value: "All" },
    { label: "Super Admin", value: "SuperAdmin" },
    ...availablePermissions.map((perm) => ({
      label: formatPermission(perm),
      value: perm,
    })),
  ];
});
</script>

<template>
  <div class="user-management">
    <div class="header">
      <h1>User Management</h1>
      <Button label="Add User" icon="pi pi-plus" @click="openAddModal" />
    </div>

    <div class="controls flex gap-4 mb-4">
      <InputText
        v-model="searchQuery"
        placeholder="Search by username..."
        class="flex-1"
        fluid
      />

      <Select
        v-model="filterPermission"
        :options="permissionOptions"
        optionLabel="label"
        optionValue="value"
        placeholder="Filter Permissions"
        class="w-1/3 items-center"
      />
    </div>

    <DataTable
      :value="filteredUsers"
      :loading="loading"
      tableStyle="min-width: 50rem"
    >
      <Column field="username" header="Username"></Column>
      <Column field="discordUserId" header="Discord ID"></Column>
      <Column header="Role">
        <template #body="slotProps">
          <Tag
            :severity="slotProps.data.isSuperAdmin ? 'success' : 'secondary'"
            :value="slotProps.data.isSuperAdmin ? 'Super Admin' : 'User'"
          />
        </template>
      </Column>
      <Column header="Permissions">
        <template #body="slotProps">
          <div class="flex flex-wrap gap-2">
            <Tag
              v-for="perm in slotProps.data.gameWritePermissions"
              :key="perm"
              :value="formatPermission(perm)"
              severity="info"
            />
          </div>
        </template>
      </Column>
      <Column header="Actions">
        <template #body="slotProps">
          <div class="flex gap-2">
            <Button
              icon="pi pi-pencil"
              severity="secondary"
              text
              rounded
              aria-label="Edit"
              @click="openUpdateModal(slotProps.data)"
            />
            <Button
              icon="pi pi-lock"
              severity="warn"
              text
              rounded
              aria-label="Reset Password"
              @click="confirmReset(slotProps.data)"
            />
            <Button
              icon="pi pi-trash"
              severity="danger"
              text
              rounded
              aria-label="Delete"
              @click="confirmDelete(slotProps.data)"
            />
          </div>
        </template>
      </Column>
    </DataTable>

    <!-- Add/Update Modal -->
    <Dialog
      v-model:visible="showAddModal"
      modal
      header="Add New User"
      :style="{ width: '25rem' }"
    >
      <form @submit.prevent="handleAddUser">
        <div class="flex flex-col gap-4 mb-4">
          <div class="flex flex-col gap-2">
            <label for="username" class="font-semibold w-24">Username</label>
            <InputText
              id="username"
              v-model="formData.username"
              class="flex-auto"
              autocomplete="off"
              required
            />
          </div>
          <div class="flex flex-col gap-2">
            <label for="discordId" class="font-semibold w-24">Discord ID</label>
            <InputText
              id="discordId"
              v-model="formData.discordUserId"
              class="flex-auto"
              autocomplete="off"
              required
              pattern="\d+"
              title="Numeric ID"
            />
          </div>
          <div class="flex items-center gap-2">
            <Checkbox
              v-model="formData.isSuperAdmin"
              binary
              inputId="isSuperAdmin"
            />
            <label for="isSuperAdmin" class="font-semibold">Super Admin</label>
          </div>
          <div class="flex flex-col gap-2">
            <label class="font-semibold">Game Write Permissions</label>
            <div class="flex flex-col gap-2">
              <div
                v-for="perm in availablePermissions"
                :key="perm"
                class="flex items-center gap-2"
              >
                <Checkbox
                  v-model="formData.permissions[perm]"
                  binary
                  :inputId="perm"
                />
                <label :for="perm">{{ formatPermission(perm) }}</label>
              </div>
            </div>
          </div>
        </div>
        <div class="flex justify-end gap-2">
          <Button
            type="button"
            label="Cancel"
            severity="secondary"
            @click="showAddModal = false"
          ></Button>
          <Button type="submit" label="Save"></Button>
        </div>
      </form>
    </Dialog>

    <Dialog
      v-model:visible="showUpdateModal"
      modal
      header="Update User"
      :style="{ width: '25rem' }"
    >
      <form @submit.prevent="handleUpdateUser">
        <div class="flex flex-col gap-4 mb-4">
          <div class="flex flex-col gap-2">
            <label for="edit-username" class="font-semibold w-24"
              >Username</label
            >
            <InputText
              id="edit-username"
              v-model="formData.username"
              class="flex-auto"
              autocomplete="off"
              required
            />
          </div>
          <div class="flex flex-col gap-2">
            <label for="edit-discordId" class="font-semibold w-24"
              >Discord ID</label
            >
            <InputText
              id="edit-discordId"
              v-model="formData.discordUserId"
              class="flex-auto"
              autocomplete="off"
              required
              pattern="\d+"
              title="Numeric ID"
            />
          </div>
          <div class="flex items-center gap-2">
            <Checkbox
              v-model="formData.isSuperAdmin"
              binary
              inputId="edit-isSuperAdmin"
            />
            <label for="edit-isSuperAdmin" class="font-semibold"
              >Super Admin</label
            >
          </div>
          <div class="flex flex-col gap-2">
            <label class="font-semibold">Game Write Permissions</label>
            <div class="flex flex-col gap-2">
              <div
                v-for="perm in availablePermissions"
                :key="perm"
                class="flex items-center gap-2"
              >
                <Checkbox
                  v-model="formData.permissions[perm]"
                  binary
                  :inputId="'edit-' + perm"
                />
                <label :for="'edit-' + perm">{{
                  formatPermission(perm)
                }}</label>
              </div>
            </div>
          </div>
        </div>
        <div class="flex justify-end gap-2">
          <Button
            type="button"
            label="Cancel"
            severity="secondary"
            @click="showUpdateModal = false"
          ></Button>
          <Button type="submit" label="Save"></Button>
        </div>
      </form>
    </Dialog>

    <Dialog
      v-model:visible="showTempPasswordModal"
      modal
      header="User Created"
      :style="{ width: '25rem' }"
    >
      <p class="mb-4">Temporary Password:</p>
      <div class="code-block select-all">
        {{ tempPassword }}
      </div>
      <p class="text-orange-500 mb-4">
        Please copy this password. It will not be shown again.
      </p>
      <div class="flex justify-end">
        <Button label="Close" @click="showTempPasswordModal = false"></Button>
      </div>
    </Dialog>
  </div>
</template>

<style scoped>
.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.text-orange-500 {
  color: var(--p-orange-500);
}
</style>
