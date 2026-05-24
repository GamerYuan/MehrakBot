import { ref, computed, watch, onMounted } from "vue";
import { useApi } from "./useApi";
import { useCommandExecution } from "./game/useCommandExecution";
import { useCharacterManagement } from "./game/useCharacterManagement";
import { useAliasManagement } from "./game/useAliasManagement";
import { useCodesManagement } from "./game/useCodesManagement";
import { usePortraitConfig } from "./game/usePortraitConfig";

export function useGameView(config) {
  const { getStoredUser } = useApi();

  const activeTab = ref(config.tabs[0]?.id || "character");

  const command = useCommandExecution(config, activeTab);
  const characters = useCharacterManagement(config, activeTab);
  const aliases = useAliasManagement(config, activeTab);
  const portrait = usePortraitConfig(config);

  const codes = useCodesManagement(config, activeTab);

  const user = getStoredUser();
  const canManage =
    user.isSuperAdmin ||
    (user.gameWritePermissions &&
      user.gameWritePermissions.includes(config.permission));

  const tabs = computed(() => {
    const t = [...config.tabs];
    if (canManage) {
      t.push({ id: "manage", name: "Manage Characters" });
      t.push({ id: "aliases", name: "Manage Aliases" });
      if (config.hasCodesManagement) {
        t.push({ id: "codes", name: "Manage Codes" });
      }
    }
    return t;
  });

  onMounted(() => {
    characters.fetchCharacters();
    if (config.hasStatEdit) {
      characters.fetchCharacterStats();
    }
  });

  watch(activeTab, (newTab) => {
    command.error.value = "";
    if (newTab === "aliases" && canManage) {
      aliases.fetchAliases();
    } else if (newTab === "codes" && canManage && config.hasCodesManagement) {
      codes.fetchCodes();
    }
  });

  const codeRefs = {
    codes: codes.codes,
    selectedCodes: codes.selectedCodes,
    newCodesInput: codes.newCodesInput,
    codesSearchQuery: codes.codesSearchQuery,
    codesLoading: codes.codesLoading,
    filteredCodes: codes.filteredCodes,
    confirmAddCodes: codes.confirmAddCodes,
    confirmDeleteCodes: codes.confirmDeleteCodes,
  };

  return {
    config,
    activeTab,
    tabs,
    canManage,

    ...command,
    ...characters,
    ...aliases,
    ...portrait,
    ...codeRefs,
  };
}
