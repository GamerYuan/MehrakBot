import { ref, computed, onMounted } from "vue";

export const gameColors = {
  Genshin: {
    bg: "rgba(255, 215, 0, 0.15)",
    border: "rgba(255, 215, 0, 0.4)",
    text: "#FFD700",
  },
  HonkaiStarRail: {
    bg: "rgba(0, 212, 255, 0.15)",
    border: "rgba(0, 212, 255, 0.4)",
    text: "#00D4FF",
  },
  ZenlessZoneZero: {
    bg: "rgba(255, 107, 0, 0.15)",
    border: "rgba(255, 107, 0, 0.4)",
    text: "#FF6B00",
  },
  HonkaiImpact3: {
    bg: "rgba(255, 105, 180, 0.15)",
    border: "rgba(255, 105, 180, 0.4)",
    text: "#FF69B4",
  },
  TearsOfThemis: {
    bg: "rgba(138, 43, 226, 0.15)",
    border: "rgba(138, 43, 226, 0.4)",
    text: "#8A2BE2",
  },
  Unsupported: {
    bg: "rgba(136, 136, 136, 0.15)",
    border: "rgba(136, 136, 136, 0.4)",
    text: "#888888",
  },
};

export const gameLabels = {
  Genshin: "Genshin Impact",
  HonkaiStarRail: "Honkai: Star Rail",
  ZenlessZoneZero: "Zenless Zone Zero",
  HonkaiImpact3: "Honkai Impact 3rd",
  TearsOfThemis: "Tears of Themis",
  Unsupported: "Miscellaneous",
};

const SUPPORTED_GAMES = Object.keys(gameLabels);

export function useDocs() {
  const documents = ref([]);
  const loading = ref(false);
  const error = ref("");
  const searchQuery = ref("");
  const selectedGames = ref([...SUPPORTED_GAMES]);

  const fetchDocuments = async () => {
    loading.value = true;
    error.value = "";
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(`${backendUrl}/docs/list`, {
        credentials: "include",
      });
      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        throw new Error(data.error || "Failed to fetch documentation");
      }
      const data = await response.json();
      documents.value = data;
    } catch (err) {
      error.value = err.message;
    } finally {
      loading.value = false;
    }
  };

  const fetchDocumentDetail = async (id) => {
    try {
      const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
      const response = await fetch(`${backendUrl}/docs/${id}`, {
        credentials: "include",
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
