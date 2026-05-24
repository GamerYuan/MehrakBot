<script setup>
import { useGameViewInject } from "../../composables/game/injectKey";
import Dialog from "primevue/dialog";
import Password from "primevue/password";
import Button from "primevue/button";
import Message from "primevue/message";

const gv = useGameViewInject();

const handleVisibleUpdate = (value) => {
  gv.showAuthModal = value;
  if (!value) {
    gv.authPassphrase = "";
  }
};
</script>

<template>
  <Dialog
    v-model:visible="gv.showAuthModal"
    modal
    header="Profile Authentication Required"
    :style="{ width: '25rem' }"
  >
    <p class="mb-4">
      Please authenticate profile <strong>{{ gv.authProfileId }}</strong>
    </p>

    <form @submit.prevent="gv.handleAuth()">
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-2">
          <label>Passphrase</label>
          <Password
            v-model="gv.authPassphrase"
            required
            :feedback="false"
            toggleMask
            fluid
          />
        </div>

        <Message v-if="gv.authError" severity="error">{{ gv.authError }}</Message>

        <div class="flex justify-end gap-2">
          <Button
            type="button"
            label="Cancel"
            severity="secondary"
            @click="handleVisibleUpdate(false)"
          />
          <Button
            type="submit"
            :label="gv.authLoading ? 'Authenticating...' : 'Authenticate'"
            :loading="gv.authLoading"
          />
        </div>
      </div>
    </form>
  </Dialog>
</template>
