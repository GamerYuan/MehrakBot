<script setup>
import { ref } from "vue";
import AppNavbar from "../components/AppNavbar.vue";
import DocCard from "../components/docs/DocCard.vue";
import DocDetailModal from "../components/docs/DocDetailModal.vue";
import DocSearchBar from "../components/docs/DocSearchBar.vue";
import GettingStartedTab from "../components/docs/tabs/GettingStartedTab.vue";
import { useDocs } from "../composables/useDocs";

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
const activeTab = ref("getting-started");
const appendixTab = ref("reference");

const docTabs = [
  { key: "getting-started", label: "Getting Started" },
  { key: "commands", label: "Commands" },
  { key: "appendix", label: "Appendix" },
];

const appendixTabs = [
  { key: "reference", label: "Reference" },
  { key: "faq", label: "FAQ" },
  { key: "notes", label: "Release Notes" },
];

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

const handleTabChange = (tab) => {
  activeTab.value = tab;
  showDetailModal.value = false;
};

const getAppendixPlaceholder = (tab) => {
  if (tab === "reference") {
    return "Use this section for glossary terms, option references, or quick lookup tables.";
  }
  if (tab === "faq") {
    return "Add common questions and answers for setup issues, account linking, and troubleshooting.";
  }
  return "Track important documentation updates and behavior changes between bot releases.";
};
</script>

<template>
  <div class="docs-page">
    <AppNavbar />

    <main class="docs-content">
      <div class="docs-shell">
        <aside class="docs-sidebar">
          <div class="sidebar-card">
            <div class="sidebar-heading">
              <h1 class="text-3xl font-bold tracking-tight text-zinc-100">
                Documentation
              </h1>
              <p>Explore all available commands and features</p>
            </div>

            <nav class="tab-nav" aria-label="Documentation sections">
              <button
                v-for="tab in docTabs"
                :key="tab.key"
                type="button"
                :class="['tab-button', { active: activeTab === tab.key }]"
                @click="handleTabChange(tab.key)"
              >
                {{ tab.label }}
              </button>
            </nav>
          </div>
        </aside>

        <section class="docs-panel">
          <div
            v-if="activeTab === 'getting-started'"
            class="getting-started-panel"
          >
            <GettingStartedTab />
          </div>

          <div v-else-if="activeTab === 'commands'" class="commands-panel">
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
          </div>

          <div v-else class="appendix-panel">
            <div
              class="appendix-subtabs"
              role="tablist"
              aria-label="Appendix sections"
            >
              <button
                v-for="tab in appendixTabs"
                :key="tab.key"
                type="button"
                :class="['appendix-tab', { active: appendixTab === tab.key }]"
                @click="appendixTab = tab.key"
              >
                {{ tab.label }}
              </button>
            </div>

            <div class="placeholder-card">
              <h2 class="text-2xl font-semibold tracking-tight text-zinc-100">
                Appendix
              </h2>
              <p>{{ getAppendixPlaceholder(appendixTab) }}</p>
            </div>
          </div>
        </section>
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

.docs-content {
  flex: 1;
  padding: 6rem 2rem 2rem;
  max-width: 1300px;
  margin: 0 auto;
  width: 100%;
}

.docs-shell {
  display: grid;
  grid-template-columns: 260px minmax(0, 1fr);
  gap: 2rem;
  align-items: start;
}

.docs-sidebar {
  position: sticky;
  top: 6.8rem;
}

.sidebar-card {
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.03);
  padding: 1.2rem;
}

.sidebar-heading {
  margin-bottom: 1.25rem;
}

.sidebar-heading h1 {
  margin: 0 0 0.45rem;
}

.sidebar-heading p {
  color: #7a7a7a;
  font-size: 0.9rem;
  line-height: 1.5;
  margin: 0;
}

.tab-nav {
  display: flex;
  flex-direction: column;
  gap: 0.55rem;
}

.tab-button {
  width: 100%;
  text-align: left;
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.02);
  color: #b7b7b7;
  border-radius: 10px;
  padding: 0.65rem 0.75rem;
  font-size: 0.92rem;
  transition: all 0.2s ease;
  cursor: pointer;
}

.tab-button:hover {
  color: #ececec;
  border-color: rgba(255, 255, 255, 0.16);
  background: rgba(255, 255, 255, 0.05);
}

.tab-button.active {
  color: #fff;
  border-color: rgba(var(--accent-rgb), 0.5);
  background: rgba(var(--accent-rgb), 0.22);
}

.docs-panel {
  min-height: 520px;
}

.commands-panel,
.appendix-panel {
  display: flex;
  flex-direction: column;
  gap: 1.4rem;
}

.placeholder-card {
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.03);
  padding: 1.6rem;
}

.placeholder-card h2 {
  margin: 0 0 0.75rem;
}

.placeholder-card p {
  margin: 0;
  color: #9b9b9b;
  line-height: 1.7;
}

.appendix-subtabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.55rem;
}

.appendix-tab {
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: rgba(255, 255, 255, 0.03);
  color: #acacac;
  border-radius: 999px;
  padding: 0.45rem 0.8rem;
  font-size: 0.82rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.appendix-tab:hover {
  color: #ececec;
  border-color: rgba(255, 255, 255, 0.2);
}

.appendix-tab.active {
  color: #fff;
  border-color: rgba(var(--accent-rgb), 0.45);
  background: rgba(var(--accent-rgb), 0.22);
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
    padding: 6.5rem 1rem 1rem;
  }

  .docs-shell {
    grid-template-columns: 1fr;
    gap: 1rem;
  }

  .docs-sidebar {
    position: static;
  }

  .tab-nav {
    flex-direction: row;
    overflow-x: auto;
    padding-bottom: 0.15rem;
  }

  .tab-button {
    min-width: 140px;
    white-space: nowrap;
  }

  .placeholder-card {
    padding: 1.2rem;
  }

  .docs-grid {
    grid-template-columns: 1fr;
  }
}
</style>
