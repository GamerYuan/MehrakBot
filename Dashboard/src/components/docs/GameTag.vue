<script setup>
import { computed } from "vue";
import { gameColors, gameLabels } from "../../composables/useDocs.js";

const props = defineProps({
  game: {
    type: String,
    required: true,
  },
  size: {
    type: String,
    default: "normal",
    validator: (value) => ["small", "normal"].includes(value),
  },
});

const colors = computed(() => gameColors[props.game] || gameColors.Unsupported);
const label = computed(() => gameLabels[props.game] || props.game);
</script>

<template>
  <span
    :class="[
      'inline-flex items-center rounded-full border font-semibold uppercase tracking-wide',
      size === 'small' ? 'px-2 py-0.5 text-[0.65rem]' : 'px-3 py-1 text-xs'
    ]"
    :style="{
      backgroundColor: colors.bg,
      borderColor: colors.border,
      color: colors.text,
    }"
  >
    {{ label }}
  </span>
</template>
