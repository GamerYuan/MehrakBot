<script setup>
import { useGameViewInject } from "../../composables/game/injectKey";
import Tabs from "primevue/tabs";
import TabList from "primevue/tablist";
import Tab from "primevue/tab";
import TabPanels from "primevue/tabpanels";
import TabPanel from "primevue/tabpanel";
import Card from "primevue/card";
import Image from "primevue/image";

import CommandCard from "./CommandCard.vue";
import ManageCharactersCard from "./ManageCharactersCard.vue";
import ManageAliasesCard from "./ManageAliasesCard.vue";
import ManageCodesCard from "./ManageCodesCard.vue";
import AuthModal from "./AuthModal.vue";
import StatEditModal from "./StatEditModal.vue";
import PortraitConfigModal from "./PortraitConfigModal.vue";

const gv = useGameViewInject();

const getTabConfig = (tabId) => gv.config.tabs.find((t) => t.id === tabId);
</script>

<template>
  <div class="game-view">
    <h1 class="text-4xl font-bold mb-3">{{ gv.config.title }}</h1>

    <Tabs v-model:value="gv.activeTab" scrollable>
      <TabList>
        <Tab
          v-for="tab in gv.tabs"
          :key="tab.id"
          :value="tab.id"
          class="whitespace-nowrap shrink-0"
        >
          {{ tab.name }}
        </Tab>
      </TabList>
      <TabPanels>
        <TabPanel v-for="tab in gv.tabs" :key="tab.id" :value="tab.id">
          <ManageCharactersCard
            v-if="tab.id === 'manage'"
          />

          <ManageAliasesCard
            v-else-if="tab.id === 'aliases'"
          />

          <ManageCodesCard
            v-else-if="tab.id === 'codes'"
          />

          <CommandCard
            v-else
            :tabConfig="getTabConfig(tab.id)"
          />
        </TabPanel>
      </TabPanels>
    </Tabs>

    <div v-if="gv.resultImages[gv.activeTab]" class="result-container mt-4">
      <Card>
        <template #content>
          <Image
            :src="gv.resultImages[gv.activeTab]"
            alt="Result"
            preview
            width="100%"
          />
        </template>
      </Card>
    </div>

    <AuthModal />

    <StatEditModal v-if="gv.config.hasStatEdit" />

    <PortraitConfigModal />
  </div>
</template>

<style scoped>
.result-container :deep(img) {
  max-width: 100%;
  border-radius: 8px;
}
</style>