<script setup>
import Dialog from 'primevue/dialog';
import InputNumber from 'primevue/inputnumber';
import InputText from 'primevue/inputtext';
import Button from 'primevue/button';

const props = defineProps({
  visible: Boolean,
  characterName: String,
  baseVal: [Number, null],
  maxAscVal: [Number, null],
  loading: Boolean,
  fetching: Boolean,
});

const emit = defineEmits(['update:visible', 'update:baseVal', 'update:maxAscVal', 'submit']);

const handleVisibleUpdate = (value) => emit('update:visible', value);
const handleBaseValUpdate = (value) => emit('update:baseVal', value);
const handleMaxAscValUpdate = (value) => emit('update:maxAscVal', value);
const handleSubmit = () => emit('submit');
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="handleVisibleUpdate"
    modal
    header="Edit Character Stats"
    :style="{ width: '30rem' }"
  >
    <div class="relative">
      <div
        v-if="fetching"
        class="absolute inset-0 z-10 flex items-center justify-center rounded bg-black/20"
      >
        <i class="pi pi-spin pi-spinner text-xl"></i>
      </div>
      <form @submit.prevent="handleSubmit">
        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label for="stat-char">Character Name</label>
            <InputText id="stat-char" :modelValue="characterName" disabled fluid />
          </div>
          <div class="flex flex-col gap-2">
            <label for="stat-base">Base Stat (HP)</label>
            <div class="flex gap-2">
              <InputNumber
                id="stat-base"
                :modelValue="baseVal"
                @update:modelValue="handleBaseValUpdate"
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
                @click="handleBaseValUpdate(null)"
              />
            </div>
          </div>
          <div class="flex flex-col gap-2">
            <label for="stat-max">Max Ascension Value (HP)</label>
            <div class="flex gap-2">
              <InputNumber
                id="stat-max"
                :modelValue="maxAscVal"
                @update:modelValue="handleMaxAscValUpdate"
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
                @click="handleMaxAscValUpdate(null)"
              />
            </div>
          </div>
          <div class="flex justify-end gap-2 mt-2">
            <Button
              type="button"
              label="Cancel"
              severity="secondary"
              @click="handleVisibleUpdate(false)"
            />
            <Button type="submit" label="Update" :loading="loading" />
          </div>
        </div>
      </form>
    </div>
  </Dialog>
</template>