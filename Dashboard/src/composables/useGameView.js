import { ref, watch, onMounted, computed } from "vue";
import { useRouter } from "vue-router";
import { useConfirm } from "primevue/useconfirm";
import { useToast } from "primevue/usetoast";

export function useGameView(config) {
  const router = useRouter();
  const confirm = useConfirm();
  const toast = useToast();

  const activeTab = ref(config.tabs[0]?.id || "character");
  const loading = ref(false);
  const error = ref("");
  const resultImages = ref({});
  const showAuthModal = ref(false);

  const showErrorToast = (message, status) => {
    toast.add({
      severity: "error",
      summary: "Error",
      detail: `${message} (Code: ${status ?? "N/A"})`,
      life: 5000,
    });
  };

  const buildError = (message, status) => {
    const err = new Error(message);
    err.status = status;
    return err;
  };

  const profileId = ref(1);
  const server = ref(config.servers[0]?.value || "America");
  const characterName = ref("");
  const floor = ref(config.tabs.find((t) => t.hasFloorInput)?.floorMin || 12);

  const allCharacters = ref([]);
  const filteredCharacters = ref([]);
  const aliases = ref([]);

  const fetchCharacters = async () => {
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/characters/list?game=${config.id}`,
        { credentials: "include" },
      );
      if (response.status === 401) {
        router.push("/login");
        return;
      }
      if (response.ok) {
        const data = await response.json();
        allCharacters.value = data.sort();
      } else {
        const data = await response.json().catch(() => ({}));
        showErrorToast(
          data.error || "Failed to fetch characters",
          response.status,
        );
      }
    } catch (err) {
      showErrorToast(err.message, err.status);
    }
  };

  const fetchAliases = async () => {
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/alias/list?game=${config.id}`,
        { credentials: "include" },
      );
      if (response.status === 401) {
        router.push("/login");
        return;
      }
      if (response.ok) {
        const data = await response.json();
        aliases.value = Object.entries(data).map(([name, aliasList]) => ({
          name,
          aliases: aliasList,
        }));
      } else {
        const data = await response.json().catch(() => ({}));
        showErrorToast(
          data.error || "Failed to fetch aliases",
          response.status,
        );
      }
    } catch (err) {
      showErrorToast(err.message, err.status);
    }
  };

  const searchCharacter = (event) => {
    const query = event.query.toLowerCase();
    filteredCharacters.value = allCharacters.value.filter((char) =>
      char.toLowerCase().includes(query),
    );
  };

  onMounted(() => {
    fetchCharacters();
    if (config.hasStatEdit) {
      fetchCharacterStats();
    }
  });

  const authProfileId = ref("");
  const authPassphrase = ref("");
  const authLoading = ref(false);
  const authError = ref("");

  const user = JSON.parse(localStorage.getItem("mehrak_user") || "{}");
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

  const newCharacterName = ref("");
  const manageSearchQuery = ref("");
  const showOnlyMissingAscension = ref(false);
  const aliasSearchQuery = ref("");
  const manageLoading = ref(false);
  const manageError = ref("");
  const characterStats = ref({});

  const toStatNumber = (value) => {
    if (typeof value === "number") return value;
    if (value === null || value === undefined || value === "") return 0;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  };

  const fetchCharacterStats = async () => {
    if (!config.hasStatEdit) return;

    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/characters/stat?game=${config.id}`,
        { credentials: "include" },
      );

      if (response.status === 401) {
        router.push("/login");
        return;
      }

      if (response.ok) {
        const data = await response.json();
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
        const data = await response.json().catch(() => ({}));
        showErrorToast(
          data.error || "Failed to fetch character stats",
          response.status,
        );
      }
    } catch (err) {
      showErrorToast(err.message, err.status);
    }
  };

  const showAddAliasModal = ref(false);
  const newAliasCharacter = ref("");
  const newAliasList = ref("");
  const addAliasLoading = ref(false);
  const isEditingAlias = ref(false);
  const originalAliases = ref([]);

  const codes = ref([]);
  const selectedCodes = ref([]);
  const newCodesInput = ref("");
  const codesSearchQuery = ref("");
  const codesLoading = ref(false);

  const openAddAliasModal = () => {
    isEditingAlias.value = false;
    newAliasCharacter.value = "";
    newAliasList.value = "";
    originalAliases.value = [];
    showAddAliasModal.value = true;
  };

  const openEditAliasModal = (data) => {
    isEditingAlias.value = true;
    newAliasCharacter.value = data.name;
    newAliasList.value = data.aliases.join(", ");
    originalAliases.value = [...data.aliases];
    showAddAliasModal.value = true;
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

  const filteredAliases = computed(() => {
    if (!aliasSearchQuery.value) return aliases.value;
    const query = aliasSearchQuery.value.toLowerCase();
    return aliases.value.filter(
      (item) =>
        item.name.toLowerCase().includes(query) ||
        item.aliases.some((alias) => alias.toLowerCase().includes(query)),
    );
  });

  const filteredCodes = computed(() => {
    if (!codesSearchQuery.value) return codes.value;
    const query = codesSearchQuery.value.toLowerCase();
    return codes.value.filter((c) => c.code.toLowerCase().includes(query));
  });

  const addCharacter = async () => {
    if (!newCharacterName.value) return;
    manageLoading.value = true;
    manageError.value = "";
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/characters/add?game=${config.id}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({ characters: [newCharacterName.value] }),
        },
      );
      if (response.status === 401) {
        router.push("/login");
        return;
      }
      if (!response.ok) {
        const data = await response.json();
        throw buildError(
          data.error || "Failed to add character",
          response.status,
        );
      }
      const addedCharacterName = newCharacterName.value;
      newCharacterName.value = "";
      await fetchCharacters();
      if (config.hasStatEdit) {
        characterStats.value[addedCharacterName] = { baseVal: 0, maxAscVal: 0 };
      }
    } catch (err) {
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
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/characters/delete?game=${config.id}&character=${encodeURIComponent(name)}`,
        {
          method: "DELETE",
          credentials: "include",
        },
      );
      if (response.status === 401) {
        router.push("/login");
        return;
      }
      if (!response.ok) {
        const data = await response.json();
        throw buildError(
          data.error || "Failed to delete character",
          response.status,
        );
      }
      await fetchCharacters();
      if (config.hasStatEdit) {
        delete characterStats.value[name];
      }
    } catch (err) {
      manageError.value = err.message;
      showErrorToast(err.message, err.status);
    } finally {
      manageLoading.value = false;
    }
  };

  const handleAliasSubmit = async () => {
    if (!newAliasCharacter.value || !newAliasList.value) return;
    addAliasLoading.value = true;

    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const currentAliasesArray = newAliasList.value
        .split(",")
        .map((s) => s.trim())
        .filter((s) => s.length > 0);

      if (isEditingAlias.value) {
        const addedAliases = currentAliasesArray.filter(
          (a) => !originalAliases.value.includes(a),
        );
        const removedAliases = originalAliases.value.filter(
          (a) => !currentAliasesArray.includes(a),
        );

        const promises = [];

        if (addedAliases.length > 0) {
          promises.push(
            fetch(`${backendUrl}/alias/add?game=${config.id}`, {
              method: "PATCH",
              headers: { "Content-Type": "application/json" },
              credentials: "include",
              body: JSON.stringify({
                character: newAliasCharacter.value,
                aliases: addedAliases,
              }),
            }).then(async (res) => {
              if (res.status === 401) {
                router.push("/login");
                throw buildError("Unauthorized", res.status);
              }
              if (!res.ok) {
                const data = await res.json();
                throw buildError(
                  data.error || "Failed to add new aliases",
                  res.status,
                );
              }
            }),
          );
        }

        for (const alias of removedAliases) {
          promises.push(
            fetch(
              `${backendUrl}/alias/delete?game=${config.id}&alias=${encodeURIComponent(alias)}`,
              {
                method: "DELETE",
                credentials: "include",
              },
            ).then(async (res) => {
              if (res.status === 401) {
                router.push("/login");
                throw buildError("Unauthorized", res.status);
              }
              if (!res.ok) {
                const data = await res.json();
                throw buildError(
                  data.error || `Failed to delete alias ${alias}`,
                  res.status,
                );
              }
            }),
          );
        }

        await Promise.all(promises);
      } else {
        const response = await fetch(
          `${backendUrl}/alias/add?game=${config.id}`,
          {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify({
              character: newAliasCharacter.value,
              aliases: currentAliasesArray,
            }),
          },
        );

        if (response.status === 401) {
          router.push("/login");
          return;
        }

        if (!response.ok) {
          const data = await response.json();
          throw buildError(
            data.error || "Failed to add aliases",
            response.status,
          );
        }
      }

      showAddAliasModal.value = false;
      newAliasCharacter.value = "";
      newAliasList.value = "";
      originalAliases.value = [];
      await fetchAliases();
      toast.add({
        severity: "success",
        summary: "Success",
        detail: isEditingAlias.value
          ? "Aliases updated successfully"
          : "Aliases added successfully",
        life: 3000,
      });
    } catch (err) {
      showErrorToast(err.message, err.status);
    } finally {
      addAliasLoading.value = false;
    }
  };

  const fetchCodes = async () => {
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/codes/list?game=${config.id}`,
        { credentials: "include" },
      );
      if (response.status === 401) {
        router.push("/login");
        return;
      }
      if (response.ok) {
        const data = await response.json();
        codes.value = data.codes.map((c) => ({ code: c }));
      } else {
        const data = await response.json().catch(() => ({}));
        showErrorToast(data.error || "Failed to fetch codes", response.status);
      }
    } catch (err) {
      showErrorToast(err.message, err.status);
    }
  };

  const confirmAddCodes = () => {
    if (!newCodesInput.value) return;
    confirm.require({
      message: "Are you sure you want to add these codes?",
      header: "Confirm Add",
      icon: "pi pi-exclamation-triangle",
      accept: executeAddCodes,
    });
  };

  const executeAddCodes = async () => {
    codesLoading.value = true;
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const codesToAdd = newCodesInput.value
        .split(",")
        .map((c) => c.trim())
        .filter((c) => c);

      const response = await fetch(
        `${backendUrl}/codes/add?game=${config.id}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({ Codes: codesToAdd }),
        },
      );

      if (response.status === 401) {
        router.push("/login");
        return;
      }

      if (!response.ok) {
        const data = await response.json();
        throw buildError(data.error || "Failed to add codes", response.status);
      }

      newCodesInput.value = "";
      await fetchCodes();
      toast.add({
        severity: "success",
        summary: "Success",
        detail: "Codes added successfully",
        life: 3000,
      });
    } catch (err) {
      showErrorToast(err.message, err.status);
    } finally {
      codesLoading.value = false;
    }
  };

  const confirmDeleteCodes = (codesList) => {
    confirm.require({
      message: `Are you sure you want to delete ${codesList.length} code(s)?`,
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
      accept: () => executeDeleteCodes(codesList),
    });
  };

  const executeDeleteCodes = async (codesList) => {
    codesLoading.value = true;
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const params = new URLSearchParams();
      params.append("game", config.id);
      codesList.forEach((c) => params.append("codes", c));
      const response = await fetch(
        `${backendUrl}/codes/remove?${params.toString()}`,
        {
          method: "DELETE",
          credentials: "include",
        },
      );

      if (response.status === 401) {
        router.push("/login");
        return;
      }

      if (!response.ok) {
        const data = await response.json();
        throw buildError(
          data.error || "Failed to delete codes",
          response.status,
        );
      }

      selectedCodes.value = [];
      await fetchCodes();
      toast.add({
        severity: "success",
        summary: "Success",
        detail: "Codes deleted successfully",
        life: 3000,
      });
    } catch (err) {
      showErrorToast(err.message, err.status);
    } finally {
      codesLoading.value = false;
    }
  };

  watch(activeTab, (newTab) => {
    error.value = "";
    if (newTab === "aliases" && canManage) {
      fetchAliases();
    } else if (newTab === "codes" && canManage) {
      fetchCodes();
    }
  });

  const executeCommand = async () => {
    loading.value = true;
    error.value = "";
    resultImages.value[activeTab.value] = "";

    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      let endpoint = `${config.endpoint}/${activeTab.value}`;
      let payload = {
        profileId: Number(profileId.value),
        server: server.value,
      };

      const currentTabConfig = config.tabs.find(
        (t) => t.id === activeTab.value,
      );
      if (currentTabConfig?.hasCharacterInput) {
        if (config.id === "HonkaiImpact3") {
          payload.battlesuit = characterName.value;
        } else {
          payload.character = characterName.value;
        }
      }
      if (currentTabConfig?.hasFloorInput) {
        payload.floor = Number(floor.value);
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
        throw buildError(data.error || "Command failed", response.status);
      }

      if (data.storageFileName) {
        resultImages.value[activeTab.value] =
          `${backendUrl}/attachments/${data.storageFileName}`;
      }
    } catch (err) {
      error.value = err.message;
      showErrorToast(err.message, err.status);
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
        throw buildError(
          data.error || "Authentication failed",
          response.status,
        );
      }

      showAuthModal.value = false;
      authPassphrase.value = "";
      executeCommand();
    } catch (err) {
      authError.value = err.message;
      showErrorToast(err.message, err.status);
    } finally {
      authLoading.value = false;
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
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/characters/stat?game=${config.id}&character=${encodeURIComponent(char)}`,
        { credentials: "include" },
      );
      if (response.status === 401) {
        router.push("/login");
        return;
      }
      if (response.ok) {
        const data = await response.json();
        editStatBase.value = data.baseVal;
        editStatMax.value = data.maxAscVal;
        characterStats.value[char] = {
          baseVal: toStatNumber(data.baseVal),
          maxAscVal: toStatNumber(data.maxAscVal),
        };
      } else {
        const data = await response.json().catch(() => ({}));
        showErrorToast(
          data.error || "Failed to fetch character stats",
          response.status,
        );
      }
    } catch (err) {
      showErrorToast(err.message, err.status);
    } finally {
      editStatFetching.value = false;
    }
  };

  const handleStatSubmit = async () => {
    editStatLoading.value = true;
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(
        `${backendUrl}/characters/stat?game=${config.id}&character=${encodeURIComponent(editStatCharacter.value)}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({
            baseVal: editStatBase.value,
            maxAscVal: editStatMax.value,
          }),
        },
      );

      if (response.status === 401) {
        router.push("/login");
        return;
      }

      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        throw buildError(
          data.error || "Failed to update character stats",
          response.status,
        );
      }

      showEditStatModal.value = false;
      characterStats.value[editStatCharacter.value] = {
        baseVal: toStatNumber(editStatBase.value),
        maxAscVal: toStatNumber(editStatMax.value),
      };
      toast.add({
        severity: "success",
        summary: "Success",
        detail: "Character stats updated successfully",
        life: 3000,
      });
    } catch (err) {
      showErrorToast(err.message, err.status);
    } finally {
      editStatLoading.value = false;
    }
  };

  return {
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
    aliases,
    authProfileId,
    authPassphrase,
    authLoading,
    authError,
    canManage,
    tabs,
    newCharacterName,
    manageSearchQuery,
    showOnlyMissingAscension,
    aliasSearchQuery,
    manageLoading,
    manageError,
    showAddAliasModal,
    newAliasCharacter,
    newAliasList,
    addAliasLoading,
    isEditingAlias,
    originalAliases,
    codes,
    selectedCodes,
    newCodesInput,
    codesSearchQuery,
    codesLoading,
    characterStats,
    showEditStatModal,
    editStatCharacter,
    editStatBase,
    editStatMax,
    editStatFetching,
    editStatLoading,
    fetchCharacters,
    fetchAliases,
    searchCharacter,
    openAddAliasModal,
    openEditAliasModal,
    filteredManageCharacters,
    manageCharacterItems,
    filteredAliases,
    filteredCodes,
    addCharacter,
    deleteCharacter,
    handleAliasSubmit,
    fetchCodes,
    confirmAddCodes,
    confirmDeleteCodes,
    executeCommand,
    handleAuth,
    openEditStatModal,
    handleStatSubmit,
    fetchCharacterStats,
  };
}
