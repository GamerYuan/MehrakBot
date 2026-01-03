<script setup>
import { ref, watch } from "vue";
import { useRouter } from "vue-router";

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

    <div class="tabs">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        class="tab-btn"
        :class="{ active: activeTab === tab.id }"
        @click="activeTab = tab.id"
      >
        {{ tab.name }}
      </button>
    </div>

    <div class="card command-card">
      <form @submit.prevent="executeCommand">
        <div class="form-row">
          <div class="form-group">
            <label>Profile ID (1-10)</label>
            <div class="number-input-wrapper">
              <button
                type="button"
                class="number-input-btn"
                @click="profileId = Math.max(1, (Number(profileId) || 0) - 1)"
              >
                -
              </button>
              <input
                v-model="profileId"
                type="number"
                min="1"
                max="10"
                required
              />
              <button
                type="button"
                class="number-input-btn"
                @click="profileId = Math.min(10, (Number(profileId) || 0) + 1)"
              >
                +
              </button>
            </div>
          </div>
          <div class="form-group">
            <label>Server</label>
            <select v-model="server">
              <option v-for="s in servers" :key="s.value" :value="s.value">
                {{ s.label }}
              </option>
            </select>
          </div>
        </div>

        <!-- Specific Inputs -->
        <div v-if="activeTab === 'character'" class="form-group">
          <label>Character Name</label>
          <input
            type="text"
            v-model="characterName"
            required
            placeholder="e.g. Nahida"
          />
        </div>

        <div v-if="activeTab === 'abyss'" class="form-group">
          <label>Floor (9-12)</label>
          <div class="number-input-wrapper">
            <button
              type="button"
              class="number-input-btn"
              @click="abyssFloor = Math.max(9, Number(abyssFloor) - 1)"
            >
              -
            </button>
            <input
              v-model="abyssFloor"
              type="number"
              min="9"
              max="12"
              required
            />
            <button
              type="button"
              class="number-input-btn"
              @click="abyssFloor = Math.min(12, Number(abyssFloor) + 1)"
            >
              +
            </button>
          </div>
        </div>

        <button type="submit" class="btn primary" :disabled="loading">
          {{ loading ? "Executing..." : "Execute" }}
        </button>
      </form>

      <div v-if="error" class="error-text mt-2">{{ error }}</div>
    </div>

    <div v-if="resultImage" class="result-container mt-2">
      <div class="card">
        <img :src="resultImage" alt="Result" class="result-image" />
      </div>
    </div>

    <!-- Auth Modal -->
    <div v-if="showAuthModal" class="modal-overlay">
      <div class="modal">
        <h2>Profile Authentication Required</h2>
        <p>
          Please authenticate profile <strong>{{ authProfileId }}</strong>
        </p>

        <form @submit.prevent="handleAuth">
          <div class="form-group">
            <label>Passphrase</label>
            <input v-model="authPassphrase" type="password" required />
          </div>

          <div v-if="authError" class="error-text mb-2">{{ authError }}</div>

          <div class="modal-actions">
            <button
              type="button"
              @click="showAuthModal = false"
              class="btn secondary"
            >
              Cancel
            </button>
            <button type="submit" class="btn primary" :disabled="authLoading">
              {{ authLoading ? "Authenticating..." : "Authenticate" }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tabs {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1.5rem;
  flex-wrap: wrap;
}

.tab-btn {
  padding: 0.8rem 1.5rem;
  background-color: var(--card-bg);
  border: 1px solid var(--border-color);
  color: #ccc;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s;
  font-weight: 500;
}

.tab-btn:hover {
  background-color: #333;
  color: white;
}

.tab-btn.active {
  background-color: var(--primary-color);
  color: white;
  border-color: var(--primary-color);
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.result-image {
  max-width: 100%;
  border-radius: 8px;
  display: block;
  margin: 0 auto;
}

@media (max-width: 600px) {
  .form-row {
    grid-template-columns: 1fr;
  }
}
</style>
