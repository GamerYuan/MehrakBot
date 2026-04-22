import { ref } from "vue";
import { gameConfigs } from "../configs/gameConfigs";

export function useAlias() {
  const aliases = ref({});
  const loading = ref(false);
  const error = ref(null);
  const searchQuery = ref("");

  const fetchAllAliases = async () => {
    loading.value = true;
    error.value = null;
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const games = Object.values(gameConfigs).map((c) => c.id);

    try {
      const promises = games.map(async (game) => {
        const response = await fetch(`${backendUrl}/alias/list?game=${game}`);
        if (!response.ok) return { game, data: {} };
        const data = await response.json();
        return { game, data };
      });

      const results = await Promise.all(promises);
      const newAliases = {};
      results.forEach(({ game, data }) => {
        newAliases[game] = Object.entries(data).map(([name, aliasList]) => {
          aliasList.sort();
          return {
            name,
            aliases: aliasList,
          };
        });
      });
      aliases.value = newAliases;
    } catch (err) {
      error.value = err.message || "Failed to fetch aliases";
    } finally {
      loading.value = false;
    }
  };

  return { aliases, loading, error, searchQuery, fetchAllAliases };
}
