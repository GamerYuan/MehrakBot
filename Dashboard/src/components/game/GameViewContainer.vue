<script setup>
import Tabs from 'primevue/tabs';
import TabList from 'primevue/tablist';
import Tab from 'primevue/tab';
import TabPanels from 'primevue/tabpanels';
import TabPanel from 'primevue/tabpanel';
import Card from 'primevue/card';
import Image from 'primevue/image';

import CommandCard from './CommandCard.vue';
import ManageCharactersCard from './ManageCharactersCard.vue';
import ManageAliasesCard from './ManageAliasesCard.vue';
import ManageCodesCard from './ManageCodesCard.vue';
import AuthModal from './AuthModal.vue';
import StatEditModal from './StatEditModal.vue';

const props = defineProps({
  title: String,
  activeTab: String,
  tabs: Array,
  config: Object,

  profileId: Number,
  server: String,
  characterName: String,
  floor: Number,
  filteredCharacters: Array,
  loading: Boolean,
  error: String,
  resultImages: Object,

  canManage: Boolean,
  manageLoading: Boolean,
  manageError: String,
  newCharacterName: String,
  manageSearchQuery: String,
  filteredManageCharacters: Array,
  hasStatEdit: Boolean,

  aliasSearchQuery: String,
  aliases: Array,
  filteredAliases: Array,
  showAddAliasModal: Boolean,
  newAliasCharacter: String,
  newAliasList: String,
  addAliasLoading: Boolean,
  isEditingAlias: Boolean,

  hasCodesManagement: Boolean,
  codes: Array,
  filteredCodes: Array,
  selectedCodes: Array,
  newCodesInput: String,
  codesSearchQuery: String,
  codesLoading: Boolean,

  showAuthModal: Boolean,
  authProfileId: [String, Number],
  authPassphrase: String,
  authLoading: Boolean,
  authError: String,

  showEditStatModal: Boolean,
  editStatCharacter: String,
  editStatBase: [Number, null],
  editStatMax: [Number, null],
  editStatFetching: Boolean,
  editStatLoading: Boolean,
});

const emit = defineEmits([
  'update:activeTab',
  'update:profileId',
  'update:server',
  'update:characterName',
  'update:floor',
  'searchCharacter',
  'execute',
  'update:newCharacterName',
  'update:manageSearchQuery',
  'addCharacter',
  'deleteCharacter',
  'editStat',
  'update:aliasSearchQuery',
  'update:showAddAliasModal',
  'update:newAliasCharacter',
  'update:newAliasList',
  'openAddAliasModal',
  'openEditAliasModal',
  'handleAliasSubmit',
  'update:selectedCodes',
  'update:newCodesInput',
  'update:codesSearchQuery',
  'confirmAddCodes',
  'confirmDeleteCodes',
  'update:showAuthModal',
  'update:authPassphrase',
  'handleAuth',
  'update:showEditStatModal',
  'update:editStatBase',
  'update:editStatMax',
  'handleStatSubmit',
]);

const getTabConfig = (tabId) => props.config.tabs.find((t) => t.id === tabId);
</script>

<template>
  <div class="game-view">
    <h1>{{ title }}</h1>

    <Tabs :value="activeTab" @update:value="(value) => emit('update:activeTab', value)" scrollable>
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
          <ManageCharactersCard
            v-if="tab.id === 'manage'"
            :characters="filteredManageCharacters"
            :filteredCharacters="filteredManageCharacters"
            :newCharacterName="newCharacterName"
            :manageSearchQuery="manageSearchQuery"
            :manageLoading="manageLoading"
            :manageError="manageError"
            :hasStatEdit="hasStatEdit"
            @update:newCharacterName="(value) => emit('update:newCharacterName', value)"
            @update:manageSearchQuery="(value) => emit('update:manageSearchQuery', value)"
            @addCharacter="emit('addCharacter')"
            @deleteCharacter="(name) => emit('deleteCharacter', name)"
            @editStat="(name) => emit('editStat', name)"
          />

          <ManageAliasesCard
            v-else-if="tab.id === 'aliases'"
            :aliases="aliases"
            :filteredAliases="filteredAliases"
            :aliasSearchQuery="aliasSearchQuery"
            :manageLoading="manageLoading"
            :showAddAliasModal="showAddAliasModal"
            :newAliasCharacter="newAliasCharacter"
            :newAliasList="newAliasList"
            :addAliasLoading="addAliasLoading"
            :isEditingAlias="isEditingAlias"
            @update:aliasSearchQuery="(value) => emit('update:aliasSearchQuery', value)"
            @update:showAddAliasModal="(value) => emit('update:showAddAliasModal', value)"
            @update:newAliasCharacter="(value) => emit('update:newAliasCharacter', value)"
            @update:newAliasList="(value) => emit('update:newAliasList', value)"
            @openAddAliasModal="emit('openAddAliasModal')"
            @openEditAliasModal="(data) => emit('openEditAliasModal', data)"
            @handleAliasSubmit="emit('handleAliasSubmit')"
          />

          <ManageCodesCard
            v-else-if="tab.id === 'codes'"
            :codes="codes"
            :filteredCodes="filteredCodes"
            :selectedCodes="selectedCodes"
            :newCodesInput="newCodesInput"
            :codesSearchQuery="codesSearchQuery"
            :codesLoading="codesLoading"
            @update:selectedCodes="(value) => emit('update:selectedCodes', value)"
            @update:newCodesInput="(value) => emit('update:newCodesInput', value)"
            @update:codesSearchQuery="(value) => emit('update:codesSearchQuery', value)"
            @confirmAddCodes="emit('confirmAddCodes')"
            @confirmDeleteCodes="(list) => emit('confirmDeleteCodes', list)"
          />

          <CommandCard
            v-else
            :activeTab="activeTab"
            :tabConfig="getTabConfig(tab.id)"
            :config="config"
            :profileId="profileId"
            :server="server"
            :characterName="characterName"
            :floor="floor"
            :filteredCharacters="filteredCharacters"
            :loading="loading"
            :error="error"
            @update:profileId="(value) => emit('update:profileId', value)"
            @update:server="(value) => emit('update:server', value)"
            @update:characterName="(value) => emit('update:characterName', value)"
            @update:floor="(value) => emit('update:floor', value)"
            @searchCharacter="(e) => emit('searchCharacter', e)"
            @execute="emit('execute')"
          />
        </TabPanel>
      </TabPanels>
    </Tabs>

    <div v-if="resultImages[activeTab]" class="result-container mt-4">
      <Card>
        <template #content>
          <Image :src="resultImages[activeTab]" alt="Result" preview width="100%" />
        </template>
      </Card>
    </div>

    <AuthModal
      :visible="showAuthModal"
      :authProfileId="authProfileId"
      :authPassphrase="authPassphrase"
      :authLoading="authLoading"
      :authError="authError"
      @update:visible="(value) => emit('update:showAuthModal', value)"
      @update:authPassphrase="(value) => emit('update:authPassphrase', value)"
      @handleAuth="emit('handleAuth')"
    />

    <StatEditModal
      v-if="hasStatEdit"
      :visible="showEditStatModal"
      :characterName="editStatCharacter"
      :baseVal="editStatBase"
      :maxAscVal="editStatMax"
      :loading="editStatLoading"
      :fetching="editStatFetching"
      @update:visible="(value) => emit('update:showEditStatModal', value)"
      @update:baseVal="(value) => emit('update:editStatBase', value)"
      @update:maxAscVal="(value) => emit('update:editStatMax', value)"
      @submit="emit('handleStatSubmit')"
    />
  </div>
</template>

<style scoped>
.result-container :deep(img) {
  max-width: 100%;
  border-radius: 8px;
}
</style>
