import { ref } from "vue";
import { useApi } from "../useApi";

export function usePortraitConfig(config) {
  const { showErrorToast, showSuccessToast, buildError, apiFetch, apiFetchJson } = useApi();

  const showPortraitConfigModal = ref(false);
  const portraitConfigCharacter = ref("");
  const portraitConfigServerIds = ref([]);
  const portraitConfigServerId = ref(null);
  const portraitConfigOffsetX = ref(null);
  const portraitConfigOffsetY = ref(null);
  const portraitConfigTargetScale = ref(null);
  const portraitConfigEnableFade = ref(null);
  const portraitConfigFadeStart = ref(null);
  const portraitConfigFetching = ref(false);
  const portraitConfigSaving = ref(false);

  const fetchPortraitConfigForServerId = async (serverId) => {
    portraitConfigServerId.value = serverId;
    portraitConfigOffsetX.value = null;
    portraitConfigOffsetY.value = null;
    portraitConfigTargetScale.value = null;
    portraitConfigEnableFade.value = null;
    portraitConfigFadeStart.value = null;
    portraitConfigFetching.value = true;

    try {
      const { ok, data } = await apiFetchJson(
        `/portraits/config?game=${config.id}&serverId=${serverId}`,
      );
      if (ok) {
        portraitConfigOffsetX.value = data.offsetX ?? 0;
        portraitConfigOffsetY.value = data.offsetY ?? 0;
        portraitConfigTargetScale.value = data.targetScale ?? 1;
        portraitConfigEnableFade.value = data.enableGradientFade ?? false;
        portraitConfigFadeStart.value = data.gradientFadeStart ?? 0.75;
      }
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      portraitConfigFetching.value = false;
    }
  };

  const openPortraitConfigModal = async (char) => {
    portraitConfigCharacter.value = char;
    portraitConfigServerIds.value = [];
    portraitConfigServerId.value = null;
    portraitConfigOffsetX.value = 0;
    portraitConfigOffsetY.value = 0;
    portraitConfigTargetScale.value = 1;
    portraitConfigEnableFade.value = false;
    portraitConfigFadeStart.value = 0.75;
    showPortraitConfigModal.value = true;
    portraitConfigFetching.value = true;

    try {
      const listResponse = await apiFetch(
        `/portraits/list?game=${config.id}&character=${encodeURIComponent(char)}`,
      );
      if (listResponse.ok) {
        portraitConfigServerIds.value = await listResponse.json();
      }

      if (portraitConfigServerIds.value.length > 0) {
        await fetchPortraitConfigForServerId(portraitConfigServerIds.value[0]);
      }
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      portraitConfigFetching.value = false;
    }
  };

  const handlePortraitConfigSubmit = async () => {
    if (!portraitConfigServerId.value) {
      showErrorToast("No portrait selected. Please select a portrait first.", 400);
      return;
    }
    portraitConfigSaving.value = true;
    try {
      const response = await apiFetch(
        `/portraits/config?game=${config.id}&serverId=${portraitConfigServerId.value}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            offsetX: portraitConfigOffsetX.value,
            offsetY: portraitConfigOffsetY.value,
            targetScale: portraitConfigTargetScale.value,
            enableGradientFade: portraitConfigEnableFade.value,
            gradientFadeStart: portraitConfigFadeStart.value,
          }),
        },
      );

      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        throw buildError(data.error || "Failed to update portrait config", response.status);
      }

      showPortraitConfigModal.value = false;
      showSuccessToast("Portrait config updated successfully");
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      portraitConfigSaving.value = false;
    }
  };

  return {
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
  };
}