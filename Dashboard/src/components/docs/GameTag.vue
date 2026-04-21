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
    :class="['game-tag', size]"
    :style="{
      backgroundColor: colors.bg,
      borderColor: colors.border,
      color: colors.text,
    }"
  >
    {{ label }}
  </span>
</template>

<style scoped>
.game-tag {
  display: inline-flex;
  align-items: center;
  padding: 0.25rem 0.75rem;
  border-radius: 100px;
  border: 1px solid;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.02em;
}

.game-tag.small {
  padding: 0.15rem 0.5rem;
  font-size: 0.65rem;
}
</style>
