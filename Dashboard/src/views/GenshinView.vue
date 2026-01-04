<script setup>
import { ref, watch, onMounted, computed } from "vue";
import { useRouter } from "vue-router";
import { useConfirm } from "primevue/useconfirm";
import Tabs from "primevue/tabs";
import TabList from "primevue/tablist";
import Tab from "primevue/tab";
import TabPanels from "primevue/tabpanels";
import TabPanel from "primevue/tabpanel";
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
const activeTab = ref("character");
const loading = ref(false);
const error = ref("");
const resultImage = ref("");
const showAuthModal = ref(false);

// Common Data
const profileId = ref(1);
const server = ref("America");

// Specific Data
const characterName = ref("");
const abyssFloor = ref(12);

const allCharacters = ref([]);
const filteredCharacters = ref([]);

const fetchCharacters = async () => {
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/characters/list?game=Genshin`, {
      credentials: "include",
    });
    if (response.ok) {
      const data = await response.json();
      allCharacters.value = data.sort();
    }
  } catch (err) {
    console.error("Failed to fetch characters:", err);
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
  (user.gameWritePermissions && user.gameWritePermissions.includes("genshin"));

const tabs = computed(() => {
  const t = [
    { id: "character", name: "Character" },
    { id: "abyss", name: "Abyss" },
    { id: "theater", name: "Theater" },
    { id: "stygian", name: "Stygian" },
    { id: "charlist", name: "Character List" },
  ];
  if (canManage) {
    t.push({ id: "manage", name: "Manage Characters" });
  }
  return t;
});

const newCharacterName = ref("");
const manageSearchQuery = ref("");
const manageLoading = ref(false);
const manageError = ref("");

const filteredManageCharacters = computed(() => {
  if (!manageSearchQuery.value) return allCharacters.value;
  const query = manageSearchQuery.value.toLowerCase();
  return allCharacters.value.filter((char) =>
    char.toLowerCase().includes(query)
  );
});

const addCharacter = async () => {
  if (!newCharacterName.value) return;
  manageLoading.value = true;
  manageError.value = "";
  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(`${backendUrl}/characters/add?game=Genshin`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify({ characters: [newCharacterName.value] }),
    });
    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to add character");
    }
    newCharacterName.value = "";
    await fetchCharacters();
  } catch (err) {
    manageError.value = err.message;
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
      `${backendUrl}/characters/delete?game=Genshin&character=${encodeURIComponent(
        name
      )}`,
      {
        method: "DELETE",
        credentials: "include",
      }
    );
    if (!response.ok) {
      const data = await response.json();
      throw new Error(data.error || "Failed to delete character");
    }
    await fetchCharacters();
  } catch (err) {
    manageError.value = err.message;
  } finally {
    manageLoading.value = false;
  }
};

// Clear result when tab changes
watch(activeTab, () => {
  resultImage.value = "";
  error.value = "";
});

const executeCommand = async () => {
  loading.value = true;
  error.value = "";
  resultImage.value = "";

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    let endpoint = `/genshin/${activeTab.value}`;
    let payload = {
      profileId: Number(profileId.value),
      server: server.value,
    };

    if (activeTab.value === "character") {
      payload.character = characterName.value;
    } else if (activeTab.value === "abyss") {
      payload.floor = Number(abyssFloor.value);
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
      throw new Error(data.error || "Command failed");
    }

    if (data.storageFileName) {
      resultImage.value = `${backendUrl}/attachments/${data.storageFileName}`;
    }
  } catch (err) {
    error.value = err.message;
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
      throw new Error(data.error || "Authentication failed");
    }

    showAuthModal.value = false;
    authPassphrase.value = "";
    // Retry the original command
    executeCommand();
  } catch (err) {
    authError.value = err.message;
  } finally {
    authLoading.value = false;
  }
};
</script>

<template>
  <div class="genshin-view">
    <h1>Genshin Impact</h1>

    <Tabs v-model:value="activeTab">
      <TabList>
        <Tab v-for="tab in tabs" :key="tab.id" :value="tab.id">{{
          tab.name
        }}</Tab>
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
                  <div
                    class="flex flex-col max-h-150 overflow-y-auto rounded p-2"
                  >
                    <div
                      v-for="char in filteredManageCharacters"
                      :key="char"
                      class="flex justify-between items-center p-2 hover:bg-gray-100 dark:hover:bg-gray-800 rounded transition-colors"
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
                      placeholder="e.g. Nahida"
                    />
                  </div>

                  <div v-if="activeTab === 'abyss'" class="flex flex-col gap-2">
                    <label>Floor (9-12)</label>
                    <InputNumber
                      v-model="abyssFloor"
                      showButtons
                      :min="9"
                      :max="12"
                      fluid
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

    <div v-if="resultImage" class="result-container mt-4">
      <Card>
        <template #content>
          <Image :src="resultImage" alt="Result" preview width="100%" />
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
