<script setup>
import { ref, watch, onMounted, computed } from "vue";
import { useRouter } from "vue-router";
import { useConfirm } from "primevue/useconfirm";
import { useToast } from "primevue/usetoast";
import Tabs from "primevue/tabs";
import TabList from "primevue/tablist";
import Tab from "primevue/tab";
import TabPanels from "primevue/tabpanels";
import TabPanel from "primevue/tabpanel";
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import Tag from "primevue/tag";
import InputText from "primevue/inputtext";
import InputNumber from "primevue/inputnumber";
import Select from "primevue/select";
import AutoComplete from "primevue/autocomplete";
import Button from "primevue/button";
import Card from "primevue/card";
import Dialog from "primevue/dialog";
import Password from "primevue/password";
import Message from "primevue/message";
import Image from "primevue/image";

const router = useRouter();
const confirm = useConfirm();
const toast = useToast();
const activeTab = ref("character");
const loading = ref(false);
const error = ref("");
const resultImages = ref({});
const showAuthModal = ref(false);

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

// Common Data
const profileId = ref(1);
const server = ref("America");

// Specific Data
const characterName = ref("");

const allCharacters = ref([]);
const filteredCharacters = ref([]);
const aliases = ref([]);

const fetchCharacters = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/characters/list?game=HonkaiStarRail`,
      {
        credentials: "include",
      }
    );
    if (response.status === 401) {
      router.push("/login");
      return;
    }
    if (response.ok) {
      const data = await response.json();
      allCharacters.value = data.sort();
    } else {
      const data = await response.json().catch(() => ({}));
      showErrorToast(
        data.error || "Failed to fetch characters",
        response.status
      );
    }
  } catch (err) {
    showErrorToast(err.message, err.status);
  }
};

const fetchAliases = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/alias/list?game=HonkaiStarRail`,
      {
        credentials: "include",
      }
    );
    if (response.status === 401) {
      router.push("/login");
      return;
    }
    if (response.ok) {
      const data = await response.json();
      aliases.value = Object.entries(data).map(([name, aliasList]) => ({
        name,
        aliases: aliasList,
      }));
    } else {
      const data = await response.json().catch(() => ({}));
      showErrorToast(data.error || "Failed to fetch aliases", response.status);
    }
  } catch (err) {
    showErrorToast(err.message, err.status);
  }
};

const searchCharacter = (event) => {
  const query = event.query.toLowerCase();
  filteredCharacters.value = allCharacters.value.filter((char) =>
    char.toLowerCase().includes(query)
  );
};

onMounted(() => {
  fetchCharacters();
});

// Auth Modal Data
const authProfileId = ref("");
const authPassphrase = ref("");
const authLoading = ref(false);
const authError = ref("");

const servers = [
  { value: "America", label: "America" },
  { value: "Europe", label: "Europe" },
  { value: "Asia", label: "Asia" },
  { value: "Sar", label: "TW/HK/MO" },
];

const user = JSON.parse(localStorage.getItem("mehrak_user") || "{}");
const canManage =
  user.isSuperAdmin ||
  (user.gameWritePermissions && user.gameWritePermissions.includes("hsr"));

const tabs = computed(() => {
  const t = [
    { id: "character", name: "Character" },
    { id: "moc", name: "Memory of Chaos" },
    { id: "pf", name: "Pure Fiction" },
    { id: "as", name: "Apocalyptic Shadow" },
    { id: "aa", name: "Anomaly Arbitration" },
    { id: "charlist", name: "Character List" },
  ];
  if (canManage) {
    t.push({ id: "manage", name: "Manage Characters" });
    t.push({ id: "aliases", name: "Manage Aliases" });
    t.push({ id: "codes", name: "Manage Codes" });
  }
  return t;
});

const newCharacterName = ref("");
const manageSearchQuery = ref("");
const aliasSearchQuery = ref("");
const manageLoading = ref(false);
const manageError = ref("");

// Add Alias Modal Data
const showAddAliasModal = ref(false);
const newAliasCharacter = ref("");
const newAliasList = ref("");
const addAliasLoading = ref(false);
const isEditingAlias = ref(false);
const originalAliases = ref([]);

// Codes Data
const codes = ref([]);
const selectedCodes = ref([]);
const newCodesInput = ref("");
const codesSearchQuery = ref("");
const codesLoading = ref(false);

const openAddAliasModal = () => {
  isEditingAlias.value = false;
  newAliasCharacter.value = "";
  newAliasList.value = "";
  originalAliases.value = [];
  showAddAliasModal.value = true;
};

const openEditAliasModal = (data) => {
  isEditingAlias.value = true;
  newAliasCharacter.value = data.name;
  newAliasList.value = data.aliases.join(", ");
  originalAliases.value = [...data.aliases];
  showAddAliasModal.value = true;
};

const filteredManageCharacters = computed(() => {
  if (!manageSearchQuery.value) return allCharacters.value;
  const query = manageSearchQuery.value.toLowerCase();
  return allCharacters.value.filter((char) =>
    char.toLowerCase().includes(query)
  );
});

const filteredAliases = computed(() => {
  if (!aliasSearchQuery.value) return aliases.value;
  const query = aliasSearchQuery.value.toLowerCase();
  return aliases.value.filter(
    (item) =>
      item.name.toLowerCase().includes(query) ||
      item.aliases.some((alias) => alias.toLowerCase().includes(query))
  );
});

const filteredCodes = computed(() => {
  if (!codesSearchQuery.value) return codes.value;
  const query = codesSearchQuery.value.toLowerCase();
  return codes.value.filter((c) => c.code.toLowerCase().includes(query));
});

const addCharacter = async () => {
  if (!newCharacterName.value) return;
  manageLoading.value = true;
  manageError.value = "";
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/characters/add?game=HonkaiStarRail`,
      {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ characters: [newCharacterName.value] }),
      }
    );
    if (response.status === 401) {
      router.push("/login");
      return;
    }
    if (!response.ok) {
      const data = await response.json();
      throw buildError(
        data.error || "Failed to add character",
        response.status
      );
    }
    newCharacterName.value = "";
    await fetchCharacters();
  } catch (err) {
    manageError.value = err.message;
    showErrorToast(err.message, err.status);
  } finally {
    manageLoading.value = false;
  }
};

const deleteCharacter = (name) => {
  confirm.require({
    message: `Are you sure you want to delete ${name}?`,
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
    accept: () => executeDeleteCharacter(name),
  });
};

const executeDeleteCharacter = async (name) => {
  manageLoading.value = true;
  manageError.value = "";
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/characters/delete?game=HonkaiStarRail&character=${encodeURIComponent(
        name
      )}`,
      {
        method: "DELETE",
        credentials: "include",
      }
    );
    if (response.status === 401) {
      router.push("/login");
      return;
    }
    if (!response.ok) {
      const data = await response.json();
      throw buildError(
        data.error || "Failed to delete character",
        response.status
      );
    }
    await fetchCharacters();
  } catch (err) {
    manageError.value = err.message;
    showErrorToast(err.message, err.status);
  } finally {
    manageLoading.value = false;
  }
};

const handleAliasSubmit = async () => {
  if (!newAliasCharacter.value || !newAliasList.value) return;
  addAliasLoading.value = true;

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const currentAliasesArray = newAliasList.value
      .split(",")
      .map((s) => s.trim())
      .filter((s) => s.length > 0);

    if (isEditingAlias.value) {
      const addedAliases = currentAliasesArray.filter(
        (a) => !originalAliases.value.includes(a)
      );
      const removedAliases = originalAliases.value.filter(
        (a) => !currentAliasesArray.includes(a)
      );

      const promises = [];

      if (addedAliases.length > 0) {
        promises.push(
          fetch(`${backendUrl}/alias/add?game=HonkaiStarRail`, {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify({
              character: newAliasCharacter.value,
              aliases: addedAliases,
            }),
          }).then(async (res) => {
            if (res.status === 401) {
              router.push("/login");
              throw buildError("Unauthorized", res.status);
            }
            if (!res.ok) {
              const data = await res.json();
              throw buildError(
                data.error || "Failed to add new aliases",
                res.status
              );
            }
          })
        );
      }

      for (const alias of removedAliases) {
        promises.push(
          fetch(
            `${backendUrl}/alias/delete?game=HonkaiStarRail&alias=${encodeURIComponent(
              alias
            )}`,
            {
              method: "DELETE",
              credentials: "include",
            }
          ).then(async (res) => {
            if (res.status === 401) {
              router.push("/login");
              throw buildError("Unauthorized", res.status);
            }
            if (!res.ok) {
              const data = await res.json();
              throw buildError(
                data.error || `Failed to delete alias ${alias}`,
                res.status
              );
            }
          })
        );
      }

      await Promise.all(promises);
    } else {
      const response = await fetch(
        `${backendUrl}/alias/add?game=HonkaiStarRail`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({
            character: newAliasCharacter.value,
            aliases: currentAliasesArray,
          }),
        }
      );

      if (response.status === 401) {
        router.push("/login");
        return;
      }

      if (!response.ok) {
        const data = await response.json();
        throw buildError(
          data.error || "Failed to add aliases",
          response.status
        );
      }
    }

    showAddAliasModal.value = false;
    newAliasCharacter.value = "";
    newAliasList.value = "";
    originalAliases.value = [];
    await fetchAliases();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: isEditingAlias.value
        ? "Aliases updated successfully"
        : "Aliases added successfully",
      life: 3000,
    });
  } catch (err) {
    showErrorToast(err.message, err.status);
  } finally {
    addAliasLoading.value = false;
  }
};

const fetchCodes = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/codes/list?game=HonkaiStarRail`,
      {
        credentials: "include",
      }
    );
    if (response.status === 401) {
      router.push("/login");
      return;
    }
    if (response.ok) {
      const data = await response.json();
      codes.value = data.codes.map((c) => ({ code: c }));
    } else {
      const data = await response.json().catch(() => ({}));
      showErrorToast(data.error || "Failed to fetch codes", response.status);
    }
  } catch (err) {
    showErrorToast(err.message, err.status);
  }
};

const confirmAddCodes = () => {
  if (!newCodesInput.value) return;
  confirm.require({
    message: "Are you sure you want to add these codes?",
    header: "Confirm Add",
    icon: "pi pi-exclamation-triangle",
    accept: executeAddCodes,
  });
};

const executeAddCodes = async () => {
  codesLoading.value = true;
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const codesToAdd = newCodesInput.value
      .split(",")
      .map((c) => c.trim())
      .filter((c) => c);

    const response = await fetch(
      `${backendUrl}/codes/add?game=HonkaiStarRail`,
      {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ Codes: codesToAdd }),
      }
    );

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    if (!response.ok) {
      const data = await response.json();
      throw buildError(data.error || "Failed to add codes", response.status);
    }

    newCodesInput.value = "";
    await fetchCodes();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: "Codes added successfully",
      life: 3000,
    });
  } catch (err) {
    showErrorToast(err.message, err.status);
  } finally {
    codesLoading.value = false;
  }
};

const confirmDeleteCodes = (codesList) => {
  confirm.require({
    message: `Are you sure you want to delete ${codesList.length} code(s)?`,
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
    accept: () => executeDeleteCodes(codesList),
  });
};

const executeDeleteCodes = async (codesList) => {
  codesLoading.value = true;
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const params = new URLSearchParams();
    params.append("game", "HonkaiStarRail");
    codesList.forEach((c) => params.append("codes", c));
    const response = await fetch(
      `${backendUrl}/codes/remove?${params.toString()}`,
      {
        method: "DELETE",
        credentials: "include",
      }
    );

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    if (!response.ok) {
      const data = await response.json();
      throw buildError(data.error || "Failed to delete codes", response.status);
    }

    selectedCodes.value = [];
    await fetchCodes();
    toast.add({
      severity: "success",
      summary: "Success",
      detail: "Codes deleted successfully",
      life: 3000,
    });
  } catch (err) {
    showErrorToast(err.message, err.status);
  } finally {
    codesLoading.value = false;
  }
};

// Clear result when tab changes
watch(activeTab, (newTab) => {
  error.value = "";
  if (newTab === "aliases" && canManage) {
    fetchAliases();
  } else if (newTab === "codes" && canManage) {
    fetchCodes();
  }
});

const executeCommand = async () => {
  loading.value = true;
  error.value = "";
  resultImages.value[activeTab.value] = "";

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    let endpoint = `/hsr/${activeTab.value}`;
    let payload = {
      profileId: Number(profileId.value),
      server: server.value,
    };

    if (activeTab.value === "character") {
      payload.character = characterName.value;
    }

    const response = await fetch(`${backendUrl}${endpoint}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify(payload),
    });

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    const data = await response.json();

    if (response.status === 403 && data.code === "AUTH_REQUIRED") {
      authProfileId.value = profileId.value;
      showAuthModal.value = true;
      return;
    }

    if (!response.ok) {
      throw buildError(data.error || "Command failed", response.status);
    }

    if (data.storageFileName) {
      resultImages.value[
        activeTab.value
      ] = `${backendUrl}/attachments/${data.storageFileName}`;
    }
  } catch (err) {
    error.value = err.message;
    showErrorToast(err.message, err.status);
  } finally {
    loading.value = false;
  }
};

const handleAuth = async () => {
  authLoading.value = true;
  authError.value = "";

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/profile-auth`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify({
        profileId: Number(authProfileId.value),
        passphrase: authPassphrase.value,
      }),
    });

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    const data = await response.json();

    if (!response.ok) {
      throw buildError(data.error || "Authentication failed", response.status);
    }

    showAuthModal.value = false;
    authPassphrase.value = "";
    // Retry the original command
    executeCommand();
  } catch (err) {
    authError.value = err.message;
    showErrorToast(err.message, err.status);
  } finally {
    authLoading.value = false;
  }
};
</script>

<template>
  <div class="hsr-view">
    <h1>Honkai: Star Rail</h1>

    <Tabs v-model:value="activeTab" scrollable>
      <TabList>
        <Tab
          v-for="tab in tabs"
          :key="tab.id"
          :value="tab.id"
          class="whitespace-nowrap shrink-0"
        >
          {{ tab.name }}
        </Tab>
      </TabList>
      <TabPanels>
        <TabPanel v-for="tab in tabs" :key="tab.id" :value="tab.id">
          <div v-if="tab.id === 'manage'">
            <Card>
              <template #title>Manage Characters</template>
              <template #content>
                <div class="flex flex-col gap-4">
                  <div class="flex gap-2">
                    <InputText
                      v-model="newCharacterName"
                      placeholder="New Character Name"
                      fluid
                      class="flex-1"
                    />
                    <Button
                      label="Add"
                      @click="addCharacter"
                      :loading="manageLoading"
                    />
                  </div>
                  <Message v-if="manageError" severity="error">{{
                    manageError
                  }}</Message>
                  <div class="flex flex-col gap-2">
                    <InputText
                      v-model="manageSearchQuery"
                      placeholder="Search characters..."
                      fluid
                    />
                  </div>
                  <div class="flex flex-col max-h-150 overflow-y-auto rounded">
                    <div
                      v-for="char in filteredManageCharacters"
                      :key="char"
                      class="flex justify-between items-center p-2 hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors"
                    >
                      <span>{{ char }}</span>
                      <Button
                        icon="pi pi-trash"
                        severity="danger"
                        text
                        @click="deleteCharacter(char)"
                        :loading="manageLoading"
                      />
                    </div>
                  </div>
                </div>
              </template>
            </Card>
          </div>
          <div v-else-if="tab.id === 'aliases'">
            <Card>
              <template #title>Manage Aliases</template>
              <template #content>
                <div class="flex flex-col gap-4">
                  <Button
                    label="Add"
                    @click="openAddAliasModal"
                    :loading="manageLoading"
                  />
                  <InputText
                    v-model="aliasSearchQuery"
                    placeholder="Search aliases..."
                    fluid
                  />
                  <DataTable
                    :value="filteredAliases"
                    paginator
                    :rows="10"
                    tableStyle="min-width: 50rem"
                  >
                    <Column
                      field="name"
                      header="Character Name"
                      sortable
                    ></Column>
                    <Column header="Aliases">
                      <template #body="slotProps">
                        <div class="flex flex-wrap gap-2">
                          <Tag
                            v-for="alias in slotProps.data.aliases"
                            :key="alias"
                            :value="alias"
                            severity="info"
                          />
                        </div>
                      </template>
                    </Column>
                    <Column style="width: 3rem">
                      <template #body="slotProps">
                        <Button
                          icon="pi pi-pencil"
                          text
                          rounded
                          severity="secondary"
                          @click="openEditAliasModal(slotProps.data)"
                        />
                      </template>
                    </Column>
                  </DataTable>
                </div>
              </template>
            </Card>
          </div>
          <div v-else-if="tab.id === 'codes'">
            <Card>
              <template #title>Manage Codes</template>
              <template #content>
                <div class="flex flex-col gap-4">
                  <div class="flex gap-2">
                    <InputText
                      v-model="newCodesInput"
                      placeholder="New Codes (comma-separated)"
                      fluid
                      class="flex-1"
                    />
                    <Button
                      label="Add"
                      @click="confirmAddCodes"
                      :loading="codesLoading"
                      :disabled="!newCodesInput"
                    />
                  </div>

                  <div class="flex justify-between gap-2">
                    <InputText
                      v-model="codesSearchQuery"
                      placeholder="Search codes..."
                      fluid
                      class="flex-1"
                    />
                    <Button
                      label="Delete Selected"
                      severity="danger"
                      @click="
                        confirmDeleteCodes(selectedCodes.map((c) => c.code))
                      "
                      :disabled="!selectedCodes.length"
                      :loading="codesLoading"
                    />
                  </div>

                  <DataTable
                    :value="filteredCodes"
                    v-model:selection="selectedCodes"
                    dataKey="code"
                    paginator
                    :rows="10"
                    tableStyle="min-width: 50rem"
                  >
                    <Column
                      selectionMode="multiple"
                      headerStyle="width: 3rem"
                    ></Column>
                    <Column field="code" header="Code" sortable></Column>
                    <Column style="width: 3rem">
                      <template #body="slotProps">
                        <Button
                          icon="pi pi-trash"
                          severity="danger"
                          text
                          rounded
                          @click="confirmDeleteCodes([slotProps.data.code])"
                          :loading="codesLoading"
                        />
                      </template>
                    </Column>
                  </DataTable>
                </div>
              </template>
            </Card>
          </div>
          <Card v-else class="command-card">
            <template #content>
              <form @submit.prevent="executeCommand">
                <div class="flex flex-col gap-4">
                  <div class="flex flex-row md:flex-row gap-4">
                    <div class="flex flex-col gap-2 flex-1">
                      <label>Profile ID (1-10)</label>
                      <InputNumber
                        v-model="profileId"
                        showButtons
                        :min="1"
                        :max="10"
                        fluid
                      />
                    </div>
                    <div class="flex flex-col gap-2 flex-1">
                      <label>Server</label>
                      <Select
                        v-model="server"
                        :options="servers"
                        optionLabel="label"
                        optionValue="value"
                        fluid
                        class="h-full items-center"
                      />
                    </div>
                  </div>

                  <!-- Specific Inputs -->
                  <div
                    v-if="activeTab === 'character'"
                    class="flex flex-col gap-2"
                  >
                    <label>Character Name</label>
                    <AutoComplete
                      v-model="characterName"
                      :suggestions="filteredCharacters"
                      @complete="searchCharacter"
                      dropdown
                      fluid
                      placeholder="e.g. Firefly"
                    />
                  </div>

                  <Button
                    type="submit"
                    :label="loading ? 'Executing...' : 'Execute'"
                    :loading="loading"
                    fluid
                  />
                </div>
              </form>
              <Message v-if="error" severity="error" class="mt-2">{{
                error
              }}</Message>
            </template>
          </Card>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <div v-if="resultImages[activeTab]" class="result-container mt-4">
      <Card>
        <template #content>
          <Image
            :src="resultImages[activeTab]"
            alt="Result"
            preview
            width="100%"
          />
        </template>
      </Card>
    </div>

    <!-- Auth Modal -->
    <Dialog
      v-model:visible="showAuthModal"
      modal
      header="Profile Authentication Required"
      :style="{ width: '25rem' }"
    >
      <p class="mb-4">
        Please authenticate profile <strong>{{ authProfileId }}</strong>
      </p>

      <form @submit.prevent="handleAuth">
        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label>Passphrase</label>
            <Password
              v-model="authPassphrase"
              required
              :feedback="false"
              toggleMask
              fluid
            />
          </div>

          <Message v-if="authError" severity="error">{{ authError }}</Message>

          <div class="flex justify-end gap-2">
            <Button
              type="button"
              label="Cancel"
              severity="secondary"
              @click="showAuthModal = false"
            />
            <Button
              type="submit"
              :label="authLoading ? 'Authenticating...' : 'Authenticate'"
              :loading="authLoading"
            />
          </div>
        </div>
      </form>
    </Dialog>

    <!-- Add/Edit Alias Modal -->
    <Dialog
      v-model:visible="showAddAliasModal"
      modal
      :header="isEditingAlias ? 'Edit Alias' : 'Add Alias'"
      :style="{ width: '30rem' }"
    >
      <form @submit.prevent="handleAliasSubmit">
        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label for="alias-char">Character Name</label>
            <InputText
              id="alias-char"
              v-model="newAliasCharacter"
              required
              placeholder="e.g. Firefly"
              fluid
              :disabled="isEditingAlias"
            />
          </div>
          <div class="flex flex-col gap-2">
            <label for="alias-list">Aliases (comma-separated)</label>
            <InputText
              id="alias-list"
              v-model="newAliasList"
              required
              placeholder="e.g. Sam, Stellaron Hunter"
              fluid
            />
          </div>
          <div class="flex justify-end gap-2 mt-2">
            <Button
              type="button"
              label="Cancel"
              severity="secondary"
              @click="showAddAliasModal = false"
            />
            <Button
              type="submit"
              :label="isEditingAlias ? 'Update' : 'Add'"
              :loading="addAliasLoading"
              :disabled="!newAliasCharacter || !newAliasList"
            />
          </div>
        </div>
      </form>
    </Dialog>
  </div>
</template>

<style scoped>
.result-image {
  max-width: 100%;
  border-radius: 8px;
  display: block;
  margin: 0 auto;
}
</style>
