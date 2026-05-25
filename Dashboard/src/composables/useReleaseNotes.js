import { useApi } from "./useApi";

export function useReleaseNotes() {
  const { apiFetch, apiFetchJson, showErrorToast, showSuccessToast } = useApi();

  const fetchAll = async () => {
    const result = await apiFetchJson("/release-notes");
    if (!result.ok) {
      showErrorToast(
        result.data?.error || "Failed to fetch release notes",
        result.status
      );
      throw new Error(result.data?.error || "Failed to fetch release notes");
    }
    return result.data;
  };

  const createVersion = async (data) => {
    const result = await apiFetchJson("/release-notes", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    });
    if (!result.ok) {
      showErrorToast(
        result.data?.error || "Failed to create release version",
        result.status
      );
      return null;
    }
    showSuccessToast("Release version created successfully");
    return true;
  };

  const updateVersion = async (id, data) => {
    const result = await apiFetchJson(`/release-notes/${id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data),
    });
    if (!result.ok) {
      showErrorToast(
        result.data?.error || "Failed to update release version",
        result.status
      );
      return false;
    }
    showSuccessToast("Release version updated successfully");
    return true;
  };

  const deleteVersion = async (id) => {
    const result = await apiFetchJson(`/release-notes/${id}`, {
      method: "DELETE",
    });
    if (!result.ok) {
      showErrorToast(
        result.data?.error || "Failed to delete release version",
        result.status
      );
      return false;
    }
    showSuccessToast("Release version deleted successfully");
    return true;
  };

  return {
    fetchAll,
    createVersion,
    updateVersion,
    deleteVersion,
  };
}
