<script setup>
import { ref } from "vue";
import { useRouter } from "vue-router";
import { useApi } from "../composables/useApi";
import { usePasswordValidation } from "../composables/usePasswordValidation";
import Password from "primevue/password";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";

const router = useRouter();
const { apiFetch } = useApi();

const { newPassword, confirmPassword, passwordsMatch, isPasswordValid, passwordRequirements, isValid } = usePasswordValidation();

const error = ref("");
const loading = ref(false);

const handleReset = async () => {
  if (!isValid.value) return;

  loading.value = true;
  error.value = "";

  try {
    const response = await apiFetch("/auth/password/reset", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ newPassword: newPassword.value }),
    });

    if (!response.ok) {
      const data = await response.json().catch(() => ({}));
      throw new Error(data.error || "Password reset failed");
    }

    router.push("/login?resetSuccess=true");
  } catch (err) {
    if (err._redirected) return;
    error.value = err.message || "An error occurred";
  } finally {
    loading.value = false;
  }
};
</script>

<template>
  <div class="auth-container">
    <Card class="auth-card">
      <template #title>Reset Password</template>
      <template #subtitle>You must reset your password to continue.</template>
      <template #content>
        <form @submit.prevent="handleReset">
          <div class="flex flex-col gap-4">
            <div class="flex flex-col gap-2">
              <label for="password">New Password</label>
              <Password
                id="password"
                v-model="newPassword"
                required
                toggleMask
                fluid
              >
                <template #footer>
                  <div class="password-requirements" v-if="newPassword">
                    <p :class="{ met: passwordRequirements.length }">
                      At least 8 characters
                    </p>
                    <p :class="{ met: passwordRequirements.uppercase }">
                      At least 1 uppercase letter
                    </p>
                    <p :class="{ met: passwordRequirements.lowercase }">
                      At least 1 lowercase letter
                    </p>
                    <p :class="{ met: passwordRequirements.number }">
                      At least 1 number
                    </p>
                    <p :class="{ met: passwordRequirements.symbol }">
                      At least 1 symbol
                    </p>
                  </div>
                </template>
              </Password>
            </div>

            <div class="flex flex-col gap-2">
              <label for="confirmPassword">Confirm Password</label>
              <Password
                id="confirmPassword"
                v-model="confirmPassword"
                required
                :feedback="false"
                toggleMask
                fluid
              />
              <small v-if="confirmPassword && !passwordsMatch" class="p-error"
                >Passwords do not match</small
              >
            </div>

            <Message v-if="error" severity="error" class="mb-2">{{
              error
            }}</Message>

            <Button
              type="submit"
              label="Reset Password"
              :loading="loading"
              :disabled="!isValid"
              fluid
              class="mt-2"
            />
          </div>
        </form>
      </template>
    </Card>
  </div>
</template>

<style scoped>
.auth-container {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 1rem;
}

.auth-card {
  width: 100%;
  max-width: 28rem;
}

.password-requirements p {
  margin: 0;
  padding: 0.25rem 0;
  color: #ef4444;
  font-size: 0.875rem;
}

.password-requirements p.met {
  color: #22c55e;
}
</style>
