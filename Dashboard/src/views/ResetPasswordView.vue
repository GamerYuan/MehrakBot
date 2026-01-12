<script setup>
import { ref, computed } from "vue";
import { useRouter } from "vue-router";
import Password from "primevue/password";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";

const router = useRouter();
const password = ref("");
const confirmPassword = ref("");
const error = ref("");
const loading = ref(false);

const passwordsMatch = computed(() => {
  return password.value === confirmPassword.value;
});

const passwordRequirements = computed(() => {
  const pwd = password.value;
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
  return isPasswordValid.value && passwordsMatch.value;
});

const handleReset = async () => {
  if (!isValid.value) return;

  loading.value = true;
  error.value = "";

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;
    if (!backendUrl) throw new Error("Backend URL not configured");

    const response = await fetch(`${backendUrl}/auth/password/reset`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      credentials: "include", // Important to send the session cookie
      body: JSON.stringify({
        newPassword: password.value,
      }),
    });

    if (response.status === 401) {
      router.push("/login");
      return;
    }

    const data = await response.json();

    if (!response.ok) {
      throw new Error(data.error || "Password reset failed");
    }

    // Redirect to login with success flag
    router.push("/login?resetSuccess=true");
  } catch (err) {
    console.error(err);
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
                v-model="password"
                required
                toggleMask
                fluid
              >
                <template #footer>
                  <div class="password-requirements" v-if="password">
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

<style scoped></style>
