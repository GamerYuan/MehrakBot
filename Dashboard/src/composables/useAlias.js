import { ref } from "vue";
import { gameConfigs } from "../configs/gameConfigs";
import { useApi } from "./useApi";

export function useAlias() {
  const { apiFetchJson } = useApi();

  const aliases = ref({});
  const loading = ref(false);
  const error = ref(null);
  const searchQuery = ref("");

  const fetchAllAliases = async () => {
    loading.value = true;
    error.value = null;
    const games = Object.values(gameConfigs).map((c) => c.id);

    try {
      const promises = games.map(async (game) => {
        const { ok, data } = await apiFetchJson(`/alias/list?game=${game}`, {
          skipAuthRedirect: true,
        });
        if (ok) {
          return { game, data };
        }
        return { game, data: {} };
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
      if (err._redirected) return;
      error.value = err.message || "Failed to fetch aliases";
    } finally {
      loading.value = false;
    }
  };

  return { aliases, loading, error, searchQuery, fetchAllAliases };
}
