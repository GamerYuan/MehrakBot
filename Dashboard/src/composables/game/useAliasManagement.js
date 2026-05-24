import { ref, computed } from "vue";
import { useApi } from "../useApi";

export function useAliasManagement(config, activeTab) {
  const { showErrorToast, showSuccessToast, buildError, apiFetch, apiFetchJson } = useApi();

  const aliases = ref([]);
  const aliasSearchQuery = ref("");
  const showAddAliasModal = ref(false);
  const newAliasCharacter = ref("");
  const newAliasList = ref("");
  const addAliasLoading = ref(false);
  const isEditingAlias = ref(false);
  const originalAliases = ref([]);

  const fetchAliases = async () => {
    try {
      const { ok, data, status } = await apiFetchJson(
        `/alias/list?game=${config.id}`,
      );
      if (ok) {
        aliases.value = Object.entries(data).map(([name, aliasList]) => ({
          name,
          aliases: aliasList,
        }));
      } else {
        showErrorToast(data.error || "Failed to fetch aliases", status);
      }
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    }
  };

  const filteredAliases = computed(() => {
    if (!aliasSearchQuery.value) return aliases.value;
    const query = aliasSearchQuery.value.toLowerCase();
    return aliases.value.filter(
      (item) =>
        item.name.toLowerCase().includes(query) ||
        item.aliases.some((alias) => alias.toLowerCase().includes(query)),
    );
  });

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

  const handleAliasSubmit = async () => {
    if (!newAliasCharacter.value || !newAliasList.value) return;
    addAliasLoading.value = true;

    try {
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

        if (addedAliases.length > 0) {
          const addRes = await apiFetch(`/alias/add?game=${config.id}`, {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              character: newAliasCharacter.value,
              aliases: addedAliases,
            }),
          });
          if (!addRes.ok) {
            const data = await addRes.json().catch(() => ({}));
            throw buildError(data.error || "Failed to add new aliases", addRes.status);
          }
        }

        for (const alias of removedAliases) {
          const delRes = await apiFetch(
            `/alias/delete?game=${config.id}&alias=${encodeURIComponent(alias)}`,
            { method: "DELETE" },
          );
          if (!delRes.ok) {
            const data = await delRes.json().catch(() => ({}));
            throw buildError(data.error || `Failed to delete alias ${alias}`, delRes.status);
          }
        }
      } else {
        const response = await apiFetch(`/alias/add?game=${config.id}`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            character: newAliasCharacter.value,
            aliases: currentAliasesArray,
          }),
        });

        if (!response.ok) {
          const data = await response.json();
          throw buildError(data.error || "Failed to add aliases", response.status);
        }
      }

      showAddAliasModal.value = false;
      newAliasCharacter.value = "";
      newAliasList.value = "";
      originalAliases.value = [];
      await fetchAliases();
      showSuccessToast(isEditingAlias.value ? "Aliases updated successfully" : "Aliases added successfully");
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      addAliasLoading.value = false;
    }
  };

  return {
    aliases,
    aliasSearchQuery,
    showAddAliasModal,
    newAliasCharacter,
    newAliasList,
    addAliasLoading,
    isEditingAlias,
    filteredAliases,
    openAddAliasModal,
    openEditAliasModal,
    handleAliasSubmit,
    fetchAliases,
  };
}