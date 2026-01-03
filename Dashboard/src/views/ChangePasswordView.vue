<script setup>
import { ref, computed } from "vue";
import { useRouter } from "vue-router";

const router = useRouter();
const currentPassword = ref("");
const newPassword = ref("");
const confirmPassword = ref("");
const error = ref("");
const loading = ref(false);
const showToast = ref(false);
const toastMessage = ref("");
const toastType = ref("error");

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
  showToast.value = false;

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
    toastMessage.value = error.value;
    toastType.value = "error";
    showToast.value = true;

    // Hide toast after 5 seconds
    setTimeout(() => {
      showToast.value = false;
    }, 5000);
  } finally {
    loading.value = false;
  }
};
</script>

<template>
  <div class="change-password-container">
    <div v-if="showToast" class="toast" :class="toastType">
      {{ toastMessage }}
    </div>

    <div class="card change-password-card">
      <h2>Change Password</h2>

      <form @submit.prevent="handleChangePassword">
        <div class="form-group">
          <label for="currentPassword">Current Password</label>
          <input
            type="password"
            id="currentPassword"
            v-model="currentPassword"
            required
          />
        </div>

        <div class="form-group">
          <label for="newPassword">New Password</label>
          <input
            type="password"
            id="newPassword"
            v-model="newPassword"
            required
          />
          <div class="requirements" v-if="newPassword">
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
        </div>

        <div class="form-group">
          <label for="confirmPassword">Confirm New Password</label>
          <input
            type="password"
            id="confirmPassword"
            v-model="confirmPassword"
            required
          />
          <span v-if="confirmPassword && !passwordsMatch" class="error-text"
            >Passwords do not match</span
          >
        </div>

        <div class="actions">
          <button
            type="button"
            @click="router.push('/dashboard')"
            class="btn secondary"
          >
            Cancel
          </button>
          <button
            type="submit"
            :disabled="loading || !isValid"
            class="btn primary"
          >
            {{ loading ? "Changing..." : "Change Password" }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<style scoped>
.change-password-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 80vh;
}

.change-password-card {
  width: 100%;
  max-width: 400px;
}

h2 {
  margin-top: 0;
  margin-bottom: 2rem;
  color: var(--primary-color);
}

.requirements {
  margin-top: 0.5rem;
  font-size: 0.8rem;
  color: #888;
}

.requirements p {
  margin: 0.2rem 0;
  display: flex;
  align-items: center;
}

.requirements p::before {
  content: "○";
  margin-right: 0.5rem;
  font-weight: bold;
}

.requirements p.met {
  color: #4caf50;
}

.requirements p.met::before {
  content: "✓";
}

.actions {
  display: flex;
  gap: 1rem;
  margin-top: 2rem;
}

.btn {
  flex: 1;
}
</style>
