import { ref, computed } from "vue";

export function usePasswordValidation({ requireCurrentPassword = false } = {}) {
  const newPassword = ref("");
  const confirmPassword = ref("");
  const currentPassword = ref("");

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

  const passwordsMatch = computed(() => {
    return newPassword.value === confirmPassword.value;
  });

  const isValid = computed(() => {
    const baseValid = isPasswordValid.value && passwordsMatch.value;
    if (requireCurrentPassword) {
      return baseValid && currentPassword.value.length > 0;
    }
    return baseValid;
  });

  return {
    newPassword,
    confirmPassword,
    currentPassword,
    passwordRequirements,
    isPasswordValid,
    passwordsMatch,
    isValid,
  };
}
