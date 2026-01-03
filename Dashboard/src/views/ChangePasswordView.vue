<script setup>
import { ref, computed } from "vue";
import { useRouter } from "vue-router";
import { useToast } from "primevue/usetoast";
import Password from "primevue/password";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";

const router = useRouter();
const toast = useToast();

const currentPassword = ref("");
const newPassword = ref("");
const confirmPassword = ref("");
const error = ref("");
const loading = ref(false);

const passwordsMatch = computed(() => {
  return newPassword.value === confirmPassword.value;
});

const passwordRequirements = computed(() => {
  const pwd = newPassword.value;
  return {
    length: pwd.length >= 8,
    uppercase: /[A-Z]/.test(pwd),
    lowercase: /[a-z]/.test(pwd),
    number: /\d/.test(pwd),
    symbol: /[\W_]/.test(pwd),
  };
});

const isPasswordValid = computed(() => {
  const r = passwordRequirements.value;
  return r.length && r.uppercase && r.lowercase && r.number && r.symbol;
});

const isValid = computed(() => {
  return (
    currentPassword.value.length > 0 &&
    isPasswordValid.value &&
    passwordsMatch.value
  );
});

const handleChangePassword = async () => {
  if (!isValid.value) return;

  loading.value = true;
  error.value = "";

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    if (!backendUrl) throw new Error("Backend URL not configured");

    const response = await fetch(`${backendUrl}/auth/password`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      credentials: "include",
      body: JSON.stringify({
        currentPassword: currentPassword.value,
        newPassword: newPassword.value,
      }),
    });

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    const data = await response.json();

    if (!response.ok) {
      throw new Error(data.error || "Failed to change password");
    }

    // Success
    // Clear local storage as the session might be invalidated or user needs to re-login
    localStorage.removeItem("mehrak_user");

    // Redirect to login with success flag
    router.push("/login?passwordChanged=true");
  } catch (err) {
    console.error(err);
    error.value = err.message || "An error occurred";
    toast.add({
      severity: "error",
      summary: "Error",
      detail: error.value,
      life: 5000,
    });
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

<style scoped></style>
