<script setup>
import { useGameView } from "../composables/useGameView";
import { gameConfigs } from "../configs/gameConfigs";
import GameViewContainer from "../components/game/GameViewContainer.vue";

const {
  config,
  activeTab,
  loading,
  error,
  resultImages,
  showAuthModal,
  profileId,
  server,
  characterName,
  floor,
  allCharacters,
  filteredCharacters,
  authProfileId,
  authPassphrase,
  authLoading,
  authError,
  canManage,
  tabs,
  newCharacterName,
  manageSearchQuery,
  showOnlyMissingAscension,
  manageCharacterItems,
  aliasSearchQuery,
  manageLoading,
  manageError,
  showAddAliasModal,
  newAliasCharacter,
  newAliasList,
  addAliasLoading,
  isEditingAlias,
  aliases,
  filteredAliases,
  filteredManageCharacters,
  searchCharacter,
  openAddAliasModal,
  openEditAliasModal,
  addCharacter,
  deleteCharacter,
  handleAliasSubmit,
  executeCommand,
  handleAuth,
  showPortraitConfigModal,
  portraitConfigCharacter,
  portraitConfigServerIds,
  portraitConfigServerId,
  portraitConfigOffsetX,
  portraitConfigOffsetY,
  portraitConfigTargetScale,
  portraitConfigEnableFade,
  portraitConfigFadeStart,
  portraitConfigFetching,
  portraitConfigSaving,
  openPortraitConfigModal,
  fetchPortraitConfigForServerId,
  handlePortraitConfigSubmit,
} = useGameView(gameConfigs.hi3);

const setActiveTab = (value) => {
  activeTab.value = value;
};

const setProfileId = (value) => {
  profileId.value = value;
};

const setServer = (value) => {
  server.value = value;
};

const setCharacterName = (value) => {
  characterName.value = value;
};

const setFloor = (value) => {
  floor.value = value;
};

const setNewCharacterName = (value) => {
  newCharacterName.value = value;
};

const setManageSearchQuery = (value) => {
  manageSearchQuery.value = value;
};

const setShowOnlyMissingAscension = (value) => {
  showOnlyMissingAscension.value = value;
};

const setAliasSearchQuery = (value) => {
  aliasSearchQuery.value = value;
};

const setShowAddAliasModal = (value) => {
  showAddAliasModal.value = value;
};

const setNewAliasCharacter = (value) => {
  newAliasCharacter.value = value;
};

const setNewAliasList = (value) => {
  newAliasList.value = value;
};

const setShowAuthModal = (value) => {
  showAuthModal.value = value;
};

const setAuthPassphrase = (value) => {
  authPassphrase.value = value;
};

const setShowPortraitConfigModal = (value) => {
  showPortraitConfigModal.value = value;
};

const setPortraitConfigOffsetX = (value) => {
  portraitConfigOffsetX.value = value;
};

const setPortraitConfigOffsetY = (value) => {
  portraitConfigOffsetY.value = value;
};

const setPortraitConfigTargetScale = (value) => {
  portraitConfigTargetScale.value = value;
};

const setPortraitConfigEnableFade = (value) => {
  portraitConfigEnableFade.value = value;
};

const setPortraitConfigFadeStart = (value) => {
  portraitConfigFadeStart.value = value;
};

const setPortraitConfigServerId = (value) => {
  portraitConfigServerId.value = value;
  fetchPortraitConfigForServerId(value);
};
</script>

<template>
  <GameViewContainer
    :title="config.title"
    :activeTab="activeTab"
    :tabs="tabs"
    :config="config"
    :profileId="profileId"
    :server="server"
    :characterName="characterName"
    :floor="floor"
    :filteredCharacters="filteredCharacters"
    :loading="loading"
    :error="error"
    :resultImages="resultImages"
    :canManage="canManage"
    :manageLoading="manageLoading"
    :manageError="manageError"
    :newCharacterName="newCharacterName"
    :manageSearchQuery="manageSearchQuery"
    :showOnlyMissingAscension="showOnlyMissingAscension"
    :manageCharacterItems="manageCharacterItems"
    :filteredManageCharacters="filteredManageCharacters"
    :hasStatEdit="false"
    :aliasSearchQuery="aliasSearchQuery"
    :aliases="aliases"
    :filteredAliases="filteredAliases"
    :showAddAliasModal="showAddAliasModal"
    :newAliasCharacter="newAliasCharacter"
    :newAliasList="newAliasList"
    :addAliasLoading="addAliasLoading"
    :isEditingAlias="isEditingAlias"
    :hasCodesManagement="config.hasCodesManagement"
    :codes="[]"
    :filteredCodes="[]"
    :selectedCodes="[]"
    :newCodesInput="''"
    :codesSearchQuery="''"
    :codesLoading="false"
    :showAuthModal="showAuthModal"
    :authProfileId="authProfileId"
    :authPassphrase="authPassphrase"
    :authLoading="authLoading"
    :authError="authError"
    :showPortraitConfigModal="showPortraitConfigModal"
    :portraitConfigCharacter="portraitConfigCharacter"
    :portraitConfigServerIds="portraitConfigServerIds"
    :portraitConfigServerId="portraitConfigServerId"
    :portraitConfigOffsetX="portraitConfigOffsetX"
    :portraitConfigOffsetY="portraitConfigOffsetY"
    :portraitConfigTargetScale="portraitConfigTargetScale"
    :portraitConfigEnableFade="portraitConfigEnableFade"
    :portraitConfigFadeStart="portraitConfigFadeStart"
    :portraitConfigFetching="portraitConfigFetching"
    :portraitConfigSaving="portraitConfigSaving"
    @update:activeTab="setActiveTab"
    @update:profileId="setProfileId"
    @update:server="setServer"
    @update:characterName="setCharacterName"
    @update:floor="setFloor"
    @searchCharacter="searchCharacter"
    @execute="executeCommand"
    @update:newCharacterName="setNewCharacterName"
    @update:manageSearchQuery="setManageSearchQuery"
    @update:showOnlyMissingAscension="setShowOnlyMissingAscension"
    @addCharacter="addCharacter"
    @deleteCharacter="deleteCharacter"
    @update:aliasSearchQuery="setAliasSearchQuery"
    @update:showAddAliasModal="setShowAddAliasModal"
    @update:newAliasCharacter="setNewAliasCharacter"
    @update:newAliasList="setNewAliasList"
    @openAddAliasModal="openAddAliasModal"
    @openEditAliasModal="openEditAliasModal"
    @handleAliasSubmit="handleAliasSubmit"
    @update:selectedCodes="() => {}"
    @update:newCodesInput="() => {}"
    @update:codesSearchQuery="() => {}"
    @confirmAddCodes="() => {}"
    @confirmDeleteCodes="() => {}"
    @update:showAuthModal="setShowAuthModal"
    @update:authPassphrase="setAuthPassphrase"
    @handleAuth="handleAuth"
    @editPortrait="openPortraitConfigModal"
    @update:showPortraitConfigModal="setShowPortraitConfigModal"
    @update:portraitConfigServerId="setPortraitConfigServerId"
    @update:portraitConfigOffsetX="setPortraitConfigOffsetX"
    @update:portraitConfigOffsetY="setPortraitConfigOffsetY"
    @update:portraitConfigTargetScale="setPortraitConfigTargetScale"
    @update:portraitConfigEnableFade="setPortraitConfigEnableFade"
    @update:portraitConfigFadeStart="setPortraitConfigFadeStart"
    @handlePortraitConfigSubmit="handlePortraitConfigSubmit"
  />
</template>
