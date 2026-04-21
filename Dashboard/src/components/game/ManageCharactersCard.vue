<script setup>
import { computed } from "vue";
import InputText from "primevue/inputtext";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import Checkbox from "primevue/checkbox";

const props = defineProps({
  characters: Array,
  filteredCharacters: Array,
  manageCharacterItems: Array,
  newCharacterName: String,
  manageSearchQuery: String,
  showOnlyMissingAscension: Boolean,
  manageLoading: Boolean,
  manageError: String,
  hasStatEdit: Boolean,
});

const emit = defineEmits([
  "update:newCharacterName",
  "update:manageSearchQuery",
  "update:showOnlyMissingAscension",
  "addCharacter",
  "deleteCharacter",
  "editStat",
]);

const handleNewCharacterNameUpdate = (value) =>
  emit("update:newCharacterName", value);
const handleSearchQueryUpdate = (value) =>
  emit("update:manageSearchQuery", value);
const handleShowOnlyMissingAscensionUpdate = (value) =>
  emit("update:showOnlyMissingAscension", value);
const handleAdd = () => emit("addCharacter");
const handleDelete = (name) => emit("deleteCharacter", name);
const handleEditStat = (name) => emit("editStat", name);

const listItems = computed(() => {
  if (
    Array.isArray(props.manageCharacterItems) &&
    props.manageCharacterItems.length > 0
  ) {
    return props.manageCharacterItems;
  }

  return (props.filteredCharacters || []).map((name) => ({
    name,
    baseVal: 0,
    maxAscVal: 0,
  }));
});

const formatStat = (value) => {
  const number = typeof value === "number" ? value : Number(value || 0);
  return number === 0 ? "-" : number;
};
</script>

<template>
  <Card>
    <template #title>Manage Characters</template>
    <template #content>
      <div class="flex flex-col gap-4">
        <div class="flex gap-2">
          <InputText
            :modelValue="newCharacterName"
            @update:modelValue="handleNewCharacterNameUpdate"
            placeholder="New Character Name"
            fluid
            class="flex-1"
          />
          <Button label="Add" @click="handleAdd" :loading="manageLoading" />
        </div>
        <Message v-if="manageError" severity="error">{{ manageError }}</Message>
        <div class="flex flex-col gap-2">
          <InputText
            :modelValue="manageSearchQuery"
            @update:modelValue="handleSearchQueryUpdate"
            placeholder="Search characters..."
            fluid
          />
        </div>
        <div v-if="hasStatEdit" class="flex items-center align-middle gap-2">
          <Checkbox
            :modelValue="showOnlyMissingAscension"
            @update:modelValue="handleShowOnlyMissingAscensionUpdate"
            binary
            inputId="missing-ascension-filter"
          />
          <label
            for="missing-ascension-filter"
            class="text-sm text-gray-500 mb-0!"
          >
            Only show characters without max ascension value
          </label>
        </div>
        <div class="flex flex-col max-h-150 overflow-y-auto rounded">
          <div
            v-for="item in listItems"
            :key="item.name"
            class="flex justify-between items-center p-2 hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors gap-2"
          >
            <div class="flex flex-col gap-1 text-left">
              <span>{{ item.name }}</span>
              <div v-if="hasStatEdit" class="flex gap-2 text-xs text-gray-500">
                <span>Base: {{ formatStat(item.baseVal) }}</span>
                <span>Max Asc: {{ formatStat(item.maxAscVal) }}</span>
              </div>
            </div>
            <div class="flex gap-2">
              <Button
                v-if="hasStatEdit"
                icon="pi pi-pencil"
                severity="info"
                text
                @click="handleEditStat(item.name)"
                :loading="manageLoading"
              />
              <Button
                icon="pi pi-trash"
                severity="danger"
                text
                @click="handleDelete(item.name)"
                :loading="manageLoading"
              />
            </div>
          </div>
        </div>
      </div>
    </template>
  </Card>
</template>
