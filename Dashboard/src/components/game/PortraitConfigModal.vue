<script setup>
import { ref, watch, computed, nextTick } from "vue";
import Dialog from "primevue/dialog";
import InputNumber from "primevue/inputnumber";
import Checkbox from "primevue/checkbox";
import Button from "primevue/button";
import { useToast } from "primevue/usetoast";

const props = defineProps({
  visible: Boolean,
  characterName: String,
  gameId: String,
  loading: Boolean,
  fetching: Boolean,
  offsetX: [Number, null],
  offsetY: [Number, null],
  targetScale: [Number, null],
  enableGradientFade: [Boolean, null],
  gradientFadeStart: [Number, null],
});

const emit = defineEmits([
  "update:visible",
  "update:offsetX",
  "update:offsetY",
  "update:targetScale",
  "update:enableGradientFade",
  "update:gradientFadeStart",
  "submit",
]);

const toast = useToast();

const canvasRef = ref(null);
const portraitImage = ref(null);
const portraitError = ref(false);
const portraitLoading = ref(false);
const bgLoaded = ref(false);
const portraitLoaded = ref(false);

const handleVisibleUpdate = (value) => emit("update:visible", value);
const handleOffsetXUpdate = (value) => emit("update:offsetX", value);
const handleOffsetYUpdate = (value) => emit("update:offsetY", value);
const handleTargetScaleUpdate = (value) => emit("update:targetScale", value);
const handleEnableFadeUpdate = (value) => emit("update:enableGradientFade", value);
const handleFadeStartUpdate = (value) => emit("update:gradientFadeStart", value);
const handleSubmit = () => emit("submit");

const bgUrl = computed(() => props.gameId ? `/${props.gameId.toLowerCase()}_portrait_bg.webp` : "");

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
  if (!props.gameId || !props.characterName) return;
  portraitLoading.value = true;
  portraitError.value = false;

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    const response = await fetch(
      `${backendUrl}/portraits/image?game=${encodeURIComponent(props.gameId)}&character=${encodeURIComponent(props.characterName)}`,
      { credentials: "include" }
    );

    if (response.status === 404) {
      portraitError.value = true;
      toast.add({
        severity: "error",
        summary: "Image Not Found",
        detail: `Portrait image for ${props.characterName} not found, please generate an image with this character in the Characters tab and try again`,
        life: 5000,
      });
      return;
    }

    if (!response.ok) {
      portraitError.value = true;
      return;
    }

    const blob = await response.blob();
    portraitImage.value = new Image();
    portraitImage.value.onload = () => {
      portraitLoaded.value = true;
      renderPreview();
    };
    portraitImage.value.src = URL.createObjectURL(blob);
  } catch {
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

  const scale = props.targetScale ?? 1;
  const portraitW = portrait.naturalWidth * scale;
  const portraitH = portrait.naturalHeight * scale;

  const centerX = canvas.width / 2;
  const centerY = canvas.height / 2;
  const offsetX = props.offsetX ?? 0;
  const offsetY = props.offsetY ?? 0;

  const x = centerX - portraitW / 2 + offsetX;
  const y = centerY - portraitH / 2 + offsetY;

  if (props.enableGradientFade) {
    const fadeCanvas = document.createElement("canvas");
    fadeCanvas.width = portraitW;
    fadeCanvas.height = portraitH;
    const fadeCtx = fadeCanvas.getContext("2d");
    fadeCtx.drawImage(portrait, 0, 0, portraitW, portraitH);

    const imageData = fadeCtx.getImageData(0, 0, portraitW, portraitH);
    const data = imageData.data;
    const fadeStartX = Math.floor(portraitW * (props.gradientFadeStart ?? 0.75));

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

let renderTimeout;
const scheduleRender = () => {
  clearTimeout(renderTimeout);
  renderTimeout = setTimeout(renderPreview, 50);
};

watch(
  () => [props.offsetX, props.offsetY, props.targetScale, props.enableGradientFade, props.gradientFadeStart],
  scheduleRender
);

watch(() => props.visible, (visible) => {
  if (visible) {
    portraitImage.value = null;
    portraitError.value = false;
    portraitLoaded.value = false;
    bgLoaded.value = false;
    backgroundImage.value = null;
    loadBackground();
    loadPortrait();
  }
});

</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="handleVisibleUpdate"
    modal
    header="Edit Portrait Config"
    :style="{ width: '50rem' }"
  >
    <div class="relative">
      <div
        v-if="fetching || portraitLoading"
        class="absolute inset-0 z-10 flex items-center justify-center rounded bg-black/20"
      >
        <i class="pi pi-spin pi-spinner text-xl"></i>
      </div>
      <form @submit.prevent="handleSubmit">
        <div class="flex gap-6">
          <div class="flex flex-col gap-4 flex-1">
            <div class="flex flex-col gap-2">
              <label for="portrait-char">Character</label>
              <input
                id="portrait-char"
                :value="characterName"
                disabled
                class="w-full rounded border bg-gray-100 dark:bg-gray-700 px-3 py-2 text-sm"
              />
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-offset-x">Offset X (px)</label>
              <InputNumber
                id="portrait-offset-x"
                :modelValue="offsetX ?? 0"
                @update:modelValue="handleOffsetXUpdate"
                :minFractionDigits="0"
                fluid
              />
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-offset-y">Offset Y (px)</label>
              <InputNumber
                id="portrait-offset-y"
                :modelValue="offsetY ?? 0"
                @update:modelValue="handleOffsetYUpdate"
                :minFractionDigits="0"
                fluid
              />
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-scale">Target Scale</label>
              <InputNumber
                id="portrait-scale"
                :modelValue="targetScale"
                @update:modelValue="handleTargetScaleUpdate"
                :minFractionDigits="2"
                :maxFractionDigits="4"
                placeholder="e.g. 1.0"
                fluid
              />
            </div>
            <div class="flex items-center gap-2">
              <Checkbox
                :modelValue="enableGradientFade ?? false"
                @update:modelValue="handleEnableFadeUpdate"
                binary
                inputId="portrait-fade"
              />
              <label for="portrait-fade">Enable Gradient Fade</label>
            </div>
            <div class="flex flex-col gap-2">
              <label for="portrait-fade-start">Gradient Fade Start</label>
              <InputNumber
                id="portrait-fade-start"
                :modelValue="gradientFadeStart ?? 0.75"
                @update:modelValue="handleFadeStartUpdate"
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
                @click="handleVisibleUpdate(false)"
              />
              <Button type="submit" label="Save" :loading="loading" :disabled="portraitError" />
            </div>
          </div>
          <div class="flex-1 flex flex-col gap-2">
            <label class="text-sm text-gray-500">Preview</label>
            <div class="border rounded overflow-hidden bg-gray-100 dark:bg-gray-800">
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
