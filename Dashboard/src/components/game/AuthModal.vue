<script setup>
import Dialog from "primevue/dialog";
import Password from "primevue/password";
import Button from "primevue/button";
import Message from "primevue/message";

const props = defineProps({
  visible: Boolean,
  authProfileId: [String, Number],
  authPassphrase: String,
  authLoading: Boolean,
  authError: String,
});

const emit = defineEmits([
  "update:visible",
  "update:authPassphrase",
  "handleAuth",
]);

const handleVisibleUpdate = (value) => emit("update:visible", value);
const handlePassphraseUpdate = (value) => emit("update:authPassphrase", value);
const handleSubmit = () => emit("handleAuth");
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="handleVisibleUpdate"
    modal
    header="Profile Authentication Required"
    :style="{ width: '25rem' }"
  >
    <p class="mb-4">
      Please authenticate profile <strong>{{ authProfileId }}</strong>
    </p>

    <form @submit.prevent="handleSubmit">
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-2">
          <label>Passphrase</label>
          <Password
            :modelValue="authPassphrase"
            @update:modelValue="handlePassphraseUpdate"
            required
            :feedback="false"
            toggleMask
            fluid
          />
        </div>

        <Message v-if="authError" severity="error">{{ authError }}</Message>

        <div class="flex justify-end gap-2">
          <Button
            type="button"
            label="Cancel"
            severity="secondary"
            @click="handleVisibleUpdate(false)"
          />
          <Button
            type="submit"
            :label="authLoading ? 'Authenticating...' : 'Authenticate'"
            :loading="authLoading"
          />
        </div>
      </div>
    </form>
  </Dialog>
</template>
