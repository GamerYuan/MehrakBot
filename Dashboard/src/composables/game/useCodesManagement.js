import { ref, computed } from "vue";
import { useConfirm } from "primevue/useconfirm";
import { useApi } from "../useApi";

export function useCodesManagement(config, activeTab) {
  const { showErrorToast, showSuccessToast, buildError, apiFetch, apiFetchJson } = useApi();
  const confirm = useConfirm();

  const codes = ref([]);
  const selectedCodes = ref([]);
  const newCodesInput = ref("");
  const codesSearchQuery = ref("");
  const codesLoading = ref(false);

  const fetchCodes = async () => {
    try {
      const { ok, data, status } = await apiFetchJson(
        `/codes/list?game=${config.id}`,
      );
      if (ok) {
        codes.value = data.codes.map((c) => ({ code: c }));
      } else {
        showErrorToast(data.error || "Failed to fetch codes", status);
      }
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    }
  };

  const filteredCodes = computed(() => {
    if (!codesSearchQuery.value) return codes.value;
    const query = codesSearchQuery.value.toLowerCase();
    return codes.value.filter((c) => c.code.toLowerCase().includes(query));
  });

  const confirmAddCodes = () => {
    if (!newCodesInput.value) return;
    confirm.require({
      message: "Are you sure you want to add these codes?",
      header: "Confirm Add",
      icon: "pi pi-exclamation-triangle",
      accept: executeAddCodes,
    });
  };

  const executeAddCodes = async () => {
    codesLoading.value = true;
    try {
      const codesToAdd = newCodesInput.value
        .split(",")
        .map((c) => c.trim())
        .filter((c) => c);

      const response = await apiFetch(`/codes/add?game=${config.id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Codes: codesToAdd }),
      });

      if (!response.ok) {
        const data = await response.json();
        throw buildError(data.error || "Failed to add codes", response.status);
      }

      newCodesInput.value = "";
      await fetchCodes();
      showSuccessToast("Codes added successfully");
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      codesLoading.value = false;
    }
  };

  const confirmDeleteCodes = (codesList) => {
    confirm.require({
      message: `Are you sure you want to delete ${codesList.length} code(s)?`,
      header: "Confirm Delete",
      icon: "pi pi-exclamation-triangle",
      rejectProps: {
        label: "Cancel",
        severity: "secondary",
        outlined: true,
      },
      acceptProps: {
        label: "Delete",
        severity: "danger",
      },
      accept: () => executeDeleteCodes(codesList),
    });
  };

  const executeDeleteCodes = async (codesList) => {
    codesLoading.value = true;
    try {
      const params = new URLSearchParams();
      params.append("game", config.id);
      codesList.forEach((c) => params.append("codes", c));
      const response = await apiFetch(
        `/codes/remove?${params.toString()}`,
        { method: "DELETE" },
      );

      if (!response.ok) {
        const data = await response.json();
        throw buildError(data.error || "Failed to delete codes", response.status);
      }

      selectedCodes.value = [];
      await fetchCodes();
      showSuccessToast("Codes deleted successfully");
    } catch (err) {
      if (err._redirected) return;
      showErrorToast(err.message, err.status);
    } finally {
      codesLoading.value = false;
    }
  };

  return {
    codes,
    selectedCodes,
    newCodesInput,
    codesSearchQuery,
    codesLoading,
    filteredCodes,
    fetchCodes,
    confirmAddCodes,
    confirmDeleteCodes,
  };
}