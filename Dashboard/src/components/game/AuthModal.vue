<script setup>
import { useGameViewInject } from "../../composables/game/injectKey";
import Dialog from "primevue/dialog";
import Password from "primevue/password";
import Button from "primevue/button";
import Message from "primevue/message";

const gv = useGameViewInject();

const handleVisibleUpdate = (value) => {
  gv.showAuthModal.value = value;
  if (!value) {
    gv.authPassphrase.value = "";
  }
};
</script>

<template>
  <Dialog
    :visible="gv.showAuthModal.value"
    @update:visible="handleVisibleUpdate"
    modal
    header="Profile Authentication Required"
    :style="{ width: '25rem' }"
  >
    <p class="mb-4">
      Please authenticate profile <strong>{{ gv.authProfileId.value }}</strong>
    </p>

    <form @submit.prevent="gv.handleAuth">
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-2">
          <label>Passphrase</label>
          <Password
            v-model="gv.authPassphrase.value"
            required
            :feedback="false"
            toggleMask
            fluid
          />
        </div>

        <Message v-if="gv.authError.value" severity="error">{{ gv.authError.value }}</Message>

        <div class="flex justify-end gap-2">
          <Button
            type="button"
            label="Cancel"
            severity="secondary"
            @click="handleVisibleUpdate(false)"
          />
          <Button
            type="submit"
            :label="gv.authLoading.value ? 'Authenticating...' : 'Authenticate'"
            :loading="gv.authLoading.value"
          />
        </div>
      </div>
    </form>
  </Dialog>
</template>