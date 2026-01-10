<script setup>
import { ref, onMounted } from "vue";
import { useRouter, useRoute } from "vue-router";
import { useToast } from "primevue/usetoast";
import InputText from "primevue/inputtext";
import Password from "primevue/password";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";

const router = useRouter();
const route = useRoute();
const toast = useToast();

const username = ref("");
const password = ref("");
const error = ref("");
const loading = ref(false);

onMounted(() => {
  if (route.query.resetSuccess === "true") {
    toast.add({
      severity: "success",
      summary: "Success",
      detail:
        "Password successfully reset. Please login with your new password.",
      life: 5000,
    });
  } else if (route.query.passwordChanged === "true") {
    toast.add({
      severity: "success",
      summary: "Success",
      detail: "Password successfully changed. Please login again.",
      life: 5000,
    });
  }
});

const handleLogin = async () => {
  loading.value = true;
  error.value = "";

  try {
    const backendUrl = import.meta.env.VITE_APP_BACKEND_URL;

    if (!backendUrl) {
      throw new Error(
        "Backend URL not configured. Please set VITE_APP_BACKEND_URL in .env"
      );
    }

    const response = await fetch(`${backendUrl}/auth/login`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      credentials: "include",
      body: JSON.stringify({
        username: username.value,
        password: password.value,
      }),
    });

    const data = await response.json();

    if (!response.ok) {
      throw new Error(data.error || "Login failed");
    }

    // Login successful
    console.log("Login successful", data);

    if (data.requiresPasswordReset) {
      router.push("/reset-password");
      return;
    }

    // Save user info
    localStorage.setItem("mehrak_user", JSON.stringify(data));

    // Redirect to dashboard
    router.push("/dashboard");
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
      <template #title>
        <h2 class="text-center">Dashboard Login</h2>
      </template>
      <template #content>
        <form @submit.prevent="handleLogin">
          <div class="flex flex-col gap-4">
            <div class="flex flex-col gap-2">
              <label for="username">Username</label>
              <InputText id="username" v-model="username" required fluid />
            </div>
            <div class="flex flex-col gap-2">
              <label for="password">Password</label>
              <Password
                id="password"
                v-model="password"
                required
                :feedback="false"
                toggleMask
                fluid
              />
            </div>
            <Message v-if="error" severity="error" class="mb-2">{{
              error
            }}</Message>

            <Button type="submit" label="Login" :loading="loading" fluid />
          </div>
        </form>
        <Button
          label="Back to Home"
          link
          @click="router.push('/')"
          class="mt-2 w-full"
        />
      </template>
    </Card>
  </div>
</template>

<style scoped>
h2 {
  margin-top: 0;
  margin-bottom: 1rem;
  color: var(--primary-color);
}
</style>
