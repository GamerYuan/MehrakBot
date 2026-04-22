<script setup>
import { ref, computed } from "vue";
import InputText from "primevue/inputtext";
import Button from "primevue/button";

const props = defineProps({
  searchQuery: String,
  selectedGames: Array,
});

const emit = defineEmits([
  "update:searchQuery",
  "toggleGame",
  "selectAllGames",
]);

const gameFilters = [
  { key: "Genshin", label: "Genshin Impact", color: "#FFD700" },
  { key: "HonkaiStarRail", label: "Honkai: Star Rail", color: "#00D4FF" },
  { key: "ZenlessZoneZero", label: "Zenless Zone Zero", color: "#FF6B00" },
  { key: "HonkaiImpact3", label: "Honkai Impact 3rd", color: "#FF69B4" },
  { key: "TearsOfThemis", label: "Tears of Themis", color: "#8A2BE2" },
  { key: "Unsupported", label: "Miscellaneous", color: "#888888" },
];

const localSearch = ref(props.searchQuery);

const handleSearchUpdate = (value) => {
  localSearch.value = value;
  emit("update:searchQuery", value);
};

const isGameSelected = (game) => props.selectedGames.includes(game);

const allSelected = computed(
  () => props.selectedGames.length === gameFilters.length,
);
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="relative w-full">
      <i
        class="pi pi-search absolute left-4 top-1/2 -translate-y-1/2 text-zinc-500 z-10 pointer-events-none"
      ></i>
      <InputText
        :modelValue="searchQuery"
        @update:modelValue="handleSearchUpdate"
        placeholder="Search commands..."
        class="w-full py-3 bg-white/5 border-white/10 rounded-xl focus:ring-emerald-500 focus:border-emerald-500 text-zinc-200 placeholder:text-zinc-500"
        pt:root="pl-11"
      />
    </div>

    <div class="flex flex-wrap items-center gap-3">
      <span class="text-sm text-zinc-400">Filter by game:</span>
      <div class="flex flex-wrap gap-2">
        <button
          v-for="game in gameFilters"
          :key="game.key"
          :class="[
            'inline-flex items-center gap-2 px-3 py-1.5 rounded-full border text-xs cursor-pointer transition-all',
            isGameSelected(game.key)
              ? 'border-white/25 bg-white/10 text-white'
              : 'border-white/10 bg-white/5 text-zinc-400 hover:border-white/20 hover:bg-white/10',
          ]"
          @click="emit('toggleGame', game.key)"
        >
          <span
            class="w-2 h-2 rounded-full"
            :style="{ backgroundColor: game.color }"
          ></span>
          {{ game.label }}
        </button>
      </div>
      <Button
        v-if="!allSelected"
        label="Select All"
        size="small"
        variant="text"
        @click="emit('selectAllGames')"
        class="text-xs! py-1! px-2! text-emerald-400!"
      />
    </div>
  </div>
</template>

<style scoped>
:deep(.p-inputtext) {
  padding-left: 2.75rem;
}
</style>
