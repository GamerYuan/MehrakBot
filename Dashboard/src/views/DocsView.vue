<script setup>
import { ref, watch, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import AppNavbar from "../components/AppNavbar.vue";
import AppFooter from "../components/AppFooter.vue";
import DocCard from "../components/docs/DocCard.vue";
import DocDetailModal from "../components/docs/DocDetailModal.vue";
import DocSearchBar from "../components/docs/DocSearchBar.vue";
import GettingStartedTab from "../components/docs/tabs/GettingStartedTab.vue";
import FaqTab from "../components/docs/tabs/FaqTab.vue";
import AboutMehrakTab from "../components/docs/tabs/AboutMehrakTab.vue";
import AboutCookiesTab from "../components/docs/tabs/AboutCookiesTab.vue";
import ReleaseNotesTab from "../components/docs/tabs/ReleaseNotesTab.vue";
import AliasTab from "../components/docs/tabs/AliasTab.vue";
import { useDocs } from "../composables/useDocs";
import Card from "primevue/card";
import Tabs from "primevue/tabs";
import TabList from "primevue/tablist";
import Tab from "primevue/tab";
import TabPanels from "primevue/tabpanels";
import TabPanel from "primevue/tabpanel";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";

const route = useRoute();
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
const activeTab = ref("getting-started");
const appendixTab = ref("about");

const docTabs = [
  { key: "getting-started", label: "Getting Started" },
  { key: "commands", label: "Commands" },
  { key: "alias", label: "Aliases" },
  { key: "faq", label: "FAQ" },
  { key: "appendix", label: "Appendix" },
];

const appendixTabs = [
  { key: "about", label: "About Mehrak" },
  { key: "cookies", label: "About HoYoLAB Cookies" },
  { key: "notes", label: "Release Notes" },
];

const syncFromUrl = () => {
  const tab = route.query.tab;
  const section = route.query.section;
  const hash = route.hash;

  if (tab) {
    activeTab.value = tab;
    if (tab === "appendix" && section) {
      appendixTab.value = section;
    }
  }

  if (hash) {
    setTimeout(() => {
      const element = document.querySelector(hash);
      if (element) {
        element.scrollIntoView({ behavior: "smooth", block: "start" });
      }
    }, 100);
  }
};

watch(() => route.query, syncFromUrl, { immediate: true });

onMounted(() => {
  syncFromUrl();
});

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
  router.push({ path: "/docs", query: { tab } });
};
</script>

<template>
  <div class="docs-page">
    <AppNavbar />

    <main class="docs-content">
      <div class="grid grid-cols-1 md:grid-cols-[260px_1fr] gap-8 items-start">
        <aside class="lg:sticky lg:top-28 h-fit">
          <Card class="bg-white/5 border border-white/10 rounded-2xl">
            <template #content>
              <div class="mb-5">
                <h1
                  class="text-3xl font-bold tracking-tight text-zinc-100 mb-2"
                >
                  Documentation
                </h1>
                <p class="text-zinc-400 text-sm leading-relaxed m-0">
                  Explore all available commands and features
                </p>
              </div>

              <div
                class="flex md:flex-col gap-2 overflow-x-auto md:overflow-visible pb-2 md:pb-0"
              >
                <button
                  v-for="tab in docTabs"
                  :key="tab.key"
                  type="button"
                  :class="[
                    'w-full text-left rounded-lg px-3 py-2.5 text-sm transition-all whitespace-nowrap md:whitespace-normal',
                    activeTab === tab.key
                      ? 'text-white border border-emerald-500/50 bg-emerald-500/20'
                      : 'text-zinc-400 border border-white/10 bg-white/5 hover:text-zinc-200 hover:border-white/20 hover:bg-white/10',
                  ]"
                  @click="handleTabChange(tab.key)"
                >
                  {{ tab.label }}
                </button>
              </div>
            </template>
          </Card>
        </aside>

        <section class="min-h-130">
          <div v-if="activeTab === 'getting-started'">
            <GettingStartedTab />
          </div>

          <div v-else-if="activeTab === 'commands'" class="flex flex-col gap-6">
            <Card class="bg-white/5 border border-white/10 rounded-2xl">
              <template #content>
                <div class="flex flex-col gap-4">
                  <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
                    Commands
                  </h2>
                  <p class="text-zinc-300 leading-relaxed m-0">
                    Search and view details about available commands
                  </p>
                </div>
              </template>
            </Card>

            <DocSearchBar
              :searchQuery="searchQuery"
              :selectedGames="selectedGames"
              @update:searchQuery="handleSearchUpdate"
              @toggleGame="toggleGame"
              @selectAllGames="selectAllGames"
            />

            <div
              v-if="loading"
              class="flex flex-col items-center justify-center gap-4 py-16 text-zinc-400"
            >
              <ProgressSpinner
                style="width: 50px; height: 50px"
                strokeWidth="4"
              />
              <span>Loading documentation...</span>
            </div>

            <div v-else-if="error" class="py-8">
              <Message severity="error" :closable="false">
                {{ error }}
              </Message>
            </div>

            <div v-else class="flex flex-col gap-10">
              <section
                v-for="(docs, game) in groupedDocuments"
                :key="game"
                class="flex flex-col gap-4"
              >
                <h2
                  class="text-xl font-semibold text-zinc-300 m-0 pb-2 border-b border-white/10"
                >
                  {{ gameLabels[game] }}
                </h2>
                <div
                  class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4"
                >
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
                class="py-16"
              >
                <Message severity="warn" :closable="false" icon="pi pi-search">
                  No commands found matching your search.
                </Message>
              </div>
            </div>
          </div>

          <div v-else-if="activeTab === 'alias'">
            <AliasTab />
          </div>

          <div v-else-if="activeTab === 'faq'">
            <FaqTab />
          </div>

            <div v-else>
              <Card class="bg-white/5 border border-white/10 rounded-2xl mb-6">
                <template #content>
                  <div class="flex flex-col gap-4">
                    <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
                      Appendix
                    </h2>
                    <p class="text-zinc-300 leading-relaxed m-0">
                      Additional information and resources
                    </p>
                  </div>
                </template>
              </Card>

              <Tabs v-model:value="appendixTab">
              <TabList>
                <Tab
                  v-for="tab in appendixTabs"
                  :key="tab.key"
                  :value="tab.key"
                >
                  {{ tab.label }}
                </Tab>
              </TabList>
              <TabPanels class="bg-transparent px-0 py-6">
                <TabPanel value="about">
                  <AboutMehrakTab />
                </TabPanel>
                <TabPanel value="cookies">
                  <AboutCookiesTab />
                </TabPanel>
                <TabPanel value="notes">
                  <ReleaseNotesTab />
                </TabPanel>
              </TabPanels>
            </Tabs>
          </div>
        </section>
      </div>
    </main>

    <DocDetailModal
      v-model:visible="showDetailModal"
      :doc="selectedDoc"
      :loading="loadingDetail"
    />

    <AppFooter />
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

:deep(.p-tabs) {
  border-radius: 0.75rem;
  overflow: clip;
}

@media (max-width: 640px) {
  .docs-content {
    padding: 6.5rem 1rem 1rem;
  }
}
</style>
