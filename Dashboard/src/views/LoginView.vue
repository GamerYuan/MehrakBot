<script setup>
import { ref, onMounted } from "vue";
import { useRouter, useRoute } from "vue-router";

const router = useRouter();
const route = useRoute();
const username = ref("");
const password = ref("");
const error = ref("");
const loading = ref(false);
const showToast = ref(false);
const toastMessage = ref("");

onMounted(() => {
  if (route.query.resetSuccess === "true") {
    toastMessage.value =
      "Password successfully reset. Please login with your new password.";
    showToast.value = true;
    setTimeout(() => {
      showToast.value = false;
    }, 5000);
  } else if (route.query.passwordChanged === "true") {
    toastMessage.value = "Password successfully changed. Please login again.";
    showToast.value = true;
    setTimeout(() => {
      showToast.value = false;
    }, 5000);
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
  <div class="login-container">
    <div v-if="showToast" class="toast success">
      {{ toastMessage }}
    </div>
    <div class="card login-card">
      <h2>Dashboard Login</h2>
      <form @submit.prevent="handleLogin">
        <div class="form-group">
          <label for="username">Username</label>
          <input type="text" id="username" v-model="username" required />
        </div>
        <div class="form-group">
          <label for="password">Password</label>
          <input type="password" id="password" v-model="password" required />
        </div>
        <div v-if="error" class="error-text mb-2">{{ error }}</div>

        <button
          type="submit"
          :disabled="loading"
          class="btn primary full-width"
        >
          {{ loading ? "Logging in..." : "Login" }}
        </button>
      </form>
      <button @click="router.push('/')" class="btn text mt-2">
        Back to Home
      </button>
    </div>
  </div>
</template>

<style scoped>
.login-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 80vh;
}

.login-card {
  width: 100%;
  max-width: 400px;
}

h2 {
  margin-top: 0;
  margin-bottom: 2rem;
  color: var(--primary-color);
}
</style>
