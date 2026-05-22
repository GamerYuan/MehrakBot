<script setup>
import { useGameViewInject } from "../../composables/game/injectKey";
import Dialog from "primevue/dialog";
import InputNumber from "primevue/inputnumber";
import InputText from "primevue/inputtext";
import Button from "primevue/button";

const gv = useGameViewInject();
</script>

<template>
  <Dialog
    :visible="gv.showEditStatModal.value"
    @update:visible="(value) => (gv.showEditStatModal.value = value)"
    modal
    header="Edit Character Stats"
    :style="{ width: '30rem' }"
  >
    <div class="relative">
      <div
        v-if="gv.editStatFetching.value"
        class="absolute inset-0 z-10 flex items-center justify-center rounded bg-black/20"
      >
        <i class="pi pi-spin pi-spinner text-xl"></i>
      </div>
      <form @submit.prevent="gv.handleStatSubmit">
        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label for="stat-char">Character Name</label>
            <InputText
              id="stat-char"
              :modelValue="gv.editStatCharacter.value"
              disabled
              fluid
            />
          </div>
          <div class="flex flex-col gap-2">
            <label for="stat-base">Base Stat (HP)</label>
            <div class="flex gap-2">
              <InputNumber
                id="stat-base"
                v-model="gv.editStatBase.value"
                :minFractionDigits="0"
                :maxFractionDigits="5"
                fluid
                class="flex-1"
              />
              <Button
                type="button"
                icon="pi pi-trash"
                severity="danger"
                text
                @click="gv.editStatBase.value = null"
              />
            </div>
          </div>
          <div class="flex flex-col gap-2">
            <label for="stat-max">Max Ascension Value (HP)</label>
            <div class="flex gap-2">
              <InputNumber
                id="stat-max"
                v-model="gv.editStatMax.value"
                :minFractionDigits="0"
                :maxFractionDigits="5"
                fluid
                class="flex-1"
              />
              <Button
                type="button"
                icon="pi pi-trash"
                severity="danger"
                text
                @click="gv.editStatMax.value = null"
              />
            </div>
          </div>
          <div class="flex justify-end gap-2 mt-2">
            <Button
              type="button"
              label="Cancel"
              severity="secondary"
              @click="gv.showEditStatModal.value = false"
            />
            <Button type="submit" label="Update" :loading="gv.editStatLoading.value" />
          </div>
        </div>
      </form>
    </div>
  </Dialog>
</template>