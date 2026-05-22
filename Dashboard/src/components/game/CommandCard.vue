<script setup>
import { useGameViewInject } from "../../composables/game/injectKey";
import InputNumber from "primevue/inputnumber";
import Select from "primevue/select";
import AutoComplete from "primevue/autocomplete";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";

defineProps({
  tabConfig: Object,
});

const gv = useGameViewInject();
</script>

<template>
  <Card class="command-card">
    <template #content>
      <form @submit.prevent="gv.executeCommand()">
        <div class="flex flex-col gap-4">
          <div class="flex flex-row md:flex-row gap-4">
            <div class="flex flex-col gap-2 flex-1">
              <label :for="`${tabConfig?.id}-profile-id`">Profile ID (1-10)</label>
              <InputNumber
                :inputId="`${tabConfig?.id}-profile-id`"
                v-model="gv.profileId"
                showButtons
                :min="1"
                :max="10"
                fluid
              />
            </div>
            <div class="flex flex-col gap-2 flex-1">
              <label :for="`${tabConfig?.id}-server`">Server</label>
              <Select
                :inputId="`${tabConfig?.id}-server`"
                v-model="gv.server"
                :options="gv.config.servers"
                optionLabel="label"
                optionValue="value"
                fluid
                class="h-full items-center"
              />
            </div>
          </div>

          <div v-if="tabConfig?.hasCharacterInput" class="flex flex-col gap-2">
            <label :for="`${tabConfig?.id}-character-name`">
              {{ tabConfig?.characterLabel || "Character Name" }}
            </label>
            <AutoComplete
              :inputId="`${tabConfig?.id}-character-name`"
              v-model="gv.characterName"
              :suggestions="gv.filteredCharacters"
              @complete="gv.searchCharacter"
              dropdown
              fluid
              :placeholder="gv.config.characterPlaceholder"
            />
          </div>

          <div v-if="tabConfig?.hasFloorInput" class="flex flex-col gap-2">
            <label :for="`${tabConfig?.id}-floor`"
              >Floor ({{ tabConfig.floorMin }}-{{ tabConfig.floorMax }})</label
            >
            <InputNumber
              :inputId="`${tabConfig?.id}-floor`"
              v-model="gv.floor"
              showButtons
              :min="tabConfig.floorMin"
              :max="tabConfig.floorMax"
              fluid
            />
          </div>

          <Button
            type="submit"
            :label="gv.loading ? 'Executing...' : 'Execute'"
            :loading="gv.loading"
            fluid
          />
        </div>
      </form>
      <Message v-if="gv.error" severity="error" class="mt-2">
        {{ gv.error }}
      </Message>
    </template>
  </Card>
</template>