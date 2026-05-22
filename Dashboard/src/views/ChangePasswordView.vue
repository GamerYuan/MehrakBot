<script setup>
import { ref, computed } from "vue";
import { useRouter } from "vue-router";
import { useApi } from "../composables/useApi";
import { usePasswordValidation } from "../composables/usePasswordValidation";
import Password from "primevue/password";
import Button from "primevue/button";
import Card from "primevue/card";

const router = useRouter();
const { apiFetch } = useApi();

const currentPassword = ref("");
const { newPassword, confirmPassword, passwordsMatch, isPasswordValid, passwordRequirements, isValid: isFormValid } = usePasswordValidation({ requireCurrentPassword: true });

const currentPasswordFilled = computed(() => currentPassword.value.length > 0);
const isValid = computed(() => currentPasswordFilled.value && isFormValid.value);

const error = ref("");
const loading = ref(false);

const handleChangePassword = async () => {
  if (!isValid.value) return;

  loading.value = true;
  error.value = "";

  try {
    const response = await apiFetch("/auth/password", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        currentPassword: currentPassword.value,
        newPassword: newPassword.value,
      }),
    });

    if (!response.ok) {
      const data = await response.json().catch(() => ({}));
      throw new Error(data.error || "Failed to change password");
    }

    localStorage.removeItem("mehrak_user");
    router.push("/login?passwordChanged=true");
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
      <template #title>Change Password</template>
      <template #content>
        <form @submit.prevent="handleChangePassword">
          <div class="flex flex-col gap-4">
            <div class="flex flex-col gap-2">
              <label for="currentPassword">Current Password</label>
              <Password
                id="currentPassword"
                v-model="currentPassword"
                required
                :feedback="false"
                toggleMask
                fluid
              />
            </div>

            <div class="flex flex-col gap-2">
              <label for="newPassword">New Password</label>
              <Password
                id="newPassword"
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
              <label for="confirmPassword">Confirm New Password</label>
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

            <div class="flex gap-2 mt-4">
              <Button
                label="Cancel"
                severity="secondary"
                outlined
                class="flex-1"
                @click="router.push('/dashboard')"
              />
              <Button
                type="submit"
                label="Change Password"
                :loading="loading"
                :disabled="!isValid"
                class="flex-1"
              />
            </div>
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