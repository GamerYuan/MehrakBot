<script setup>
import { ref, onMounted, computed } from "vue";
import { useReleaseNotes } from "../composables/useReleaseNotes";
import Card from "primevue/card";
import Button from "primevue/button";
import InputText from "primevue/inputtext";
import InputNumber from "primevue/inputnumber";
import Textarea from "primevue/textarea";
import Select from "primevue/select";
import Dialog from "primevue/dialog";
import ProgressSpinner from "primevue/progressspinner";
import { useConfirm } from "primevue/useconfirm";
import { useToast } from "primevue/usetoast";

const { fetchAll, createVersion, updateVersion, deleteVersion } = useReleaseNotes();
const confirm = useConfirm();
const toast = useToast();

const releases = ref([]);
const loading = ref(false);
const showModal = ref(false);
const isEditing = ref(false);
const editingId = ref(null);

const emptySection = () => ({
  name: "",
  notes: [emptyNote()],
});

const emptyNote = () => ({
  type: "feature",
  text: "",
});

const form = ref({
  version: "",
  date: "",
  displayOrder: 0,
  sections: [emptySection()],
});

const noteTypes = [
  { label: "Feature", value: "feature" },
  { label: "Improvement", value: "improvement" },
  { label: "Fix", value: "fix" },
];

const sortedReleases = computed(() => {
  return [...releases.value].sort((a, b) => b.displayOrder - a.displayOrder);
});

const loadReleases = async () => {
  loading.value = true;
  try {
    releases.value = await fetchAll();
  } finally {
    loading.value = false;
  }
};

const openAddModal = () => {
  isEditing.value = false;
  editingId.value = null;
  const maxOrder = releases.value.reduce((max, r) => Math.max(max, r.displayOrder), 0);
  form.value = {
    version: "",
    date: "",
    displayOrder: maxOrder + 1,
    sections: [emptySection()],
  };
  showModal.value = true;
};

const openEditModal = (release) => {
  isEditing.value = true;
  editingId.value = release.id;
  form.value = {
    version: release.version,
    date: release.date,
    displayOrder: release.displayOrder,
    sections: release.sections.map((s) => ({
      name: s.name,
      notes: s.notes.map((n) => ({
        type: n.type,
        text: n.text,
      })),
    })),
  };
  showModal.value = true;
};

const addSection = () => {
  form.value.sections.push(emptySection());
};

const removeSection = (index) => {
  form.value.sections.splice(index, 1);
};

const addNote = (sectionIndex) => {
  form.value.sections[sectionIndex].notes.push(emptyNote());
};

const removeNote = (sectionIndex, noteIndex) => {
  form.value.sections[sectionIndex].notes.splice(noteIndex, 1);
};

const handleSave = async () => {
  if (!form.value.version.trim()) {
    toast.add({
      severity: "warn",
      summary: "Validation",
      detail: "Version is required",
      life: 3000,
    });
    return;
  }

  const payload = {
    version: form.value.version.trim(),
    date: form.value.date?.trim() || "",
    displayOrder: form.value.displayOrder,
    sections: form.value.sections
      .filter((s) => s.name.trim())
      .map((s) => ({
        name: s.name.trim(),
        notes: s.notes
          .filter((n) => n.text.trim())
          .map((n) => ({
            type: n.type.trim().toLowerCase(),
            text: n.text.trim(),
          })),
      })),
  };

  if (payload.sections.length === 0) {
    toast.add({
      severity: "warn",
      summary: "Validation",
      detail: "At least one section is required",
      life: 3000,
    });
    return;
  }

  let success;
  if (isEditing.value) {
    success = await updateVersion(editingId.value, payload);
  } else {
    await createVersion(payload);
    success = true;
  }

  if (success) {
    showModal.value = false;
    await loadReleases();
  }
};

const confirmDelete = (release) => {
  confirm.require({
    message: `Are you sure you want to delete version ${release.version}?`,
    header: "Confirm Delete",
    icon: "pi pi-exclamation-triangle",
    accept: async () => {
      const success = await deleteVersion(release.id);
      if (success) {
        await loadReleases();
      }
    },
  });
};

onMounted(loadReleases);
</script>

<template>
  <div class="flex flex-col gap-6 p-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <div class="flex items-center justify-between">
          <div>
            <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
              Release Notes Management
            </h2>
            <p class="text-zinc-300 leading-relaxed">
              Add, edit, or remove release versions and their notes
            </p>
          </div>
          <Button
            icon="pi pi-plus"
            label="Add Version"
            class="bg-emerald-500/20! border-emerald-500/50! text-emerald-300!"
            @click="openAddModal"
          />
        </div>
      </template>
    </Card>

    <div v-if="loading" class="flex justify-center py-12">
      <ProgressSpinner />
    </div>

    <div v-else-if="!sortedReleases.length" class="text-center py-12 text-zinc-400">
      No release versions found. Click "Add Version" to create one.
    </div>

    <div v-else class="grid grid-cols-1 lg:grid-cols-[280px_1fr] gap-6">
      <aside class="h-fit">
        <Card class="bg-white/5 border border-white/10 rounded-2xl">
          <template #content>
            <h4
              class="text-sm font-semibold text-zinc-400 uppercase tracking-wider mb-3"
            >
              Versions
            </h4>
            <nav class="flex flex-col gap-1 max-h-[calc(100vh-300px)] overflow-y-auto pr-1 scrollbar-thin scrollbar-thumb-white/10 scrollbar-track-transparent">
              <button
                v-for="release in sortedReleases"
                :key="release.id"
                type="button"
                class="text-left px-3 py-2 rounded-lg text-sm transition-all text-zinc-300 hover:text-zinc-100 hover:bg-white/5 border border-transparent hover:border-white/10"
                @click="openEditModal(release)"
              >
                <span class="font-mono text-xs">{{ release.version }}</span>
                <span v-if="release.date" class="text-zinc-500 ml-2">{{ release.date }}</span>
              </button>
            </nav>
          </template>
        </Card>
      </aside>

      <div class="flex flex-col gap-4">
        <Card
          v-for="release in sortedReleases"
          :key="release.id"
          class="bg-white/5 border border-white/10 rounded-2xl"
        >
          <template #content>
            <div class="flex items-center justify-between mb-4">
              <div>
                <h3 class="text-xl font-bold text-zinc-100 font-mono">
                  {{ release.version }}
                </h3>
                <span v-if="release.date" class="text-sm text-zinc-500">{{ release.date }}</span>
              </div>
              <div class="flex gap-2">
                <Button
                  icon="pi pi-pencil"
                  severity="secondary"
                  text
                  class="text-zinc-400 hover:text-zinc-200"
                  @click="openEditModal(release)"
                />
                <Button
                  icon="pi pi-trash"
                  severity="danger"
                  text
                  class="text-red-400 hover:text-red-300"
                  @click="confirmDelete(release)"
                />
              </div>
            </div>

            <div
              v-for="section in release.sections"
              :key="section.name"
              class="flex flex-col gap-2 mb-4"
            >
              <h4 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider border-b border-white/10 pb-2">
                {{ section.name }}
              </h4>
              <ul class="flex flex-col gap-1.5">
                <li
                  v-for="(note, idx) in section.notes"
                  :key="idx"
                  class="flex items-start gap-2"
                >
                  <span
                    :class="[
                      'text-xs font-semibold px-2 py-0.5 rounded shrink-0 mt-0.5',
                      note.type === 'feature'
                        ? 'bg-emerald-500/20 text-emerald-300'
                        : note.type === 'improvement'
                          ? 'bg-blue-500/20 text-blue-300'
                          : 'bg-orange-500/20 text-orange-300',
                    ]"
                  >
                    {{ note.type }}
                  </span>
                  <span class="text-zinc-300 text-sm">{{ note.text }}</span>
                </li>
              </ul>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <Dialog
      v-model:visible="showModal"
      :header="isEditing ? 'Edit Release Version' : 'Add Release Version'"
      modal
      :style="{ width: '700px' }"
      class="bg-zinc-900 border border-white/10"
    >
      <div class="flex flex-col gap-4">
        <div class="grid grid-cols-2 gap-4">
          <div class="flex flex-col gap-2">
            <label class="text-sm text-zinc-400">Version</label>
            <InputText
              v-model="form.version"
              placeholder="e.g. v1.2.0"
              class="w-full"
            />
          </div>
          <div class="flex flex-col gap-2">
            <label class="text-sm text-zinc-400">Date</label>
            <InputText
              v-model="form.date"
              placeholder="e.g. 2024-01-15"
              class="w-full"
            />
          </div>
        </div>

        <div class="flex flex-col gap-2">
          <label class="text-sm text-zinc-400">Display Order</label>
          <InputNumber v-model="form.displayOrder" class="w-full" />
        </div>

        <div class="flex flex-col gap-4">
          <div
            v-for="(section, sIndex) in form.sections"
            :key="sIndex"
            class="flex flex-col gap-3 p-4 rounded-xl bg-white/5 border border-white/10"
          >
            <div class="flex items-center justify-between gap-2">
              <InputText
                v-model="section.name"
                placeholder="Section name (e.g. Common)"
                class="flex-1"
              />
              <Button
                icon="pi pi-trash"
                severity="danger"
                text
                class="text-red-400 shrink-0"
                @click="removeSection(sIndex)"
              />
            </div>

            <div class="flex flex-col gap-2 pl-4 border-l-2 border-white/10">
              <div
                v-for="(note, nIndex) in section.notes"
                :key="nIndex"
                class="flex flex-col gap-2"
              >
                <div class="flex items-start gap-2">
                  <Select
                    v-model="note.type"
                    :options="noteTypes"
                    option-label="label"
                    option-value="value"
                    placeholder="Type"
                    class="w-36 shrink-0"
                  />
                  <Textarea
                    v-model="note.text"
                    placeholder="Note text"
                    rows="2"
                    class="flex-1 min-w-0"
                  />
                  <Button
                    icon="pi pi-trash"
                    severity="danger"
                    text
                    class="text-red-400 shrink-0"
                    @click="removeNote(sIndex, nIndex)"
                  />
                </div>
              </div>
              <Button
                icon="pi pi-plus"
                label="Add Note"
                text
                class="text-emerald-400 justify-start"
                @click="addNote(sIndex)"
              />
            </div>
          </div>
        </div>

        <Button
          icon="pi pi-plus"
          label="Add Section"
          text
          class="text-emerald-400 justify-start"
          @click="addSection"
        />

        <div class="flex justify-end gap-2 pt-2 border-t border-white/10">
          <Button
            label="Cancel"
            severity="secondary"
            text
            @click="showModal = false"
          />
          <Button
            label="Save"
            class="bg-emerald-500/20! border-emerald-500/50! text-emerald-300!"
            @click="handleSave"
          />
        </div>
      </div>
    </Dialog>
  </div>
</template>
