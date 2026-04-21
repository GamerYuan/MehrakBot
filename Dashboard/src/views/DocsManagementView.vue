<script setup>
import { ref, computed, onMounted } from "vue";
import { useRouter } from "vue-router";
import { useConfirm } from "primevue/useconfirm";
import { useToast } from "primevue/usetoast";
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import Button from "primevue/button";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import Tag from "primevue/tag";
import DocFormModal from "../components/docs/DocFormModal.vue";
import GameTag from "../components/docs/GameTag.vue";

const router = useRouter();
const confirm = useConfirm();
const toast = useToast();

const props = defineProps({
  userInfo: {
    type: Object,
    required: true,
  },
});

const documents = ref([]);
const loading = ref(false);
const error = ref("");
const searchQuery = ref("");
const filterGame = ref("All");

const showModal = ref(false);
const selectedDoc = ref(null);
const isEditing = ref(false);

const gameOptions = [
  { label: "All Games", value: "All" },
  { label: "Genshin Impact", value: "Genshin" },
  { label: "Honkai: Star Rail", value: "HonkaiStarRail" },
  { label: "Zenless Zone Zero", value: "ZenlessZoneZero" },
  { label: "Honkai Impact 3rd", value: "HonkaiImpact3" },
  { label: "Miscellaneous", value: "Unsupported" },
];

const gameLabels = {
  Genshin: "Genshin Impact",
  HonkaiStarRail: "Honkai: Star Rail",
  ZenlessZoneZero: "Zenless Zone Zero",
  HonkaiImpact3: "Honkai Impact 3rd",
  Unsupported: "Miscellaneous",
};

const showErrorToast = (message, status) => {
  toast.add({
    severity: "error",
    summary: "Error",
    detail: `${message} (Code: ${status ?? "N/A"})`,
    life: 5000,
  });
};

const buildError = (message, status) => {
  const err = new Error(message);
  err.status = status;
  return err;
};

const hasGameWriteAccess = (game) => {
  if (props.userInfo.isSuperAdmin) return true;
  const normalized = game.toLowerCase();
  return props.userInfo.gameWritePermissions?.includes(normalized);
};

const fetchDocuments = async () => {
  loading.value = true;
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/docs/list`, {
      credentials: "include",
    });
    if (response.status === 401) {
      router.push("/login");
      return;
    }
    if (!response.ok) {
      throw buildError("Failed to fetch documentation", response.status);
    }
    documents.value = await response.json();
  } catch (err) {
    error.value = err.message;
    showErrorToast(err.message, err.status);
  } finally {
    loading.value = false;
  }
};

const filteredDocuments = computed(() => {
  return documents.value.filter((doc) => {
    const matchesSearch = doc.name
      .toLowerCase()
      .includes(searchQuery.value.toLowerCase());

    if (filterGame.value === "All") return matchesSearch;
    return matchesSearch && doc.game === filterGame.value;
  });
});

const openAddModal = () => {
  selectedDoc.value = null;
  isEditing.value = false;
  showModal.value = true;
};

const openEditModal = async (doc) => {
  if (!hasGameWriteAccess(doc.game)) {
    toast.add({
      severity: "warn",
      summary: "Permission Denied",
      detail: "You do not have permission to edit this documentation.",
      life: 4000,
    });
    return;
  }

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/docs/${doc.id}`, {
      credentials: "include",
    });
    if (!response.ok) {
      throw buildError(
        "Failed to fetch documentation details",
        response.status,
      );
    }
    selectedDoc.value = await response.json();
    isEditing.value = true;
    showModal.value = true;
  } catch (err) {
    showErrorToast(err.message, err.status);
  }
};

const confirmDelete = (doc) => {
  if (!hasGameWriteAccess(doc.game)) {
    toast.add({
      severity: "warn",
      summary: "Permission Denied",
      detail: "You do not have permission to delete this documentation.",
      life: 4000,
    });
    return;
  }

  confirm.require({
    message: `Are you sure you want to delete "${doc.name}"?`,
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
    accept: () => handleDelete(doc),
  });
};

const handleDelete = async (doc) => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/docs/${doc.id}`, {
      method: "DELETE",
      credentials: "include",
    });

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    if (!response.ok) {
      const data = await response.json();
      throw buildError(
        data.error || "Failed to delete documentation",
        response.status,
      );
    }

    fetchDocuments();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: "Documentation deleted successfully",
      life: 3000,
    });
  } catch (err) {
    showErrorToast(err.message, err.status);
  }
};

const handleSave = async (formData) => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const url = isEditing.value
      ? `${backendUrl}/docs/${selectedDoc.value.id}`
      : `${backendUrl}/docs/add`;
    const method = isEditing.value ? "PUT" : "POST";

    const response = await fetch(url, {
      method,
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify(formData),
    });

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    if (!response.ok) {
      const data = await response.json();
      throw buildError(
        data.error || "Failed to save documentation",
        response.status,
      );
    }

    showModal.value = false;
    fetchDocuments();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: `Documentation ${isEditing.value ? "updated" : "created"} successfully`,
      life: 3000,
    });
  } catch (err) {
    showErrorToast(err.message, err.status);
  }
};

const formatDate = (dateStr) => {
  if (!dateStr) return "";
  return new Date(dateStr).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
};

onMounted(() => {
  fetchDocuments();
});
</script>

<template>
  <div class="docs-management">
    <div class="header">
      <h1>Documentation Management</h1>
      <Button
        label="Add Documentation"
        icon="pi pi-plus"
        @click="openAddModal"
      />
    </div>

    <div class="controls flex gap-4 mb-4">
      <InputText
        v-model="searchQuery"
        placeholder="Search by name..."
        class="flex-1"
        fluid
      />
      <Select
        v-model="filterGame"
        :options="gameOptions"
        optionLabel="label"
        optionValue="value"
        placeholder="Filter by game"
        class="w-64 items-center"
      />
    </div>

    <DataTable
      :value="filteredDocuments"
      :loading="loading"
      tableStyle="min-width: 50rem"
    >
      <Column field="name" header="Name">
        <template #body="slotProps">
          <span class="font-semibold">{{ slotProps.data.name }}</span>
        </template>
      </Column>
      <Column field="description" header="Description">
        <template #body="slotProps">
          <span class="text-gray-400">{{ slotProps.data.description }}</span>
        </template>
      </Column>
      <Column field="game" header="Game">
        <template #body="slotProps">
          <GameTag :game="slotProps.data.game" size="small" />
        </template>
      </Column>
      <Column field="updatedAt" header="Last Updated">
        <template #body="slotProps">
          <span class="text-gray-400">{{
            formatDate(slotProps.data.updatedAt)
          }}</span>
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
              @click="openEditModal(slotProps.data)"
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

    <DocFormModal
      v-model:visible="showModal"
      :doc="selectedDoc"
      :isEditing="isEditing"
      :userInfo="userInfo"
      @save="handleSave"
    />
  </div>
</template>

<style scoped>
.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.text-gray-400 {
  color: #888;
}
</style>
