<script setup>
import { ref, computed } from "vue";
import { useRouter } from "vue-router";

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
  <div class="reset-container">
    <div class="reset-card">
      <h2>Reset Password</h2>
      <p class="instruction">You must reset your password to continue.</p>

      <form @submit.prevent="handleReset">
        <div class="form-group">
          <label for="password">New Password</label>
          <input type="password" id="password" v-model="password" required />
          <div class="requirements" v-if="password">
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
          <label for="confirmPassword">Confirm Password</label>
          <input
            type="password"
            id="confirmPassword"
            v-model="confirmPassword"
            required
          />
          <span
            v-if="confirmPassword && !passwordsMatch"
            class="validation-error"
            >Passwords do not match</span
          >
        </div>

        <div v-if="error" class="error">{{ error }}</div>

        <button
          type="submit"
          :disabled="loading || !isValid"
          class="btn primary full-width"
        >
          {{ loading ? "Resetting..." : "Reset Password" }}
        </button>
      </form>
    </div>
  </div>
</template>

<style scoped>
.reset-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 80vh;
}

.reset-card {
  background-color: var(--card-bg);
  padding: 2.5rem;
  border-radius: 12px;
  width: 100%;
  max-width: 400px;
  border: 1px solid #333;
  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.3);
}

h2 {
  margin-top: 0;
  color: var(--primary-color);
}

.instruction {
  margin-bottom: 2rem;
  color: #aaa;
  font-size: 0.9rem;
}

.form-group {
  margin-bottom: 1.5rem;
  text-align: left;
}

label {
  display: block;
  margin-bottom: 0.5rem;
  color: #ccc;
}

input {
  width: 100%;
  padding: 0.8rem;
  border-radius: 6px;
  border: 1px solid #444;
  background-color: #1a1a1a;
  color: white;
  font-size: 1rem;
  box-sizing: border-box;
}

input:focus {
  outline: none;
  border-color: var(--primary-color);
}

.validation-error {
  color: #ff4444;
  font-size: 0.8rem;
  margin-top: 0.25rem;
  display: block;
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

.btn {
  padding: 0.8rem 1.5rem;
  border-radius: 8px;
  font-weight: bold;
  cursor: pointer;
  border: none;
  font-size: 1rem;
  transition: background-color 0.2s;
  box-sizing: border-box;
}

.btn.primary {
  background-color: var(--primary-color);
  color: white;
}

.btn.primary:hover {
  background-color: #4752c4;
}

.btn.primary:disabled {
  background-color: #444;
  cursor: not-allowed;
}

.full-width {
  width: 100%;
  margin-top: 1rem;
}

.error {
  color: #ff4444;
  margin-bottom: 1rem;
  font-size: 0.9rem;
}
</style>
