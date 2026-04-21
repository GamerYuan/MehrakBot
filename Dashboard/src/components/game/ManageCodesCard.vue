<script setup>
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import InputText from "primevue/inputtext";
import Button from "primevue/button";
import Card from "primevue/card";

const props = defineProps({
  codes: Array,
  filteredCodes: Array,
  selectedCodes: Array,
  newCodesInput: String,
  codesSearchQuery: String,
  codesLoading: Boolean,
});

const emit = defineEmits([
  "update:selectedCodes",
  "update:newCodesInput",
  "update:codesSearchQuery",
  "confirmAddCodes",
  "confirmDeleteCodes",
]);

const handleSelectionUpdate = (value) => emit("update:selectedCodes", value);
const handleNewCodesInputUpdate = (value) =>
  emit("update:newCodesInput", value);
const handleSearchQueryUpdate = (value) =>
  emit("update:codesSearchQuery", value);
const handleAdd = () => emit("confirmAddCodes");
const handleDelete = (codesList) => emit("confirmDeleteCodes", codesList);
</script>

<template>
  <Card>
    <template #title>Manage Codes</template>
    <template #content>
      <div class="flex flex-col gap-4">
        <div class="flex gap-2">
          <InputText
            :modelValue="newCodesInput"
            @update:modelValue="handleNewCodesInputUpdate"
            placeholder="New Codes (comma-separated)"
            fluid
            class="flex-1"
          />
          <Button
            label="Add"
            @click="handleAdd"
            :loading="codesLoading"
            :disabled="!newCodesInput"
          />
        </div>

        <div class="flex justify-between gap-2">
          <InputText
            :modelValue="codesSearchQuery"
            @update:modelValue="handleSearchQueryUpdate"
            placeholder="Search codes..."
            fluid
            class="flex-1"
          />
          <Button
            label="Delete Selected"
            severity="danger"
            @click="handleDelete(selectedCodes.map((c) => c.code))"
            :disabled="!selectedCodes.length"
            :loading="codesLoading"
          />
        </div>

        <DataTable
          :value="filteredCodes"
          :selection="selectedCodes"
          @update:selection="handleSelectionUpdate"
          dataKey="code"
          paginator
          :rows="10"
          tableStyle="min-width: 50rem"
        >
          <Column selectionMode="multiple" headerStyle="width: 3rem"></Column>
          <Column field="code" header="Code" sortable></Column>
          <Column style="width: 3rem">
            <template #body="slotProps">
              <Button
                icon="pi pi-trash"
                severity="danger"
                text
                rounded
                @click="handleDelete([slotProps.data.code])"
                :loading="codesLoading"
              />
            </template>
          </Column>
        </DataTable>
      </div>
    </template>
  </Card>
</template>
