import { ref, computed } from "vue";
import { useConfirm } from "primevue/useconfirm";
import { useApi } from "../useApi";

export function useCharacterManagement(config, activeTab) {
  const { showErrorToast, showSuccessToast, buildError, apiFetch, apiFetchJson } = useApi();
  const confirm = useConfirm();

  const allCharacters = ref([]);
  const filteredCharacters = ref([]);
  const newCharacterName = ref("");
  const manageSearchQuery = ref("");
  const showOnlyMissingAscension = ref(false);
  const manageLoading = ref(false);
  const manageError = ref("");
  const characterStats = ref({});

  const toStatNumber = (value) => {
    if (typeof value === "number") return value;
    if (value === null || value === undefined || value === "") return 0;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  };

  const fetchCharacters = async () => {
    try {
      const { ok, data, status } = await apiFetchJson(
        `/characters/list?game=${config.id}`,
      );
      if (ok) {
        allCharacters.value = data.sort();
      } else {
        showErrorToast(data.error || "Failed to fetch characters", status);
      }
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    }
  };

  const fetchCharacterStats = async () => {
    if (!config.hasStatEdit) return;

    try {
      const { ok, data, status } = await apiFetchJson(
        `/characters/stat?game=${config.id}`,
      );
      if (ok) {
        const normalizedStats = Object.fromEntries(
          Object.entries(data || {}).map(([name, stat]) => [
            name,
            {
              baseVal: toStatNumber(stat?.baseVal ?? stat?.BaseVal),
              maxAscVal: toStatNumber(stat?.maxAscVal ?? stat?.MaxAscVal),
            },
          ]),
        );
        characterStats.value = normalizedStats;
      } else {
        showErrorToast(data.error || "Failed to fetch character stats", status);
      }
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    }
  };

  const searchCharacter = (event) => {
    const query = event.query.toLowerCase();
    filteredCharacters.value = allCharacters.value.filter((char) =>
      char.toLowerCase().includes(query),
    );
  };

  const filteredManageCharacters = computed(() => {
    const query = manageSearchQuery.value.toLowerCase();

    return allCharacters.value.filter((char) => {
      const matchesQuery = !query || char.toLowerCase().includes(query);

      if (!matchesQuery) {
        return false;
      }

      if (!showOnlyMissingAscension.value) {
        return true;
      }

      const stat = characterStats.value[char];
      return toStatNumber(stat?.maxAscVal) === 0;
    });
  });

  const manageCharacterItems = computed(() =>
    filteredManageCharacters.value.map((name) => {
      const stat = characterStats.value[name] || { baseVal: 0, maxAscVal: 0 };
      return {
        name,
        baseVal: toStatNumber(stat.baseVal),
        maxAscVal: toStatNumber(stat.maxAscVal),
      };
    }),
  );

  const addCharacter = async () => {
    if (!newCharacterName.value) return;
    manageLoading.value = true;
    manageError.value = "";
    try {
      const response = await apiFetch(
        `/characters/add?game=${config.id}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ characters: [newCharacterName.value] }),
        },
      );
      if (!response.ok) {
        const data = await response.json();
        throw buildError(data.error || "Failed to add character", response.status);
      }
      const addedCharacterName = newCharacterName.value;
      newCharacterName.value = "";
      await fetchCharacters();
      if (config.hasStatEdit) {
        characterStats.value[addedCharacterName] = { baseVal: 0, maxAscVal: 0 };
      }
    } catch (err) {
      if (err._redirected) return;
      manageError.value = err.message;
      showErrorToast(err.message, err.status);
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
      const response = await apiFetch(
        `/characters/delete?game=${config.id}&character=${encodeURIComponent(name)}`,
        { method: "DELETE" },
      );
      if (!response.ok) {
        const data = await response.json();
        throw buildError(data.error || "Failed to delete character", response.status);
      }
      await fetchCharacters();
      if (config.hasStatEdit) {
        delete characterStats.value[name];
      }
    } catch (err) {
      if (err._redirected) return;
      manageError.value = err.message;
      showErrorToast(err.message, err.status);
    } finally {
      manageLoading.value = false;
    }
  };

  const showEditStatModal = ref(false);
  const editStatCharacter = ref("");
  const editStatBase = ref(null);
  const editStatMax = ref(null);
  const editStatFetching = ref(false);
  const editStatLoading = ref(false);

  const openEditStatModal = async (char) => {
    editStatCharacter.value = char;
    editStatBase.value = null;
    editStatMax.value = null;
    showEditStatModal.value = true;
    editStatFetching.value = true;

    try {
      const { ok, data, status } = await apiFetchJson(
        `/characters/stat?game=${config.id}&character=${encodeURIComponent(char)}`,
      );
      if (ok) {
        editStatBase.value = data.baseVal ?? data.BaseVal;
        editStatMax.value = data.maxAscVal ?? data.MaxAscVal;
        characterStats.value[char] = {
          baseVal: toStatNumber(data.baseVal ?? data.BaseVal),
          maxAscVal: toStatNumber(data.maxAscVal ?? data.MaxAscVal),
        };
      } else {
        showErrorToast(data.error || "Failed to fetch character stats", status);
      }
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      editStatFetching.value = false;
    }
  };

  const handleStatSubmit = async () => {
    editStatLoading.value = true;
    try {
      const response = await apiFetch(
        `/characters/stat?game=${config.id}&character=${encodeURIComponent(editStatCharacter.value)}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            baseVal: editStatBase.value,
            maxAscVal: editStatMax.value,
          }),
        },
      );

      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        throw buildError(data.error || "Failed to update character stats", response.status);
      }

      showEditStatModal.value = false;
      characterStats.value[editStatCharacter.value] = {
        baseVal: toStatNumber(editStatBase.value),
        maxAscVal: toStatNumber(editStatMax.value),
      };
      showSuccessToast("Character stats updated successfully");
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      editStatLoading.value = false;
    }
  };

  return {
    allCharacters,
    filteredCharacters,
    newCharacterName,
    manageSearchQuery,
    showOnlyMissingAscension,
    manageLoading,
    manageError,
    characterStats,
    filteredManageCharacters,
    manageCharacterItems,
    showEditStatModal,
    editStatCharacter,
    editStatBase,
    editStatMax,
    editStatFetching,
    editStatLoading,
    fetchCharacters,
    fetchCharacterStats,
    searchCharacter,
    addCharacter,
    deleteCharacter,
    openEditStatModal,
    handleStatSubmit,
    toStatNumber,
  };
}