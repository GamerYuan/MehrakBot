<script setup>
import { useGameViewInject } from "../../composables/game/injectKey";
import DataTable from "primevue/datatable";
import Column from "primevue/column";
import InputText from "primevue/inputtext";
import Button from "primevue/button";
import Card from "primevue/card";

const gv = useGameViewInject();
</script>

<template>
  <Card>
    <template #title>Manage Codes</template>
    <template #content>
      <div class="flex flex-col gap-4">
        <div class="flex gap-2">
          <InputText
            v-model="gv.newCodesInput.value"
            placeholder="New Codes (comma-separated)"
            fluid
            class="flex-1"
          />
          <Button
            label="Add"
            @click="gv.confirmAddCodes"
            :loading="gv.codesLoading.value"
            :disabled="!gv.newCodesInput.value"
          />
        </div>

        <div class="flex justify-between gap-2">
          <InputText
            v-model="gv.codesSearchQuery.value"
            placeholder="Search codes..."
            fluid
            class="flex-1"
          />
          <Button
            label="Delete Selected"
            severity="danger"
            @click="gv.confirmDeleteCodes(gv.selectedCodes.value.map((c) => c.code))"
            :disabled="!gv.selectedCodes.value.length"
            :loading="gv.codesLoading.value"
          />
        </div>

        <DataTable
          :value="gv.filteredCodes.value"
          v-model:selection="gv.selectedCodes.value"
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
                @click="gv.confirmDeleteCodes([slotProps.data.code])"
                :loading="gv.codesLoading.value"
              />
            </template>
          </Column>
        </DataTable>
      </div>
    </template>
  </Card>
</template>