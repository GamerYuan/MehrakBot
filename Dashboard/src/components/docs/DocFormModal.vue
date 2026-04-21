<script setup>
import { ref, watch, computed } from "vue";
import Dialog from "primevue/dialog";
import Button from "primevue/button";
import InputText from "primevue/inputtext";
import Textarea from "primevue/textarea";
import Select from "primevue/select";
import Checkbox from "primevue/checkbox";
import Divider from "primevue/divider";

const props = defineProps({
  visible: Boolean,
  doc: Object,
  isEditing: Boolean,
  userInfo: Object,
});

const emit = defineEmits(["update:visible", "save"]);

const gameOptions = [
  { label: "Genshin Impact", value: "Genshin" },
  { label: "Honkai: Star Rail", value: "HonkaiStarRail" },
  { label: "Zenless Zone Zero", value: "ZenlessZoneZero" },
  { label: "Honkai Impact 3rd", value: "HonkaiImpact3" },
  { label: "Miscellaneous", value: "Unsupported" },
];

const form = ref({
  name: "",
  description: "",
  game: "Genshin",
  parameters: [],
  examples: [],
});

const newParam = ref({
  name: "",
  type: "text",
  description: "",
  required: false,
});

const newExample = ref("");

const paramTypeOptions = [
  { label: "text", value: "text" },
  { label: "number", value: "number" },
  { label: "boolean", value: "boolean" },
  { label: "server", value: "server" },
];

const resetForm = () => {
  form.value = {
    name: "",
    description: "",
    game: "Genshin",
    parameters: [],
    examples: [],
  };
  newParam.value = { name: "", type: "text", description: "", required: false };
  newExample.value = "";
};

watch(
  () => props.visible,
  (visible) => {
    if (visible) {
      if (props.doc && props.isEditing) {
        form.value = {
          name: props.doc.name || "",
          description: props.doc.description || "",
          game: props.doc.game || "Genshin",
          parameters: props.doc.parameters ? [...props.doc.parameters] : [],
          examples: props.doc.examples ? [...props.doc.examples] : [],
        };
      } else {
        resetForm();
      }
    }
  },
);

const addParameter = () => {
  if (!newParam.value.name.trim()) return;
  form.value.parameters.push({
    name: newParam.value.name.trim(),
    type: newParam.value.type,
    description: newParam.value.description.trim(),
    required: newParam.value.required,
  });
  newParam.value = { name: "", type: "text", description: "", required: false };
};

const removeParameter = (index) => {
  form.value.parameters.splice(index, 1);
};

const addExample = () => {
  if (!newExample.value.trim()) return;
  form.value.examples.push(newExample.value.trim());
  newExample.value = "";
};

const removeExample = (index) => {
  form.value.examples.splice(index, 1);
};

const handleSubmit = () => {
  if (!form.value.name.trim() || !form.value.description.trim()) {
    return;
  }
  emit("save", { ...form.value });
};

const handleClose = () => {
  emit("update:visible", false);
};

const canEditGame = computed(() => {
  if (props.userInfo?.isSuperAdmin) return true;
  return false;
});
</script>

<template>
  <Dialog
    :visible="visible"
    @update:visible="emit('update:visible', $event)"
    :header="isEditing ? 'Edit Documentation' : 'Add Documentation'"
    modal
    :style="{ width: '90%', maxWidth: '700px' }"
  >
    <form @submit.prevent="handleSubmit" class="doc-form">
      <div class="flex flex-col gap-4">
        <div class="flex flex-col gap-2">
          <label class="font-semibold">Command Name</label>
          <InputText v-model="form.name" placeholder="e.g., build" required />
        </div>

        <div class="flex flex-col gap-2">
          <label class="font-semibold">Description</label>
          <Textarea
            v-model="form.description"
            placeholder="Brief description of what this command does"
            rows="3"
            required
          />
        </div>

        <div class="flex flex-col gap-2">
          <label class="font-semibold">Game</label>
          <Select
            v-model="form.game"
            :options="gameOptions"
            optionLabel="label"
            optionValue="value"
            :disabled="isEditing && !canEditGame"
          />
        </div>

        <Divider />

        <div class="flex flex-col gap-2">
          <label class="font-semibold">Parameters</label>
          <div class="param-input-container">
            <div class="param-input-col flex-1">
              <div class="param-input-row w-full">
                <InputText
                  v-model="newParam.name"
                  placeholder="Parameter name"
                  class="flex-1"
                />
                <Select
                  v-model="newParam.type"
                  :options="paramTypeOptions"
                  optionLabel="label"
                  optionValue="value"
                />
                <div class="flex items-center gap-2 px-2">
                  <Checkbox
                    v-model="newParam.required"
                    binary
                    inputId="param-required"
                  />
                  <label for="param-required" class="text-sm">Required</label>
                </div>
              </div>
              <InputText
                v-model="newParam.description"
                placeholder="Description"
                class="w-full"
              />
            </div>
            <Button
              type="button"
              icon="pi pi-plus"
              size="small"
              @click="addParameter"
              :disabled="!newParam.name.trim()"
              class="h-full"
            />
          </div>
          <div v-if="form.parameters.length" class="param-list">
            <div
              v-for="(param, index) in form.parameters"
              :key="index"
              class="param-item"
            >
              <span class="param-name">{{ param.name }}</span>
              <span class="param-type-badge">{{ param.type }}</span>
              <span v-if="param.required" class="param-required-badge"
                >Required</span
              >
              <span v-if="param.description" class="param-desc">{{
                param.description
              }}</span>
              <Button
                type="button"
                icon="pi pi-times"
                severity="danger"
                text
                size="small"
                @click="removeParameter(index)"
              />
            </div>
          </div>
        </div>

        <Divider />

        <div class="flex flex-col gap-2">
          <label class="font-semibold">Examples</label>
          <div class="example-input-row">
            <InputText
              v-model="newExample"
              placeholder="Example command usage"
              class="flex-1"
              @keyup.enter="addExample"
            />
            <Button
              type="button"
              icon="pi pi-plus"
              size="small"
              @click="addExample"
              :disabled="!newExample.trim()"
            />
          </div>
          <div v-if="form.examples.length" class="example-list">
            <div
              v-for="(example, index) in form.examples"
              :key="index"
              class="example-item"
            >
              <code>{{ example }}</code>
              <Button
                type="button"
                icon="pi pi-times"
                severity="danger"
                text
                size="small"
                @click="removeExample(index)"
              />
            </div>
          </div>
        </div>
      </div>

      <div class="flex justify-end gap-2 mt-6">
        <Button
          type="button"
          label="Cancel"
          severity="secondary"
          @click="handleClose"
        />
        <Button
          type="submit"
          :label="isEditing ? 'Update' : 'Create'"
          :disabled="!form.name.trim() || !form.description.trim()"
        />
      </div>
    </form>
  </Dialog>
</template>

<style scoped>
.doc-form {
  display: flex;
  flex-direction: column;
}

.param-input-container {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
  padding: 0.5rem 0.75rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 6px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.param-input-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}

.param-input-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
}

.example-input-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.param-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.param-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 6px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.param-name {
  font-weight: 600;
  font-family: monospace;
  color: #fff;
}

.param-type-badge {
  font-size: 0.7rem;
  padding: 0.15rem 0.4rem;
  background: rgba(88, 101, 242, 0.2);
  color: #a0a0ff;
  border-radius: 4px;
  font-family: monospace;
}

.param-required-badge {
  font-size: 0.65rem;
  padding: 0.1rem 0.35rem;
  background: rgba(255, 107, 0, 0.2);
  color: #ff9500;
  border-radius: 4px;
  text-transform: uppercase;
  font-weight: 600;
}

.param-desc {
  flex: 1;
  color: #888;
  font-size: 0.85rem;
}

.example-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: 0.5rem;
}

.example-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0.75rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 6px;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.example-item code {
  flex: 1;
  font-family: monospace;
  font-size: 0.9rem;
  color: #5865f2;
}
</style>
