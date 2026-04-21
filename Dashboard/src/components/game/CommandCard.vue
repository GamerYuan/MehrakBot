<script setup>
import InputNumber from 'primevue/inputnumber';
import Select from 'primevue/select';
import AutoComplete from 'primevue/autocomplete';
import Button from 'primevue/button';
import Card from 'primevue/card';
import Message from 'primevue/message';

const props = defineProps({
  activeTab: String,
  tabConfig: Object,
  config: Object,
  profileId: Number,
  server: String,
  characterName: String,
  floor: Number,
  filteredCharacters: Array,
  loading: Boolean,
  error: String,
});

const emit = defineEmits([
  'update:profileId',
  'update:server',
  'update:characterName',
  'update:floor',
  'searchCharacter',
  'execute',
]);

const handleProfileIdUpdate = (value) => emit('update:profileId', value);
const handleServerUpdate = (value) => emit('update:server', value);
const handleCharacterNameUpdate = (value) => emit('update:characterName', value);
const handleFloorUpdate = (value) => emit('update:floor', value);
const handleSearch = (event) => emit('searchCharacter', event);
const handleSubmit = () => emit('execute');
</script>

<template>
  <Card class="command-card">
    <template #content>
      <form @submit.prevent="handleSubmit">
        <div class="flex flex-col gap-4">
          <div class="flex flex-row md:flex-row gap-4">
            <div class="flex flex-col gap-2 flex-1">
              <label>Profile ID (1-10)</label>
              <InputNumber
                :modelValue="profileId"
                @update:modelValue="handleProfileIdUpdate"
                showButtons
                :min="1"
                :max="10"
                fluid
              />
            </div>
            <div class="flex flex-col gap-2 flex-1">
              <label>Server</label>
              <Select
                :modelValue="server"
                @update:modelValue="handleServerUpdate"
                :options="config.servers"
                optionLabel="label"
                optionValue="value"
                fluid
                class="h-full items-center"
              />
            </div>
          </div>

          <div v-if="tabConfig?.hasCharacterInput" class="flex flex-col gap-2">
            <label>{{ tabConfig?.characterLabel || 'Character Name' }}</label>
            <AutoComplete
              :modelValue="characterName"
              @update:modelValue="handleCharacterNameUpdate"
              :suggestions="filteredCharacters"
              @complete="handleSearch"
              dropdown
              fluid
              :placeholder="config.characterPlaceholder"
            />
          </div>

          <div v-if="tabConfig?.hasFloorInput" class="flex flex-col gap-2">
            <label>Floor ({{ tabConfig.floorMin }}-{{ tabConfig.floorMax }})</label>
            <InputNumber
              :modelValue="floor"
              @update:modelValue="handleFloorUpdate"
              showButtons
              :min="tabConfig.floorMin"
              :max="tabConfig.floorMax"
              fluid
            />
          </div>

          <Button
            type="submit"
            :label="loading ? 'Executing...' : 'Execute'"
            :loading="loading"
            fluid
          />
        </div>
      </form>
      <Message v-if="error" severity="error" class="mt-2">
        {{ error }}
      </Message>
    </template>
  </Card>
</template>