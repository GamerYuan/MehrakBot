<script setup>
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import Tag from "primevue/tag";
import InputText from "primevue/inputtext";
import Button from "primevue/button";
import Card from "primevue/card";
import Dialog from "primevue/dialog";

const props = defineProps({
  aliases: Array,
  filteredAliases: Array,
  aliasSearchQuery: String,
  manageLoading: Boolean,
  showAddAliasModal: Boolean,
  newAliasCharacter: String,
  newAliasList: String,
  addAliasLoading: Boolean,
  isEditingAlias: Boolean,
});

const emit = defineEmits([
  "update:aliasSearchQuery",
  "update:showAddAliasModal",
  "update:newAliasCharacter",
  "update:newAliasList",
  "openAddAliasModal",
  "openEditAliasModal",
  "handleAliasSubmit",
]);

const handleSearchQueryUpdate = (value) =>
  emit("update:aliasSearchQuery", value);
const handleModalVisibleUpdate = (value) =>
  emit("update:showAddAliasModal", value);
const handleCharacterUpdate = (value) =>
  emit("update:newAliasCharacter", value);
const handleAliasListUpdate = (value) => emit("update:newAliasList", value);
const handleOpenAdd = () => emit("openAddAliasModal");
const handleOpenEdit = (data) => emit("openEditAliasModal", data);
const handleSubmit = () => emit("handleAliasSubmit");
</script>

<template>
  <Card>
    <template #title>Manage Aliases</template>
    <template #content>
      <div class="flex flex-col gap-4">
        <div class="flex flex-row gap-4">
          <InputText
            :modelValue="aliasSearchQuery"
            @update:modelValue="handleSearchQueryUpdate"
            placeholder="Search aliases..."
            fluid
          />
          <Button label="Add" @click="handleOpenAdd" :loading="manageLoading" />
        </div>
        <DataTable
          :value="filteredAliases"
          paginator
          :rows="10"
          tableStyle="min-width: 50rem"
        >
          <Column field="name" header="Character Name" sortable></Column>
          <Column header="Aliases">
            <template #body="slotProps">
              <div class="flex flex-wrap gap-2">
                <Tag
                  v-for="alias in slotProps.data.aliases"
                  :key="alias"
                  :value="alias"
                  severity="info"
                />
              </div>
            </template>
          </Column>
          <Column style="width: 3rem">
            <template #body="slotProps">
              <Button
                icon="pi pi-pencil"
                text
                rounded
                severity="secondary"
                @click="handleOpenEdit(slotProps.data)"
              />
            </template>
          </Column>
        </DataTable>
      </div>
    </template>
  </Card>

  <Dialog
    :visible="showAddAliasModal"
    @update:visible="handleModalVisibleUpdate"
    modal
    :header="isEditingAlias ? 'Edit Alias' : 'Add Alias'"
    :style="{ width: '30rem' }"
  >
    <form @submit.prevent="handleSubmit">
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-2">
          <label for="alias-char">Character Name</label>
          <InputText
            id="alias-char"
            :modelValue="newAliasCharacter"
            @update:modelValue="handleCharacterUpdate"
            required
            placeholder="e.g. Nahida"
            fluid
            :disabled="isEditingAlias"
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="alias-list">Aliases (comma-separated)</label>
          <InputText
            id="alias-list"
            :modelValue="newAliasList"
            @update:modelValue="handleAliasListUpdate"
            required
            placeholder="e.g. Radish, Dendro Archon"
            fluid
          />
        </div>
        <div class="flex justify-end gap-2 mt-2">
          <Button
            type="button"
            label="Cancel"
            severity="secondary"
            @click="handleModalVisibleUpdate(false)"
          />
          <Button
            type="submit"
            :label="isEditingAlias ? 'Update' : 'Add'"
            :loading="addAliasLoading"
            :disabled="!newAliasCharacter || !newAliasList"
          />
        </div>
      </div>
    </form>
  </Dialog>
</template>
