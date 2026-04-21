<script setup>
import { computed } from "vue";

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

const gameColors = {
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
  Unsupported: {
    bg: "rgba(136, 136, 136, 0.15)",
    border: "rgba(136, 136, 136, 0.4)",
    text: "#888888",
  },
};

const gameLabels = {
  Genshin: "Genshin Impact",
  HonkaiStarRail: "Honkai: Star Rail",
  ZenlessZoneZero: "Zenless Zone Zero",
  HonkaiImpact3: "Honkai Impact 3rd",
  Unsupported: "Miscellaneous",
};

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
