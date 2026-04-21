<script setup>
import { ref, computed } from "vue";
import InputText from "primevue/inputtext";
import Checkbox from "primevue/checkbox";
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
  { key: "Unsupported", label: "Miscellaneous", color: "#888888" },
];

const localSearch = ref(props.searchQuery);

const handleSearchUpdate = (value) => {
  localSearch.value = value;
  emit("update:searchQuery", value);
};

const isGameSelected = (game) => props.selectedGames.includes(game);

const allSelected = computed(() => props.selectedGames.length === 5);
</script>

<template>
  <div class="search-bar">
    <div class="search-input-wrapper">
      <i class="pi pi-search search-icon"></i>
      <InputText
        :modelValue="searchQuery"
        @update:modelValue="handleSearchUpdate"
        placeholder="Search commands..."
        class="search-input"
      />
    </div>

    <div class="filter-section">
      <span class="filter-label">Filter by game:</span>
      <div class="filter-options">
        <button
          v-for="game in gameFilters"
          :key="game.key"
          :class="['filter-chip', { active: isGameSelected(game.key) }]"
          @click="emit('toggleGame', game.key)"
        >
          <span
            class="filter-dot"
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
        class="select-all-btn"
      />
    </div>
  </div>
</template>

<style scoped>
.search-bar {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.search-input-wrapper {
  position: relative;
}

.search-icon {
  position: absolute;
  left: 1rem;
  top: 50%;
  transform: translateY(-50%);
  color: #666;
  font-size: 1rem;
}

.search-input {
  width: 100%;
  padding: 0.875rem 1rem 0.875rem 2.75rem;
  border-radius: 10px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  background: rgba(255, 255, 255, 0.03);
  color: #fff;
  font-size: 1rem;
}

.search-input:focus {
  outline: none;
  border-color: #5865f2;
  background: rgba(255, 255, 255, 0.05);
}

.search-input::placeholder {
  color: #666;
}

.filter-section {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}

.filter-label {
  font-size: 0.875rem;
  color: #888;
}

.filter-options {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.filter-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.4rem 0.75rem;
  border-radius: 100px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  background: rgba(255, 255, 255, 0.03);
  color: #888;
  font-size: 0.8rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.filter-chip:hover {
  border-color: rgba(255, 255, 255, 0.2);
  background: rgba(255, 255, 255, 0.06);
}

.filter-chip.active {
  border-color: rgba(255, 255, 255, 0.25);
  background: rgba(255, 255, 255, 0.08);
  color: #fff;
}

.filter-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.select-all-btn {
  font-size: 0.8rem !important;
  padding: 0.4rem 0.5rem !important;
}
</style>
