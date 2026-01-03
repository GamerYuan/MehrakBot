<script setup>
import { ref, watch } from "vue";
import { useRouter } from "vue-router";
import Tabs from "primevue/tabs";
import TabList from "primevue/tablist";
import Tab from "primevue/tab";
import TabPanels from "primevue/tabpanels";
import TabPanel from "primevue/tabpanel";
import InputText from "primevue/inputtext";
import InputNumber from "primevue/inputnumber";
import Select from "primevue/select";
import Button from "primevue/button";
import Card from "primevue/card";
import Dialog from "primevue/dialog";
import Password from "primevue/password";
import Message from "primevue/message";
import Image from "primevue/image";

const router = useRouter();
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

const tabs = [
  { id: "character", name: "Character" },
  { id: "abyss", name: "Abyss" },
  { id: "theater", name: "Theater" },
  { id: "stygian", name: "Stygian" },
  { id: "charlist", name: "Character List" },
];

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
          <Card class="command-card">
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
                    <InputText
                      v-model="characterName"
                      required
                      placeholder="e.g. Nahida"
                      fluid
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
