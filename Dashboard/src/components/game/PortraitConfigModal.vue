<script setup>
import { ref, watch, computed, onUnmounted } from "vue";
import { useGameViewInject } from "../../composables/game/injectKey";
import { useApi } from "../../composables/useApi";
import Dialog from "primevue/dialog";
import InputNumber from "primevue/inputnumber";
import Checkbox from "primevue/checkbox";
import Select from "primevue/select";
import Button from "primevue/button";
import { useToast } from "primevue/usetoast";

const gv = useGameViewInject();
const { apiFetch } = useApi();
const toast = useToast();

const canvasRef = ref(null);
const portraitImage = ref(null);
const portraitBlobUrl = ref(null);
const portraitError = ref(false);
const portraitLoading = ref(false);
const bgLoaded = ref(false);
const portraitLoaded = ref(false);
let renderTimeout;

const revokePortraitBlob = () => {
  if (portraitBlobUrl.value) {
    URL.revokeObjectURL(portraitBlobUrl.value);
    portraitBlobUrl.value = null;
  }
};

const cleanupPortrait = () => {
  revokePortraitBlob();
  portraitImage.value = null;
  portraitError.value = false;
  portraitLoaded.value = false;
};

const bgFileByGameId = {
  Genshin: "genshin_portrait_bg.webp",
  HonkaiImpact3: "hi3_portrait_bg.webp",
  HonkaiStarRail: "hsr_portrait_bg.webp",
  ZenlessZoneZero: "zzz_portrait_bg.webp",
};

const bgUrl = computed(() => {
  const file = bgFileByGameId[gv.config.id];
  return file ? `/${file}` : "";
});

const serverIdOptions = computed(() =>
  (gv.portraitConfigServerIds || []).map((id) => ({ label: `ID: ${id}`, value: id })),
);

const backgroundImage = ref(null);

const loadBackground = () => {
  if (!bgUrl.value) return;
  backgroundImage.value = new Image();
  backgroundImage.value.onload = () => {
    bgLoaded.value = true;
    renderPreview();
  };
  backgroundImage.value.src = bgUrl.value;
};

const loadPortrait = async () => {
  if (!gv.config.id || !gv.portraitConfigServerId) return;
  portraitLoading.value = true;
  portraitError.value = false;
  portraitLoaded.value = false;

  try {
    const response = await apiFetch(
      `/portraits/image?game=${encodeURIComponent(gv.config.id)}&serverId=${gv.portraitConfigServerId}`,
    );

    if (response.status === 404) {
      portraitError.value = true;
      toast.add({
        severity: "error",
        summary: "Image Not Found",
        detail: `Portrait image for ${gv.portraitConfigCharacter} (ID: ${gv.portraitConfigServerId}) not found, please generate an image with this character in the Characters tab and try again`,
        life: 5000,
      });
      return;
    }

    if (!response.ok) {
      portraitError.value = true;
      return;
    }

    revokePortraitBlob();
    const blob = await response.blob();
    portraitBlobUrl.value = URL.createObjectURL(blob);
    portraitImage.value = new Image();
    portraitImage.value.onload = () => {
      portraitLoaded.value = true;
      renderPreview();
    };
    portraitImage.value.src = portraitBlobUrl.value;
  } catch (err) {
    if (err._redirected) return;
    portraitError.value = true;
  } finally {
    portraitLoading.value = false;
  }
};

const renderPreview = () => {
  if (!canvasRef.value || !bgLoaded.value || !portraitLoaded.value) return;
  if (!backgroundImage.value || !portraitImage.value) return;

  const canvas = canvasRef.value;
  const bg = backgroundImage.value;
  const portrait = portraitImage.value;

  canvas.width = bg.naturalWidth;
  canvas.height = bg.naturalHeight;
  const ctx = canvas.getContext("2d");

  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.drawImage(bg, 0, 0);

  const scale = gv.portraitConfigTargetScale ?? 1;
  const portraitW = Math.round(portrait.naturalWidth * scale);
  const portraitH = Math.round(portrait.naturalHeight * scale);

  const alignX = gv.config.portraitAlignX ?? 0;
  const alignY = gv.config.portraitAlignY ?? 0;
  const anchorX = gv.config.portraitAnchorX ?? 0.5;
  const anchorY = gv.config.portraitAnchorY ?? 0.5;
  const offsetX = gv.portraitConfigOffsetX ?? 0;
  const offsetY = gv.portraitConfigOffsetY ?? 0;

  const x = alignX - portraitW * anchorX + offsetX;
  const y = alignY - portraitH * anchorY + offsetY;

  if (gv.portraitConfigEnableFade) {
    const fadeCanvas = document.createElement("canvas");
    fadeCanvas.width = portraitW;
    fadeCanvas.height = portraitH;
    const fadeCtx = fadeCanvas.getContext("2d");
    fadeCtx.drawImage(portrait, 0, 0, portraitW, portraitH);

    const imageData = fadeCtx.getImageData(0, 0, portraitW, portraitH);
    const data = imageData.data;
    const fadeStartX = Math.floor(portraitW * (gv.portraitConfigFadeStart ?? 0.75));

    for (let px = fadeStartX; px < portraitW; px++) {
      const alpha = 1 - (px - fadeStartX) / (portraitW - fadeStartX);
      const fadeAlpha = Math.pow(Math.max(0, Math.min(1, alpha)), 5);
      for (let py = 0; py < portraitH; py++) {
        const idx = (py * portraitW + px) * 4;
        data[idx + 3] = Math.floor(data[idx + 3] * fadeAlpha);
      }
    }

    fadeCtx.putImageData(imageData, 0, 0);
    ctx.drawImage(fadeCanvas, x, y);
  } else {
    ctx.drawImage(portrait, x, y, portraitW, portraitH);
  }
};

const scheduleRender = () => {
  clearTimeout(renderTimeout);
  renderTimeout = setTimeout(renderPreview, 50);
};

watch(
  () => [gv.portraitConfigOffsetX, gv.portraitConfigOffsetY, gv.portraitConfigTargetScale, gv.portraitConfigEnableFade, gv.portraitConfigFadeStart],
  scheduleRender,
);

watch(
  () => gv.portraitConfigServerId,
  () => {
    if (gv.portraitConfigServerId != null) {
      cleanupPortrait();
      loadPortrait();
    }
  },
);

watch(
  () => gv.showPortraitConfigModal,
  (visible) => {
    if (visible) {
      cleanupPortrait();
      bgLoaded.value = false;
      backgroundImage.value = null;
      loadBackground();
      if (gv.portraitConfigServerId != null) {
        loadPortrait();
      }
    }
  },
);

onUnmounted(() => {
  clearTimeout(renderTimeout);
  revokePortraitBlob();
});
</script>

<template>
  <Dialog
    v-model:visible="gv.showPortraitConfigModal"
    modal
    header="Edit Portrait Config"
    :style="{ width: '70rem' }"
  >
    <div class="relative">
      <div
        v-if="gv.portraitConfigFetching || portraitLoading"
        class="absolute inset-0 z-10 flex items-center justify-center rounded bg-black/20"
      >
        <i class="pi pi-spin pi-spinner text-xl"></i>
      </div>
      <form @submit.prevent="gv.handlePortraitConfigSubmit()">
        <div class="flex gap-6">
          <div class="flex flex-col gap-3 w-64 shrink-0">
            <div class="flex flex-col gap-2">
              <label for="portrait-char">Character</label>
              <input
                id="portrait-char"
                :value="gv.portraitConfigCharacter"
                disabled
                class="w-full rounded border bg-gray-100 dark:bg-gray-700 px-3 py-2 text-sm"
              />
            </div>
            <div
              v-if="gv.portraitConfigServerIds && gv.portraitConfigServerIds.length > 1"
              class="flex flex-col gap-2"
            >
              <label for="portrait-server-id">Portrait (Server ID)</label>
              <Select
                id="portrait-server-id"
                v-model="gv.portraitConfigServerId"
                :options="serverIdOptions"
                optionLabel="label"
                optionValue="value"
                placeholder="Select portrait"
                fluid
              />
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-offset-x">Offset X (px)</label>
              <InputNumber
                id="portrait-offset-x"
                v-model="gv.portraitConfigOffsetX"
                :minFractionDigits="0"
                fluid
              />
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-offset-y">Offset Y (px)</label>
              <InputNumber
                id="portrait-offset-y"
                v-model="gv.portraitConfigOffsetY"
                :minFractionDigits="0"
                fluid
              />
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-scale">Target Scale</label>
              <InputNumber
                id="portrait-scale"
                v-model="gv.portraitConfigTargetScale"
                :minFractionDigits="2"
                :maxFractionDigits="4"
                placeholder="e.g. 1.0"
                fluid
              />
            </div>
            <div class="flex items-center gap-2">
              <Checkbox
                v-model="gv.portraitConfigEnableFade"
                binary
                inputId="portrait-fade"
              />
              <label for="portrait-fade">Enable Gradient Fade</label>
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-fade-start">Gradient Fade Start</label>
              <InputNumber
                id="portrait-fade-start"
                v-model="gv.portraitConfigFadeStart"
                :minFractionDigits="2"
                :maxFractionDigits="2"
                :min="0"
                :max="1"
                fluid
              />
            </div>
            <div class="flex justify-end gap-2 mt-2">
              <Button
                type="button"
                label="Cancel"
                severity="secondary"
                @click="gv.showPortraitConfigModal = false"
              />
              <Button
                type="submit"
                label="Save"
                :loading="gv.portraitConfigSaving"
                :disabled="portraitError"
              />
            </div>
          </div>
          <div class="flex-1 min-w-0 flex flex-col gap-2">
            <label class="text-sm text-gray-500">Preview</label>
            <div
              class="border rounded overflow-hidden bg-gray-100 dark:bg-gray-800"
            >
              <canvas ref="canvasRef" class="w-full h-auto" />
            </div>
            <div
              v-if="portraitError"
              class="text-sm text-red-500 text-center italic"
            >
              Portrait image not available
            </div>
          </div>
        </div>
      </form>
    </div>
  </Dialog>
</template>