import { ref, computed, onMounted } from "vue";
import { useApi } from "./useApi";
import { gameMeta } from "../configs/gameMeta";

export const gameColors = Object.fromEntries(
  Object.entries(gameMeta).map(([key, meta]) => [
    key,
    { bg: meta.bgColor, border: meta.borderColor, text: meta.color },
  ]),
);

export const gameLabels = Object.fromEntries(
  Object.entries(gameMeta).map(([key, meta]) => [key, meta.label]),
);

const SUPPORTED_GAMES = Object.keys(gameLabels);

export function useDocs() {
  const { apiFetch, apiFetchJson } = useApi();

  const documents = ref([]);
  const loading = ref(false);
  const error = ref("");
  const searchQuery = ref("");
  const selectedGames = ref([...SUPPORTED_GAMES]);

  const fetchDocuments = async () => {
    loading.value = true;
    error.value = "";
    try {
      const { ok, data } = await apiFetchJson("/docs/list", {
        skipAuthRedirect: true,
      });
      if (ok) {
        documents.value = data;
      } else {
        error.value = data.error || "Failed to fetch documentation";
      }
    } catch (err) {
      if (err._redirected) return;
      error.value = err.message;
    } finally {
      loading.value = false;
    }
  };

  const fetchDocumentDetail = async (id) => {
    try {
      const response = await apiFetch(`/docs/${id}`, {
        skipAuthRedirect: true,
      });
      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        throw new Error(data.error || "Failed to fetch documentation details");
      }
      return await response.json();
    } catch (err) {
      throw err;
    }
  };

  const filteredDocuments = computed(() => {
    let result = documents.value;

    if (searchQuery.value.trim()) {
      const query = searchQuery.value.toLowerCase().trim();
      result = result.filter((doc) => doc.name.toLowerCase().includes(query));
    }

    if (selectedGames.value.length > 0) {
      result = result.filter((doc) => selectedGames.value.includes(doc.game));
    }

    return result;
  });

  const groupedDocuments = computed(() => {
    const groups = {};
    for (const game of SUPPORTED_GAMES) {
      const docs = filteredDocuments.value.filter((doc) => doc.game === game);
      if (docs.length > 0) {
        groups[game] = docs;
      }
    }
    return groups;
  });

  const toggleGame = (game) => {
    const index = selectedGames.value.indexOf(game);
    if (index > -1) {
      if (selectedGames.value.length > 1) {
        selectedGames.value.splice(index, 1);
      }
    } else {
      selectedGames.value.push(game);
    }
  };

  const selectAllGames = () => {
    selectedGames.value = [...SUPPORTED_GAMES];
  };

  onMounted(() => {
    fetchDocuments();
  });

  return {
    documents,
    loading,
    error,
    searchQuery,
    selectedGames,
    filteredDocuments,
    groupedDocuments,
    fetchDocuments,
    fetchDocumentDetail,
    toggleGame,
    selectAllGames,
    gameColors,
    gameLabels,
  };
}
