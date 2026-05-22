<script setup>
import { ref, computed, onMounted } from "vue";
import { useConfirm } from "primevue/useconfirm";
import { useApi } from "../composables/useApi";
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import Button from "primevue/button";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import GameTag from "../components/docs/GameTag.vue";
import DocFormModal from "../components/docs/DocFormModal.vue";
import { gameFilterOptions, gameLabels } from "../configs/gameMeta";

const confirm = useConfirm();
const { apiFetch, apiFetchJson, showErrorToast } = useApi();

const props = defineProps({
  userInfo: {
    type: Object,
    required: true,
  },
});

const documents = ref([]);
const loading = ref(false);
const searchQuery = ref("");
const filterGame = ref("All");

const showModal = ref(false);
const selectedDoc = ref(null);
const isEditing = ref(false);

const hasGameWriteAccess = (game) => {
  if (props.userInfo.isSuperAdmin) return true;
  const normalized = game.toLowerCase();
  return props.userInfo.gameWritePermissions?.includes(normalized);
};

const fetchDocuments = async () => {
  loading.value = true;
  try {
    const { ok, data, status } = await apiFetchJson("/docs/list");
    if (ok) {
      documents.value = data;
    } else {
      showErrorToast(data.error || "Failed to fetch documentation", status);
    }
  } catch (err) {
    if (err._redirected) return;
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
    showErrorToast("You do not have permission to edit this documentation.", "Permission Denied");
    return;
  }

  try {
    const response = await apiFetch(`/docs/${doc.id}`);
    if (!response.ok) {
      throw new Error("Failed to fetch documentation details");
    }
    selectedDoc.value = await response.json();
    isEditing.value = true;
    showModal.value = true;
  } catch (err) {
    if (err._redirected) return;
    showErrorToast(err.message, err.status);
  }
};

const confirmDelete = (doc) => {
  if (!hasGameWriteAccess(doc.game)) {
    showErrorToast("You do not have permission to delete this documentation.", "Permission Denied");
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
    const response = await apiFetch(`/docs/${doc.id}`, {
      method: "DELETE",
    });

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to delete documentation");
    }

    fetchDocuments();
    showErrorToast("Documentation deleted successfully", undefined);
  } catch (err) {
    if (err._redirected) return;
    showErrorToast(err.message, err.status);
  }
};

const handleSave = async (formData) => {
  try {
    const url = isEditing.value
      ? `/docs/${selectedDoc.value.id}`
      : "/docs/add";
    const method = isEditing.value ? "PUT" : "POST";

    const response = await apiFetch(url, {
      method,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(formData),
    });

    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to save documentation");
    }

    showModal.value = false;
    fetchDocuments();
  } catch (err) {
    if (err._redirected) return;
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
        :options="gameFilterOptions"
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
</style>