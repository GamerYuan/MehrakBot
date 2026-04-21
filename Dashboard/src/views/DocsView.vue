<script setup>
import { ref } from "vue";
import { useRouter } from "vue-router";
import Button from "primevue/button";
import DocCard from "../components/docs/DocCard.vue";
import DocDetailModal from "../components/docs/DocDetailModal.vue";
import DocSearchBar from "../components/docs/DocSearchBar.vue";
import { useDocs } from "../composables/useDocs";

const router = useRouter();
const {
  loading,
  error,
  searchQuery,
  selectedGames,
  groupedDocuments,
  fetchDocumentDetail,
  toggleGame,
  selectAllGames,
  gameLabels,
} = useDocs();

const selectedDoc = ref(null);
const showDetailModal = ref(false);
const loadingDetail = ref(false);

const handleDocClick = async (doc) => {
  loadingDetail.value = true;
  showDetailModal.value = true;
  selectedDoc.value = { ...doc, parameters: [], examples: [] };

  try {
    const fullDoc = await fetchDocumentDetail(doc.id);
    selectedDoc.value = fullDoc;
  } catch (err) {
    console.error("Failed to fetch document details:", err);
  } finally {
    loadingDetail.value = false;
  }
};

const handleSearchUpdate = (value) => {
  searchQuery.value = value;
};
</script>

<template>
  <div class="docs-page">
    <nav class="nav">
      <div class="nav-logo" @click="router.push('/')">
        <span class="logo-icon">✦</span>
        <span>MehrakBot</span>
      </div>
      <div class="nav-links">
        <Button
          label="Dashboard"
          size="small"
          @click="router.push('/dashboard')"
        />
      </div>
    </nav>

    <main class="docs-content">
      <div class="docs-header">
        <h1>Documentation</h1>
        <p>Explore all available commands and features</p>
      </div>

      <DocSearchBar
        :searchQuery="searchQuery"
        :selectedGames="selectedGames"
        @update:searchQuery="handleSearchUpdate"
        @toggleGame="toggleGame"
        @selectAllGames="selectAllGames"
      />

      <div v-if="loading" class="loading-state">
        <i class="pi pi-spinner pi-spin"></i>
        <span>Loading documentation...</span>
      </div>

      <div v-else-if="error" class="error-state">
        <i class="pi pi-exclamation-triangle"></i>
        <span>{{ error }}</span>
      </div>

      <div v-else class="docs-sections">
        <section
          v-for="(docs, game) in groupedDocuments"
          :key="game"
          class="docs-section"
        >
          <h2 class="section-title">{{ gameLabels[game] }}</h2>
          <div class="docs-grid">
            <DocCard
              v-for="doc in docs"
              :key="doc.id"
              :doc="doc"
              @click="handleDocClick"
            />
          </div>
        </section>

        <div
          v-if="Object.keys(groupedDocuments).length === 0"
          class="empty-state"
        >
          <i class="pi pi-search"></i>
          <p>No commands found matching your search.</p>
        </div>
      </div>
    </main>

    <DocDetailModal
      v-model:visible="showDetailModal"
      :doc="selectedDoc"
      :loading="loadingDetail"
    />

    <footer class="footer">
      <p>&copy; 2026 MehrakBot. Not affiliated with HoYoverse.</p>
    </footer>
  </div>
</template>

<style scoped>
.docs-page {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: linear-gradient(to bottom, #0a0a0f, #111118);
}

.nav {
  position: sticky;
  top: 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 2rem;
  background: rgba(10, 10, 15, 0.9);
  backdrop-filter: blur(12px);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  z-index: 100;
}

.nav-logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 700;
  font-size: 1.2rem;
  cursor: pointer;
}

.logo-icon {
  color: #5865f2;
  font-size: 1.4rem;
}

.docs-content {
  flex: 1;
  padding: 2rem;
  max-width: 1200px;
  margin: 0 auto;
  width: 100%;
}

.docs-header {
  text-align: center;
  margin-bottom: 2.5rem;
}

.docs-header h1 {
  font-size: clamp(2rem, 5vw, 3rem);
  font-weight: 700;
  margin: 0 0 0.5rem;
  background: linear-gradient(135deg, #fff, #a0a0a0);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.docs-header p {
  color: #666;
  font-size: 1.1rem;
  margin: 0;
}

.loading-state,
.error-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  padding: 4rem 2rem;
  color: #666;
  text-align: center;
}

.loading-state i,
.error-state i,
.empty-state i {
  font-size: 2rem;
}

.error-state {
  color: #ff6b6b;
}

.docs-sections {
  display: flex;
  flex-direction: column;
  gap: 2.5rem;
}

.docs-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.section-title {
  font-size: 1.25rem;
  font-weight: 600;
  margin: 0;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  color: #ccc;
}

.docs-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}

.footer {
  padding: 1.5rem 2rem;
  text-align: center;
  border-top: 1px solid rgba(255, 255, 255, 0.05);
  background: rgba(0, 0, 0, 0.3);
}

.footer p {
  margin: 0;
  color: #444;
  font-size: 0.85rem;
}

@media (max-width: 640px) {
  .docs-content {
    padding: 1rem;
  }

  .nav {
    padding: 1rem;
  }

  .docs-grid {
    grid-template-columns: 1fr;
  }
}
</style>
