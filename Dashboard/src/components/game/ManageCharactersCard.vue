<script setup>
import InputText from 'primevue/inputtext';
import Button from 'primevue/button';
import Card from 'primevue/card';
import Message from 'primevue/message';

const props = defineProps({
  characters: Array,
  filteredCharacters: Array,
  newCharacterName: String,
  manageSearchQuery: String,
  manageLoading: Boolean,
  manageError: String,
  hasStatEdit: Boolean,
});

const emit = defineEmits([
  'update:newCharacterName',
  'update:manageSearchQuery',
  'addCharacter',
  'deleteCharacter',
  'editStat',
]);

const handleNewCharacterNameUpdate = (value) => emit('update:newCharacterName', value);
const handleSearchQueryUpdate = (value) => emit('update:manageSearchQuery', value);
const handleAdd = () => emit('addCharacter');
const handleDelete = (name) => emit('deleteCharacter', name);
const handleEditStat = (name) => emit('editStat', name);
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
        <div class="flex flex-col max-h-150 overflow-y-auto rounded">
          <div
            v-for="char in filteredCharacters"
            :key="char"
            class="flex justify-between items-center p-2 hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors"
          >
            <span>{{ char }}</span>
            <div class="flex gap-2">
              <Button
                v-if="hasStatEdit"
                icon="pi pi-pencil"
                severity="info"
                text
                @click="handleEditStat(char)"
                :loading="manageLoading"
              />
              <Button
                icon="pi pi-trash"
                severity="danger"
                text
                @click="handleDelete(char)"
                :loading="manageLoading"
              />
            </div>
          </div>
        </div>
      </div>
    </template>
  </Card>
</template>