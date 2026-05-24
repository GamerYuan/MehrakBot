<script setup>
import { ref, onMounted } from "vue";
import { useRouter, useRoute } from "vue-router";
import { useApi } from "../composables/useApi";
import { useToast } from "primevue/usetoast";
import InputText from "primevue/inputtext";
import Password from "primevue/password";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";

const router = useRouter();
const route = useRoute();
const { apiFetch } = useApi();
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
    const response = await apiFetch("/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      skipAuthRedirect: true,
      body: JSON.stringify({
        username: username.value,
        password: password.value,
      }),
    });

    const data = await response.json();

    if (!response.ok) {
      throw new Error(data.error || "Login failed");
    }

    if (data.requiresPasswordReset) {
      router.push("/reset-password");
      return;
    }

    localStorage.setItem("mehrak_user", JSON.stringify(data));
    router.push("/dashboard");
  } catch (err) {
    if (err._redirected) return;
    error.value = err.message || "An error occurred";
  } finally {
    loading.value = false;
  }
};
</script>

<template>
  <div class="flex items-center justify-center min-h-screen p-4">
    <Card class="w-full max-w-md">
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
