<script setup>
import { useGameViewInject } from "../../composables/game/injectKey";
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import Tag from "primevue/tag";
import InputText from "primevue/inputtext";
import Button from "primevue/button";
import Card from "primevue/card";
import Dialog from "primevue/dialog";

const gv = useGameViewInject();
</script>

<template>
  <Card>
    <template #title>Manage Aliases</template>
    <template #content>
      <div class="flex flex-col gap-4">
        <div class="flex flex-row gap-4">
          <InputText
            v-model="gv.aliasSearchQuery.value"
            placeholder="Search aliases..."
            fluid
          />
          <Button label="Add" @click="gv.openAddAliasModal" :loading="gv.manageLoading.value" />
        </div>
        <DataTable
          :value="gv.filteredAliases.value"
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
                @click="gv.openEditAliasModal(slotProps.data)"
              />
            </template>
          </Column>
        </DataTable>
      </div>
    </template>
  </Card>

  <Dialog
    :visible="gv.showAddAliasModal.value"
    @update:visible="(value) => (gv.showAddAliasModal.value = value)"
    modal
    :header="gv.isEditingAlias.value ? 'Edit Alias' : 'Add Alias'"
    :style="{ width: '30rem' }"
  >
    <form @submit.prevent="gv.handleAliasSubmit">
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-2">
          <label for="alias-char">Character Name</label>
          <InputText
            id="alias-char"
            v-model="gv.newAliasCharacter.value"
            required
            placeholder="e.g. Nahida"
            fluid
            :disabled="gv.isEditingAlias.value"
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="alias-list">Aliases (comma-separated)</label>
          <InputText
            id="alias-list"
            v-model="gv.newAliasList.value"
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
            @click="gv.showAddAliasModal.value = false"
          />
          <Button
            type="submit"
            :label="gv.isEditingAlias.value ? 'Update' : 'Add'"
            :loading="gv.addAliasLoading.value"
            :disabled="!gv.newAliasCharacter.value || !gv.newAliasList.value"
          />
        </div>
      </div>
    </form>
  </Dialog>
</template>